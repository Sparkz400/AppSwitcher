import test from "node:test";
import assert from "node:assert/strict";
import { randomUUID } from "node:crypto";
import "../core.js";

function fakeBrowser() {
  const state = { tabs: new Map(), windows: new Map([[1, { id: 1, state: "normal", focused: true }]]), groups: new Map(),
    storage: {}, tags: new Map(), nextTab: 1, nextGroup: 1, calls: [], fail: null, counts: {}, denyFocus: false };
  const wrap = (name, fn) => async (...args) => {
    state.calls.push({ name, args: structuredClone(args) });
    state.counts[name] = (state.counts[name] || 0) + 1;
    const result = await fn(...args);
    if (state.fail?.name === name && state.fail.count === state.counts[name]) { state.fail = null; throw new Error("Lost response after side effect"); }
    return structuredClone(result);
  };
  const api = {
    runtime: { getURL: path => `chrome-extension://aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/${path}` },
    storage: { local: {
      get: wrap("storage.get", k => k === null ? state.storage : { [k]: state.storage[k] }),
      set: wrap("storage.set", obj => { Object.assign(state.storage, structuredClone(obj)); })
    } },
    tabs: {
      query: wrap("tabs.query", () => [...state.tabs.values()]),
      get: wrap("tabs.get", id => { if (!state.tabs.has(id)) throw new Error("No tab"); return state.tabs.get(id); }),
      create: wrap("tabs.create", props => {
        const tab = { id: state.nextTab++, windowId: props.windowId || 1, groupId: -1, active: false, ...props };
        state.tabs.set(tab.id, tab); return tab;
      }),
      update: wrap("tabs.update", (id, props) => {
        const tab = state.tabs.get(id); if (!tab) throw new Error("No tab");
        if (props.active) for (const t of state.tabs.values()) if (t.windowId === tab.windowId) t.active = false;
        Object.assign(tab, props); return tab;
      }),
      group: wrap("tabs.group", ({ tabIds }) => {
        const id = state.nextGroup++; state.groups.set(id, { id, collapsed: false });
        for (const tabId of tabIds) state.tabs.get(tabId).groupId = id;
        return id;
      }),
      remove: wrap("tabs.remove", ids => { for (const id of Array.isArray(ids) ? ids : [ids]) { state.tabs.delete(id); state.tags.delete(id); } })
    },
    windows: {
      get: wrap("windows.get", id => state.windows.get(id)),
      update: wrap("windows.update", (id, props) => {
        const w = state.windows.get(id);
        if (!state.denyFocus) Object.assign(w, props);
        return w;
      })
    },
    tabGroups: { update: wrap("tabGroups.update", (id, props) => { Object.assign(state.groups.get(id), props); return state.groups.get(id); }) },
    sessions: {
      getTabValue: wrap("sessions.get", id => state.tags.get(id)),
      setTabValue: wrap("sessions.set", (id, _key, value) => { state.tags.set(id, structuredClone(value)); })
    }
  };
  return { state, api, controller: new AppSwitcherManagedTabs(api, "session-1") };
}
const request = () => ({ action: "ensure", appId: randomUUID(), generation: randomUUID(), url: "https://192.168.1.78:8443/hmi?a=1#panel", name: "HMI", allowCreate: true, focus: true });
const pages = state => [...state.tabs.values()].filter(t => /^https?:/.test(t.url));

test("new marker tabs with only pendingUrl finish creation on the first click", async () => {
  const { api, state, controller } = fakeBrowser(), r = request();
  const create = api.tabs.create;
  api.tabs.create = async props => {
    const tab = await create(props);
    Object.assign(state.tabs.get(tab.id), { url: "", pendingUrl: props.url });
    return { ...tab, url: "", pendingUrl: props.url };
  };
  const result = await controller.handle(r);
  assert.equal(result.code, "CreatedNew");
  assert.equal(pages(state).length, 1);
});

