/* Chromium loads the core as a classic worker script; Firefox lists both background scripts. */
if (typeof importScripts === "function") importScripts("core.js");
(() => {
  const api = globalThis.browser || globalThis.chrome;
  const firefox = typeof globalThis.browser !== "undefined" && /Firefox\//.test(navigator.userAgent);
  const browserName = firefox ? "Firefox" : /Edg\//.test(navigator.userAgent) ? "Edge" : "Chrome";
  let port, controller, identity, retryTimer, snapshotPending, eventTimer, snapshotDirty;
  const ready = (async () => {
    const local = await api.storage.local.get(["profileId", "profileName"]);
    const session = await api.storage.session.get("sessionId");
    identity = { profileId: local.profileId || crypto.randomUUID(), profileName: local.profileName || browserName,
      sessionId: session.sessionId || crypto.randomUUID() };
    await api.storage.local.set({ profileId: identity.profileId, profileName: identity.profileName });
    await api.storage.session.set({ sessionId: identity.sessionId });
    controller = new AppSwitcherManagedTabs(api, identity.sessionId, firefox);
  })();
  function send(message) { try { port?.postMessage({ protocol: 1, ...message }); } catch { } }
  function snapshot() {
    if (!port) return Promise.resolve();
    if (snapshotPending) { snapshotDirty = true; return snapshotPending; }
    snapshotPending = (async () => {
      await ready;
      try { send({ type: "state", states: await controller.snapshot() }); } catch { send({ type: "state", states: [] }); }
    })().finally(() => { snapshotPending = undefined; if (snapshotDirty) { snapshotDirty = false; scheduleSnapshot(); } });
    return snapshotPending;
  }
  function scheduleSnapshot() {
    clearTimeout(eventTimer);
    eventTimer = setTimeout(snapshot, 100);
  }
  async function connect() {
    await ready;
    if (port) return;
    try {
      port = api.runtime.connectNative("com.appswitcher.browser");
      port.onDisconnect.addListener(() => {
        void api.runtime.lastError;
        port = undefined;
        clearTimeout(retryTimer);
        retryTimer = setTimeout(connect, 5000);
      });
      port.onMessage.addListener(async message => {
        if (message.protocol !== 1 || message.type !== "request" || !message.requestId) return;
        let reply;
        try { reply = await controller.handle(message.request); }
        catch { reply = { code: "Recovering", state: "Unknown" }; }
        send({ type: "reply", requestId: message.requestId, reply });
        await snapshot();
      });
      send({ type: "hello", browser: browserName, extensionVersion: api.runtime.getManifest().version, ...identity });
      await snapshot();
    } catch { clearTimeout(retryTimer); retryTimer = setTimeout(connect, 5000); }
  }
  api.tabs.onRemoved.addListener((id, info) => { ready.then(() => controller.removed(id, info)).then(snapshot).catch(() => {}); });
  api.tabs.onReplaced?.addListener((added, removed) => { ready.then(() => controller.replaced(added, removed)).then(snapshot).catch(() => {}); });
  api.tabs.onUpdated.addListener((_id, change) => {
    if (["url", "status", "groupId", "pinned"].some(k => k in change)) scheduleSnapshot();
  });
  api.tabGroups.onUpdated?.addListener(scheduleSnapshot);
  api.tabGroups.onRemoved?.addListener(scheduleSnapshot);
  api.tabs.onAttached.addListener(scheduleSnapshot);
  api.tabs.onDetached.addListener(scheduleSnapshot);
  api.runtime.onStartup.addListener(connect);
  api.runtime.onInstalled.addListener(connect);
  api.alarms.onAlarm.addListener(() => { connect(); snapshot(); });
  api.alarms.create("appswitcher-reconnect", { periodInMinutes: 0.5 });
  setInterval(snapshot, 5000);
  connect();
})();
