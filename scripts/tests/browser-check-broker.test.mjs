import test from "node:test";
import assert from "node:assert/strict";
import { spawn } from "node:child_process";
import net from "node:net";
import { mkdtemp, readFile, writeFile, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { randomUUID } from "node:crypto";

test("browser test broker rejects unrelated profiles without replacing the isolated profile", { skip: process.platform !== "win32", timeout: 15000 }, async () => {
  const root = await mkdtemp(join(tmpdir(), "AppSwitcher-broker-"));
  const id = randomUUID().replaceAll("-", "");
  const pipe = `AppSwitcher.Browser.checks.${id}`;
  const expected = `AppSwitcher checks ${id}`;
  const metadata = join(root, "broker.json"), identity = join(root, "identity.txt");
  await writeFile(identity, expected);
  const process = spawn(globalThis.process.execPath, [resolve(dirname(fileURLToPath(import.meta.url)), "../browser-check-broker.mjs"), pipe, metadata, identity], { windowsHide: true, stdio: "ignore" });
  const clients = [];
  const until = async (predicate) => {
    const deadline = Date.now() + 5000;
    while (Date.now() < deadline) {
      if (await predicate()) return;
      await new Promise(r => setTimeout(r, 20));
    }
    assert.fail("Broker state did not arrive before the deadline");
  };
  try {
    let meta;
    await until(async () => { try { meta = JSON.parse(await readFile(metadata, "utf8")); return true; } catch { return false; } });
    const status = async () => (await fetch(meta.url + "/status", { headers: { authorization: meta.token } })).json();
    const hello = async (profileName) => {
      const client = net.connect(`\\\\.\\pipe\\${pipe}`); clients.push(client);
      await new Promise((accept, reject) => { client.once("connect", accept); client.once("error", reject); });
      const payload = Buffer.from(JSON.stringify({ type: "hello", browser: "Chrome", profileName }));
      const header = Buffer.alloc(4); header.writeInt32LE(payload.length);
      client.write(Buffer.concat([header, payload]));
      return client;
    };
    const unrelated = await hello("Personal browser");
    await until(async () => unrelated.destroyed);
    assert.deepEqual(await status(), []);
    await hello(expected);
    await until(async () => (await status()).length === 1);
    const later = await hello("Another normal profile");
    await until(async () => later.destroyed);
    assert.equal((await status())[0].profileName, expected);
  } finally {
    for (const client of clients) client.destroy();
    if (process.exitCode === null) {
      const exited = new Promise(resolve => process.once("exit", resolve));
      process.kill(); await exited;
    }
    assert.equal(dirname(root).toLowerCase(), resolve(tmpdir()).toLowerCase());
    await rm(root, { recursive: true, force: true });
  }
});