test("lost primary-close response can finish closing without touching another tab", async () => {
  const { api, state, controller } = fakeBrowser(), r = request();
  await controller.handle(r);
  const unrelated = await api.tabs.create({ url: r.url });
  state.fail = { name: "tabs.remove", count: 1 };
  await assert.rejects(controller.handle({ ...r, action: "close", allowCreate: false }));
  const retry = await controller.handle({ ...r, action: "close", allowCreate: false });
  assert.equal(retry.code, "Closed");
  assert.deepEqual([...state.tabs.keys()], [unrelated.id]);
});

test("Firefox initial about:blank is awaited without repeating either create", async () => {
  const { api, state, controller } = fakeBrowser(), r = request();
  const create = api.tabs.create;
  api.tabs.create = async props => {
    const tab = await create(props);
    state.tabs.get(tab.id).url = "about:blank";
    setTimeout(() => { state.tabs.get(tab.id).url = props.url; }, 30);
    return { ...tab, url: "about:blank" };
  };
  assert.equal((await controller.handle(r)).code, "CreatedNew");
  assert.equal(state.counts["tabs.create"], 2);
});

test("a reserved replacement can be inspected again after an interrupted pre-launch hook", async () => {
  const { api, controller, state } = fakeBrowser(), r = request();
  const first = await controller.handle(r);
  await api.tabs.remove(first.tabId);
  await controller.removed(first.tabId, { isWindowClosing: false });
  const replacement = { ...r, generation: randomUUID() };
  assert.equal((await controller.handle({ ...replacement, action: "inspect" })).state, "Absent");
  assert.equal((await controller.handle(replacement)).code, "CreatedNew");
  assert.equal(state.tabs.size, 2);
});

test("repeated minimized activation selects exactly the original tab without navigation", async () => {
  const { state, controller } = fakeBrowser(), r = request();
  const first = await controller.handle(r);
  assert.equal(first.code, "CreatedNew");
  const navigationCount = state.calls.filter(c => c.name === "tabs.update" && c.args[1].url).length;
  for (let i = 0; i < 100; i++) {
    state.windows.get(1).state = "minimized";
    state.windows.get(1).focused = false;
    const result = await controller.handle({ ...r, allowCreate: false });
    assert.equal(result.code, "ActivatedExisting"); assert.equal(result.tabId, first.tabId);
  }
  assert.equal(pages(state).length, 1); assert.equal(state.tabs.size, 2);
  assert.equal(state.calls.filter(c => c.name === "tabs.update" && c.args[1].url).length, navigationCount);
});

test("simultaneous launches serialize to one creation", async () => {
  const { state, controller } = fakeBrowser(), r = request();
  const results = await Promise.all(Array.from({ length: 30 }, () => controller.handle(r)));
  assert.equal(results.filter(r => r.code === "CreatedNew").length, 1);
  assert.equal(pages(state).length, 1);
});

test("same URL, changed title, redirects, another tab and window do not change identity", async () => {
  const { state, controller, api } = fakeBrowser(), r = request();
  const first = await controller.handle(r);
  await api.tabs.create({ url: r.url, active: true });
  state.tabs.get(first.tabId).url = "https://login.example.test/complete";
  state.tabs.get(first.tabId).title = "A totally different title";
  state.windows.set(9, { id: 9, state: "minimized", focused: false });
  for (const tab of state.tabs.values()) if (tab.groupId >= 0) tab.windowId = 9;
  const result = await controller.handle({ ...r, allowCreate: false });
  assert.equal(result.tabId, first.tabId); assert.equal(result.windowId, 9);
  assert.equal(state.tabs.get(first.tabId).url, "https://login.example.test/complete");
  assert.equal(state.tabs.size, 3);
});

