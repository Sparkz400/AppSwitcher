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
  assert(worker, "Extension not restored");
  const request = await worker.evaluate(async () => (await chrome.storage.local.get("test.request"))["test.request"]);
  assert(request, "Persistent request lost");
  const before = await worker.evaluate(() => chrome.tabs.query({}));
  const reply = await control("/command", { browser, request: { ...request, allowCreate: false } });
  assert(reply.code === "ActivatedExisting", `Restart: ${JSON.stringify(reply)}`);
  const after = await worker.evaluate(() => chrome.tabs.query({}));
  assert(before.length === after.length, "Restart created a tab");
  assert(after.find(t => t.id === reply.tabId)?.url.startsWith(request.url), "Wrong restored tab");
  const close = await control("/command", { browser, request: { ...request, action: "close", allowCreate: false } });
  assert(close.code === "Closed", `Close: ${JSON.stringify(close)}`);
  const remaining = await worker.evaluate(() => chrome.tabs.query({}));
  assert(remaining.length === after.length - 2, "Close affected wrong tabs");
  assert(remaining.some(t => t.url.startsWith(request.url)), "Unrelated same-URL tab was closed");
  return { browser, restartRecovered: reply, close: close.code, unrelatedTabPreserved: true };
}
