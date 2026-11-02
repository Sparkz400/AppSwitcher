/* Shared by Chromium's service worker, Firefox's event page, and the fault-injection tests. */
(() => {
  "use strict";
  const key = id => `appswitcher.tab.${id}`;
  const uuid = value => typeof value === "string" && /^[a-f0-9-]{32,36}$/i.test(value);
  class ManagedTabs {
    constructor(api, sessionId, firefox = false) {
      this.api = api;
      this.sessionId = sessionId;
      this.firefox = firefox;
      this.queue = Promise.resolve();
    }
    exclusive(fn) {
      const task = this.queue.then(fn);
      this.queue = task.catch(() => {});
      return task;
    }
    async read(id) { return (await this.api.storage.local.get(key(id)))[key(id)]; }
    async save(record) { await this.api.storage.local.set({ [key(record.appId)]: record }); }
    markerUrl(r, role) {
      return `${this.api.runtime.getURL("identity.html")}?app=${encodeURIComponent(r.appId)}&generation=${encodeURIComponent(r.generation)}&role=${role}`;
    }
    marker(tab) {
      try {
        // Newly-created extension tabs may expose only pendingUrl until their first navigation commits.
        const u = new URL((!tab.url || tab.url === "about:blank") ? (tab.pendingUrl || tab.url || "") : tab.url);
        const own = new URL(this.api.runtime.getURL("identity.html"));
        if (u.protocol !== own.protocol || u.host !== own.host || u.pathname !== own.pathname) return null;
        const appId = u.searchParams.get("app"), generation = u.searchParams.get("generation"), role = u.searchParams.get("role");
        return uuid(appId) && uuid(generation) && ["anchor", "primary"].includes(role) ? { appId, generation, role } : null;
      } catch { return null; }
    }
    async captureInventory() {
      const tabs = await this.api.tabs.query({}), identities = new Map();
      if (this.firefox && this.api.sessions?.getTabValue) {
        // Read each tab value once per pass, with bounded concurrency.
        let cursor = 0;
        await Promise.all(Array.from({ length: Math.min(8, tabs.length) }, async () => {
          while (cursor < tabs.length) {
            const tab = tabs[cursor++];
            identities.set(tab.id, await this.api.sessions.getTabValue(tab.id, "appswitcher.identity").catch(() => null));
          }
        }));
      }
      return { tabs, identities };
    }
    async inventory(r, captured) {
      const { tabs, identities } = captured || await this.captureInventory();
      const markers = tabs.filter(t => this.marker(t)?.appId === r.appId);
      if (markers.some(t => this.marker(t).generation !== r.generation)) return { state: "Conflict", tabs };
      const anchors = markers.filter(t => this.marker(t).role === "anchor");
      const placeholders = markers.filter(t => this.marker(t).role === "primary");
      if (anchors.length > 1 || placeholders.length > 1) return { state: "Conflict", tabs };
      const anchor = anchors[0];
      let tagged = [];
      if (this.firefox && this.api.sessions?.getTabValue) {
        for (const tab of tabs) {
          const identity = identities.get(tab.id);
          if (identity?.appId === r.appId) {
            if (identity.generation !== r.generation) return { state: "Conflict", tabs };
            tagged.push(tab);
          }
        }
        if (tagged.length > 1) return { state: "Conflict", tabs };
      }
      if (anchor && anchor.groupId >= 0) {
        const members = tabs.filter(t => t.groupId === anchor.groupId && t.windowId === anchor.windowId && t.id !== anchor.id);
        if (members.length > 1 || members.some(t => this.marker(t)?.role === "anchor")) return { state: "Conflict", tabs };
        const primary = members[0];
        if (primary && tagged.length && tagged[0].id !== primary.id) return { state: "Conflict", tabs };
        if (primary) return { state: "Present", tabs, anchor, primary };
      }
      // Firefox's tab value is a stronger identity than group membership and survives session restore.
      if (tagged.length === 1 && !anchor) return { state: "Present", tabs, primary: tagged[0] };
      return { state: anchor || placeholders.length ? "Incomplete" : "Missing", tabs, anchor, placeholder: placeholders[0] };
    }
    async resolve(r, record, captured) {
      const found = await this.inventory(r, captured);
      if (found.state === "Conflict") return { ...found, code: "IdentityConflict" };
      if (found.state === "Present") {
        if (record?.phase === "closed") return { ...found, state: "Conflict", code: "IdentityConflict" };
        // A primary moved to a different group must not make a stranger in the old group the primary.
        if (record?.sessionId === this.sessionId && record.primaryId != null && record.primaryId !== found.primary.id)
          return { ...found, state: "Conflict", code: "IdentityConflict" };
        if (this.marker(found.primary)?.role === "primary")
          return { ...found, state: "Incomplete", placeholder: found.primary, code: "Recovering" };
        return { ...found, code: "Present" };
      }
      if (found.state === "Missing" && record?.phase === "closed") return { ...found, state: "Absent", code: "Absent" };
      if (record?.phase === "primaryClosed" && found.state === "Incomplete" && found.anchor && !found.placeholder)
        return { ...found, state: "Absent", code: "Absent" };
      if (found.state === "Missing" && !record && r.allowCreate) return { ...found, state: "Absent", code: "Absent" };
      return { ...found, state: "Unknown", code: "Recovering" };
    }
    async bind(r, found, record) {
      const next = { ...record, appId: r.appId, generation: r.generation, phase: "open", stage: "done",
        sessionId: this.sessionId, primaryId: found.primary.id, anchorId: found.anchor?.id, windowId: found.primary.windowId };
      if (JSON.stringify(next) !== JSON.stringify(record)) {
        if (this.firefox) await this.api.sessions.setTabValue(found.primary.id, "appswitcher.identity", { appId: r.appId, generation: r.generation });
        await this.save(next);
      }
      return next;
    }
    reply(code, state, primary) { return { code, state, tabId: primary?.id, windowId: primary?.windowId }; }
    async activate(primary, focus) {
      if (!focus) return true;
      const current = await this.api.tabs.get(primary.id);
      const window = await this.api.windows.get(current.windowId);
      if (window.state === "minimized") await this.api.windows.update(window.id, { state: "normal" });
      if (current.groupId >= 0) await this.api.tabGroups.update(current.groupId, { collapsed: false });
      await this.api.tabs.update(current.id, { active: true });
      await this.api.windows.update(current.windowId, { focused: true });
      for (let attempt = 0; attempt < 10; attempt++) {
        const tab = await this.api.tabs.get(current.id);
        const win = await this.api.windows.get(tab.windowId);
        if (tab.active && win.focused && win.state !== "minimized") return true;
        await new Promise(resolve => setTimeout(resolve, 50));
      }
      return false;
    }
    handle(r) { return this.exclusive(() => this.execute(r)); }
    async execute(r) {
      if (!uuid(r?.appId) || !uuid(r?.generation) || !["inspect", "ensure", "activate", "close"].includes(r.action))
        return this.reply("InvalidRequest", "Unknown");
      if (r.action === "ensure") {
        try { const u = new URL(r.url); if (!["http:", "https:"].includes(u.protocol) || u.username || u.password) throw new Error(); }
        catch { return this.reply("InvalidUrl", "Unknown"); }
      }
      let record = await this.read(r.appId);
      if (record?.phase === "primaryClosed" && record.generation !== r.generation && r.action === "inspect" && r.allowCreate) {
        const previous = await this.inventory(record);
        return previous.state === "Incomplete" && previous.anchor && !previous.placeholder
          ? this.reply("Absent", "Absent") : this.reply("IdentityConflict", "Conflict");
      }
      if (record?.phase === "primaryClosed" && r.action === "ensure" && r.allowCreate) {
        const previous = await this.inventory(record);
        if (previous.state !== "Incomplete" || !previous.anchor || previous.placeholder)
          return this.reply("IdentityConflict", "Conflict");
        await this.api.tabs.remove(previous.anchor.id);
        await this.save({ ...record, phase: "closed" });
        record = await this.read(r.appId);
      }
      if (record && record.generation !== r.generation) {
        if (record.phase !== "closed" || !r.allowCreate) return this.reply("IdentityConflict", "Conflict");
        const old = await this.inventory(record);
        if (old.state !== "Missing") return this.reply("IdentityConflict", "Conflict");
        record = undefined;
      }
      let found = await this.resolve(r, record);
      if (r.action === "inspect") return this.reply(found.code, found.state, found.primary);
      if (r.action === "close") return this.close(r, record, found);
      if (record?.phase === "closing") return this.reply("Recovering", "Unknown");
      if (found.state === "Present") {
        await this.bind(r, found, record);
        return this.reply(await this.activate(found.primary, r.focus) ? "ActivatedExisting" : "FocusDenied", "Present", found.primary);
      }
      if (r.action === "activate" || found.state === "Conflict") return this.reply(found.code, found.state);
      if (found.state === "Absent" && r.allowCreate) {
        record = { appId: r.appId, generation: r.generation, phase: "creating", stage: "anchor", sessionId: this.sessionId };
        await this.save(record); // Intent BEFORE the side effect. A lost response is never permission to repeat it.
        const anchor = await this.api.tabs.create({ url: this.markerUrl(r, "anchor"), active: false });
        record.anchorId = anchor.id;
        record.stage = "primary";
        await this.save(record);
        const primary = await this.api.tabs.create({ url: this.markerUrl(r, "primary"), windowId: anchor.windowId, active: false });
        record.primaryId = primary.id;
        record.stage = "group";
        await this.save(record);
      } else if (record?.phase !== "creating") return this.reply("Recovering", "Unknown");

      // Resume only identifiable effects. Missing markers after a crash are not proof of absence.
      let inventory = await this.inventory(r);
      // Firefox can initially expose about:blank with no pendingUrl for a newly-created tab.
      // Wait for the identity navigation to become observable; never repeat a creation here.
      for (let attempt = 0; attempt < 40 && inventory.state !== "Conflict" &&
          (!inventory.anchor || !(inventory.primary || inventory.placeholder)); attempt++) {
        if (inventory.anchor && record.stage === "anchor") break;
        await new Promise(resolve => setTimeout(resolve, 50));
        inventory = await this.inventory(r);
      }
      if (inventory.state === "Conflict") return this.reply("IdentityConflict", "Conflict");
      let anchor = inventory.anchor;
      let primary = inventory.primary || inventory.placeholder;
      if (anchor && !primary && record.stage === "anchor") {
        // An anchor exists and the primary-create intent has never been written: safe to advance once.
        record.stage = "primary";
        record.anchorId = anchor.id;
        await this.save(record);
        primary = await this.api.tabs.create({ url: this.markerUrl(r, "primary"), windowId: anchor.windowId, active: false });
        record.primaryId = primary.id;
        record.stage = "group";
        await this.save(record);
      }
      if (!anchor || !primary) return this.reply("Recovering", "Unknown");
      if (!this.marker(primary)) {
        await this.bind(r, { anchor, primary }, record);
        return this.reply(await this.activate(primary, r.focus) ? "ActivatedExisting" : "FocusDenied", "Present", primary);
      }
      if (primary.pendingUrl && primary.pendingUrl !== primary.url && !this.marker({ url: primary.pendingUrl }))
        return this.reply("Recovering", "Unknown");
      if (primary.windowId !== anchor.windowId) return this.reply("IdentityConflict", "Conflict");
      const groupId = anchor.groupId >= 0 && anchor.groupId === primary.groupId ? anchor.groupId
        : await this.api.tabs.group({ tabIds: [anchor.id, primary.id] });
      await this.api.tabGroups.update(groupId, { title: `AppSwitcher · ${(r.name || "Website").slice(0, 80)}`, collapsed: false });
      if (this.firefox) await this.api.sessions.setTabValue(primary.id, "appswitcher.identity", { appId: r.appId, generation: r.generation });
      record.stage = "navigate";
      record.primaryId = primary.id;
      record.anchorId = anchor.id;
      record.sessionId = this.sessionId;
      await this.save(record);
      primary = await this.api.tabs.update(primary.id, { url: r.url });
      await this.bind(r, { anchor, primary }, record);
      return this.reply(await this.activate(primary, r.focus) ? "CreatedNew" : "CreatedFocusDenied", "Present", primary);
    }
    async close(r, record, found) {
      if (found.state === "Absent") {
        if (found.anchor) await this.api.tabs.remove(found.anchor.id);
        if (record) await this.save({ ...record, phase: "closed" });
        return this.reply("Closed", "Absent");
      }
      if (found.state !== "Present") {
        if (record?.phase === "closing" && record.sessionId === this.sessionId && found.anchor &&
            found.anchor.id === record.anchorId && !found.tabs.some(t => t.id === record.primaryId)) {
          await this.api.tabs.remove(found.anchor.id);
          await this.save({ ...record, phase: "closed" });
          return this.reply("Closed", "Absent");
        }
        if (record?.phase === "closing" && record.sessionId === this.sessionId && found.tabs &&
            !found.tabs.some(t => t.id === record.primaryId || t.id === record.anchorId)) {
          await this.save({ ...record, phase: "closed" });
          return this.reply("Closed", "Absent");
        }
        return this.reply(found.code, found.state);
      }
      record = { ...record, appId: r.appId, generation: r.generation, phase: "closing", sessionId: this.sessionId,
        primaryId: found.primary.id, anchorId: found.anchor?.id };
      await this.save(record);
      // Closing the page first preserves the anchor if a beforeunload prompt blocks closure.
      await this.api.tabs.remove(found.primary.id);
      if (await this.api.tabs.get(found.primary.id).catch(() => null)) return this.reply("Recovering", "Unknown");
      if (found.anchor) await this.api.tabs.remove(found.anchor.id);
      await this.save({ ...record, phase: "closed" });
      return this.reply("Closed", "Absent");
    }
    removed(tabId, info) {
      return this.exclusive(async () => {
        if (info.isWindowClosing) return; // Browser shutdown and window closure are not explicit website stops.
        const all = await this.api.storage.local.get(null);
        for (const [k, record] of Object.entries(all)) {
          if (!k.startsWith("appswitcher.tab.") || record.sessionId !== this.sessionId || record.primaryId !== tabId || record.phase !== "open") continue;
          const anchor = await this.api.tabs.get(record.anchorId).catch(() => null);
          if (!anchor || this.marker(anchor)?.generation !== record.generation) continue;
          // Keep the marker: a future click may safely create a new generation only after an explicit close cleanup.
          await this.save({ ...record, phase: "primaryClosed" });
        }
      });
    }
    replaced(added, removed) {
      return this.exclusive(async () => {
        const all = await this.api.storage.local.get(null);
        for (const [k, record] of Object.entries(all)) {
          if (k.startsWith("appswitcher.tab.") && record.sessionId === this.sessionId && record.primaryId === removed)
            await this.save({ ...record, primaryId: added });
        }
      });
    }
    snapshot() {
      return this.exclusive(async () => {
        const all = await this.api.storage.local.get(null), states = [];
        const records = Object.entries(all).filter(([k]) => k.startsWith("appswitcher.tab."));
        if (!records.length) return states;
        const captured = await this.captureInventory();
        for (const [k, record] of Object.entries(all)) {
          if (!k.startsWith("appswitcher.tab.")) continue;
          const found = await this.resolve({ ...record, allowCreate: false }, record, captured);
          if (found.state === "Present" && record.phase === "open") await this.bind(record, found, record);
          states.push({ appId: record.appId, generation: record.generation, state: record.phase === "closing" ? "Unknown" : found.state });
        }
        return states;
      });
    }
  }
  globalThis.AppSwitcherManagedTabs = ManagedTabs;
})();