test("browser restart with new numeric IDs recovers the restored group", async () => {
  const { state, api, controller } = fakeBrowser(), r = request();
  const original = await controller.handle(r);
  const replacements = [...state.tabs.values()].map(t => ({ ...t, id: t.id + 1000, groupId: 500 }));
  state.tabs = new Map(replacements.map(t => [t.id, t])); state.groups.set(500, { id: 500 });
  const restarted = new AppSwitcherManagedTabs(api, "session-2");
  const result = await restarted.handle({ ...r, allowCreate: false });
  assert.equal(result.code, "ActivatedExisting"); assert.equal(result.tabId, original.tabId + 1000);
  assert.equal(state.tabs.size, 2);
});

test("Firefox session tags recover the primary when the group marker is missing", async () => {
  const { state, api } = fakeBrowser(), r = request();
  const controller = new AppSwitcherManagedTabs(api, "session-1", true);
  const first = await controller.handle(r);
  const primary = state.tabs.get(first.tabId), tag = state.tags.get(first.tabId);
  state.tabs.clear(); state.tabs.set(200, { ...primary, id: 200, groupId: -1 }); state.tags.clear(); state.tags.set(200, tag);
  const result = await new AppSwitcherManagedTabs(api, "session-2", true).handle({ ...r, allowCreate: false });
  assert.equal(result.tabId, 200); assert.equal(state.tabs.size, 1);
});

test("startup without restored tabs does not create another copy", async () => {
  const { state, api, controller } = fakeBrowser(), r = request();
  await controller.handle(r); const restored = new Map(state.tabs); state.tabs.clear();
  const restarted = new AppSwitcherManagedTabs(api, "session-2");
  assert.equal((await restarted.handle({ ...r, allowCreate: false })).code, "Recovering");
  assert.equal(state.tabs.size, 0);
  state.tabs = restored;
  assert.equal((await restarted.handle({ ...r, allowCreate: false })).code, "ActivatedExisting");
});

test("lost extension storage does not authorize creation; an intact marker can recover", async () => {
  const { state, api, controller } = fakeBrowser(), r = request();
  await controller.handle(r); state.storage = {};
  const restarted = new AppSwitcherManagedTabs(api, "session-2");
  assert.equal((await restarted.handle({ ...r, allowCreate: false })).code, "ActivatedExisting");
  state.storage = {}; state.tabs.clear();
  assert.equal((await restarted.handle({ ...r, allowCreate: false })).code, "Recovering");
  assert.equal(state.tabs.size, 0);
});

test("duplicated marker or extra primary is a conflict, never an arbitrary choice", async () => {
  for (const duplicateMarker of [true, false]) {
    const { state, api, controller } = fakeBrowser(), r = request();
    await controller.handle(r);
    const existing = [...state.tabs.values()][duplicateMarker ? 0 : 1];
    const duplicate = await api.tabs.create({ url: existing.url });
    if (!duplicateMarker) state.tabs.get(duplicate.id).groupId = existing.groupId;
    assert.equal((await controller.handle(r)).code, "IdentityConflict");
    assert.equal(state.tabs.size, 3);
  }
});

test("moving only the primary cannot cause activation of a stranger", async () => {
  const { state, api, controller } = fakeBrowser(), r = request();
  const first = await controller.handle(r), group = state.tabs.get(first.tabId).groupId;
  state.tabs.get(first.tabId).groupId = -1;
  const stranger = await api.tabs.create({ url: r.url }); state.tabs.get(stranger.id).groupId = group;
  assert.equal((await controller.handle(r)).code, "IdentityConflict");
});

for (const [method, occurrence] of [["tabs.create", 1], ["tabs.create", 2], ["tabs.group", 1], ["tabGroups.update", 1], ["tabs.update", 1],
  ["storage.set", 1], ["storage.set", 2], ["storage.set", 3], ["storage.set", 4], ["storage.set", 5]]) {
  test(`crash after ${method} #${occurrence} never creates a duplicate on retry`, async () => {
    const { state, api, controller } = fakeBrowser(), r = request();
    state.fail = { name: method, count: occurrence };
    await controller.handle(r).catch(() => {});
    const restarted = new AppSwitcherManagedTabs(api, "session-1");
    for (let i = 0; i < 3; i++) await restarted.handle({ ...r, allowCreate: false });
    assert.ok(pages(state).length <= 1); assert.ok(state.tabs.size <= 2);
  });
}

