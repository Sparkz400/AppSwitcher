// Isolated test driver for the real native host + extension. Never reads AppSwitcher configuration.
import net from "node:net";
import http from "node:http";
import { randomUUID } from "node:crypto";
import { readFile, writeFile } from "node:fs/promises";
const [pipeName, metadataPath, identityPath] = process.argv.slice(2);
if (!pipeName?.startsWith("AppSwitcher.Browser.") || !metadataPath || !identityPath) throw new Error("Expected pipe name, metadata path and isolated-profile identity file");
const expectedProfileName = (await readFile(identityPath, "utf8")).trim();
if (!/^AppSwitcher checks [a-f0-9]{32}$/.test(expectedProfileName)) throw new Error("Invalid isolated-profile identity");
const token = randomUUID(), clients = new Map(), pending = new Map();
const pipe = net.createServer(socket => {
  let buffer = Buffer.alloc(0), identity;
  socket.on("data", chunk => {
    buffer = Buffer.concat([buffer, chunk]);
    while (buffer.length >= 4) {
      const length = buffer.readInt32LE();
      if (length < 1 || length > 1048576) { socket.destroy(); return; }
      if (buffer.length < length + 4) return;
      const message = JSON.parse(buffer.subarray(4, length + 4)); buffer = buffer.subarray(length + 4);
      if (message.type === "hello") {
        if (message.profileName !== expectedProfileName) { socket.destroy(); return; }
        identity = message; clients.set(message.browser, { socket, identity });
      }
      if (message.type === "reply") pending.get(message.requestId)?.(message.reply);
    }
  });
  socket.on("error", () => {});
  socket.on("close", () => { if (clients.get(identity?.browser)?.socket === socket) clients.delete(identity.browser); });
});
pipe.listen(`\\\\.\\pipe\\${pipeName}`);
const server = http.createServer(async (req, res) => {
  if (req.url?.startsWith("/fixture")) {
    res.setHeader("Content-Type", "text/html");
    res.end('<!doctype html><title>AppSwitcher browser fixture</title><h1>Browser integration test</h1><input id="draft" placeholder="Unsaved text"><script>window.fixtureNonce=crypto.randomUUID()</script>'); return;
  }
  if (req.headers.authorization !== token) { res.writeHead(403).end(); return; }
  try {
    if (req.url === "/status") { res.end(JSON.stringify([...clients.values()].map(c => c.identity))); return; }
    if (req.url === "/disconnect") { for (const c of clients.values()) c.socket.destroy(); res.end('{}'); return; }
    if (req.url !== "/command") { res.writeHead(404).end(); return; }
    const chunks = []; for await (const chunk of req) chunks.push(chunk);
    const { browser, request } = JSON.parse(Buffer.concat(chunks));
    const client = clients.get(browser); if (!client) throw new Error("Browser not connected");
    const id = randomUUID();
    const reply = await new Promise((resolve, reject) => {
      const timeout = setTimeout(() => { pending.delete(id); reject(new Error("Native reply timeout")); }, 20000);
      pending.set(id, value => { pending.delete(id); clearTimeout(timeout); resolve(value); });
      const body = Buffer.from(JSON.stringify({ protocol: 1, type: "request", requestId: id, request }));
      const header = Buffer.alloc(4); header.writeInt32LE(body.length); client.socket.write(Buffer.concat([header, body]));
    });
    res.end(JSON.stringify(reply));
  } catch (error) { res.writeHead(500).end(JSON.stringify({ error: error.message })); }
});
await new Promise(resolve => server.listen(0, "127.0.0.1", resolve));
await writeFile(metadataPath, JSON.stringify({ url: `http://127.0.0.1:${server.address().port}`, token }));
console.log(`Browser check broker listening on ${server.address().port}`);
