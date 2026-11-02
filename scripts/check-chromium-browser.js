// Template executed by playwright-cli run-code, after replacing the three __...__ placeholders.
async (page) => {
  const url = "__BROKER_URL__", token = "__BROKER_TOKEN__", browser = "__BROWSER__";
  const assert = (condition, message) => { if (!condition) throw new Error(message); };
  const control = async (path, data) => {
    const response = await page.request.post(url + path, { headers: { authorization: token }, data });
    assert(response.ok(), await response.text()); return response.json();
  };
  for (let i = 0; i < 30; i++) {
    if ((await control("/status")).some(c => c.browser === browser)) break;
    await new Promise(r => setTimeout(r, 500));
  }
  const worker = page.context().serviceWorkers().find(w => w.url().includes("afmmehaldghpcccgholfklbeemokekoj"));
  assert(worker, "Extension worker missing");
  const request = await worker.evaluate(async fixture => {
    const appId = crypto.randomUUID();
    const request = { action: "ensure", appId, generation: crypto.randomUUID(), url: `${fixture}?run=${appId}`,
      name: "Browser integration check", allowCreate: true, focus: true };
    await chrome.storage.local.set({ "test.request": request }); return request;
  }, url + "/fixture");
  const first = await control("/command", { browser, request });
  assert(first.code === "CreatedNew", `Creation: ${JSON.stringify(first)}`);
  await page.context().waitForEvent("page", { timeout: 1000 }).catch(() => null);
  const website = page.context().pages().find(p => p.url() === request.url);
  assert(website, "Website not created");
  await website.waitForLoadState();
  const nonce = await website.evaluate(() => { window.unsavedAppSwitcherDraft = "do not reload"; return window.fixtureNonce; });
  await worker.evaluate(async ({ id, fixture }) => {
    await chrome.windows.create({ url: fixture });
    await chrome.tabs.update(id, { active: true });
  }, { id: first.tabId, fixture: request.url });
  for (let i = 0; i < 20; i++) {
    await worker.evaluate(id => chrome.windows.update(id, { state: "minimized" }), first.windowId);
    const reply = await control("/command", { browser, request: { ...request, allowCreate: false } });
    assert(reply.code === "ActivatedExisting" && reply.tabId === first.tabId, `Activation ${i}: ${JSON.stringify(reply)}`);
  }
  const after = await website.evaluate(() => ({ nonce: window.fixtureNonce, draft: window.unsavedAppSwitcherDraft }));
  assert(after.nonce === nonce && after.draft === "do not reload", "Activation reloaded the original page");
  const tabs = await worker.evaluate(() => chrome.tabs.query({}));
  assert(tabs.filter(t => t.url.startsWith(request.url)).length === 2, "Duplicate website created");
  assert(tabs.filter(t => t.url.includes(`app=${request.appId}`)).length === 1, "Duplicate identity marker");
  await control("/disconnect");
  for (let i = 0; i < 30; i++) {
    if ((await control("/status")).some(c => c.browser === browser)) break;
    await new Promise(r => setTimeout(r, 500));
  }
  const reconnect = await control("/command", { browser, request: { ...request, allowCreate: false } });
  assert(reconnect.code === "ActivatedExisting" && reconnect.tabId === first.tabId, "Native reconnect lost ownership");
  return { browser, first, minimizedActivations: 20, originalDocumentPreserved: true, unrelatedSameUrlPreserved: true, nativeReconnect: reconnect.code, tabs: tabs.length };
}