test("close affects only the owned pair and permits a deliberate new generation", async () => {
  const { state, api, controller } = fakeBrowser(), r = request();
  await controller.handle(r); const other = await api.tabs.create({ url: r.url });
  assert.equal((await controller.handle({ ...r, action: "close" })).code, "Closed");
  assert.deepEqual([...state.tabs.keys()], [other.id]);
  assert.equal((await controller.handle({ ...r, generation: randomUUID() })).code, "CreatedNew");
  assert.equal(state.tabs.size, 3);
});

test("closing the primary explicitly permits replacement without orphaning the marker", async () => {
  const { state, api, controller } = fakeBrowser(), r = request();
  const first = await controller.handle(r);
  await api.tabs.remove(first.tabId); await controller.removed(first.tabId, { isWindowClosing: false });
  assert.equal((await controller.handle({ ...r, action: "inspect", allowCreate: false })).state, "Absent");
  assert.equal((await controller.handle({ ...r, generation: randomUUID() })).code, "CreatedNew");
  assert.equal(state.tabs.size, 2);
});

test("window closure is not mistaken for a confirmed tab stop", async () => {
  const { state, api, controller } = fakeBrowser(), r = request();
  const first = await controller.handle(r); state.tabs.clear();
  await controller.removed(first.tabId, { isWindowClosing: true });
  assert.equal((await controller.handle({ ...r, allowCreate: false })).code, "Recovering");
  assert.equal(state.tabs.size, 0);
});

test("foreground denial reports failure without another launch", async () => {
  const { state, controller } = fakeBrowser(), r = request();
  await controller.handle(r); state.windows.get(1).focused = false; state.denyFocus = true;
  assert.equal((await controller.handle(r)).code, "FocusDenied"); assert.equal(state.tabs.size, 2);
});

test("activation-only never creates a tab", async () => {
  const { state, controller } = fakeBrowser();
  assert.equal((await controller.handle({ ...request(), action: "activate", allowCreate: false })).code, "Recovering");
  assert.equal(state.tabs.size, 0);
});

test("invalid schemes and embedded credentials never reach tabs.create", async () => {
  for (const url of ["javascript:alert(1)", "file:///C:/test", "https://user:pass@example.com/", "not a URL"]) {
    const { state, controller } = fakeBrowser();
    assert.equal((await controller.handle({ ...request(), url })).code, "InvalidUrl"); assert.equal(state.tabs.size, 0);
  }
});

for (const firefox of [false, true]) test(`snapshot uses one inventory for 20 managed apps (firefox=${firefox}) and commands revalidate`, async () => {
  const { api, state } = fakeBrowser();
  const controller = new AppSwitcherManagedTabs(api, "session-1", firefox);
  const requests = Array.from({ length: 20 }, request);
  for (const r of requests) await controller.handle(r);
  for (let n = 0; n < 80; n++) await api.tabs.create({ url: "https://unrelated.example/" });
  state.counts["tabs.query"] = 0; state.counts["sessions.get"] = 0;
  const snapshot = await controller.snapshot();
  assert.equal(snapshot.length, 20);
  assert.ok(snapshot.every(s => s.state === "Present"));
  assert.equal(state.counts["tabs.query"], 1);
  assert.equal(state.counts["sessions.get"], firefox ? 120 : 0);
  // A later mutation must not reuse this observation inventory.
  const primary = pages(state)[0];
  const intruder = await api.tabs.create({ url: "https://stranger.example/" });
  state.tabs.get(intruder.id).groupId = primary.groupId;
  const result = await controller.handle({ ...requests[0], action: "activate", allowCreate: false });
  assert.equal(result.state, "Conflict");
  assert.equal(state.counts["tabs.query"], 2);
});
