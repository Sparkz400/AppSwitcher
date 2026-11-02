import { readFile, writeFile, mkdir, copyFile } from "node:fs/promises";
import { resolve, dirname } from "node:path";
import { fileURLToPath } from "node:url";
import { createHash } from "node:crypto";

const root = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const output = process.argv[2] ? resolve(process.argv[2]) : resolve(root, "artifacts/browser-package");
const files = ["core.js", "background.js", "identity.html", "profile.html", "profile.js", "pages.css"];
for (const browser of ["chromium", "firefox"]) {
  const dest = resolve(output, browser);
  await mkdir(dest, { recursive: true });
  const manifest = JSON.parse(await readFile(resolve(root, `browser-extension/manifest.${browser}.json`), "utf8"));
  await writeFile(resolve(dest, "manifest.json"), JSON.stringify(manifest, null, 2));
  for (const file of files) await copyFile(resolve(root, "browser-extension", file), resolve(dest, file));
  if (browser === "chromium") {
    const hex = createHash("sha256").update(Buffer.from(manifest.key, "base64")).digest("hex").slice(0, 32);
    const id = [...hex].map(c => String.fromCharCode(97 + parseInt(c, 16))).join("");
    await writeFile(resolve(output, "extension-ids.json"), JSON.stringify({ chrome: id, edge: id, firefox: "browser@appswitcher.local" }, null, 2));
    console.log(`Unpacked Chromium extension ID: ${id}`);
  }
}
for (const file of ["Install-BrowserHost.ps1", "Uninstall-BrowserHost.ps1"]) await copyFile(resolve(root, "scripts", file), resolve(output, file));
console.log(`Extensions: ${output}`);
