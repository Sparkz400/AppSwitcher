// Reuse the CLI check templates without navigating any restored tab on restart.
// Usage: node scripts/check-chromium-session.cjs <bundled-chromium.exe> [playwright-package-directory]
const fs = require('node:fs');
const path = require('node:path');
const { randomUUID } = require('node:crypto');
const { chromium } = require(process.argv[3] || 'playwright');
if (!process.argv[2]) throw new Error('A bundled Chromium executable is required.');
const root = path.resolve(__dirname, '..');
const output = path.join(root, 'output', 'playwright');
const meta = JSON.parse(fs.readFileSync(path.join(output, 'broker.json'), 'utf8'));
const profile = path.join(output, 'session-check-' + randomUUID());
const extension = path.join(output, 'chromium-extension');
const options = { headless: false, executablePath: path.resolve(process.argv[2]),
  args: ['--restore-last-session', `--disable-extensions-except=${extension}`, `--load-extension=${extension}`] };
const readCheck = name => eval(fs.readFileSync(path.join(__dirname, name), 'utf8')
  .replaceAll('__BROKER_URL__', meta.url).replaceAll('__BROKER_TOKEN__', meta.token).replaceAll('__BROWSER__', 'Chrome'));
(async () => {
  let context, passed = false;
  try {
    context = await chromium.launchPersistentContext(profile, options);
    const browserVersion = context.browser().version();
    const before = await readCheck('check-chromium-browser.js')(context.pages()[0]);
    await context.close();
    context = await chromium.launchPersistentContext(profile, options);
    const after = await readCheck('check-chromium-restart.js')(context.pages()[0]);
    const result = { browserVersion, before, after, scope: 'Visible bundled Chromium, isolated unpacked extension and synthetic native-host broker; not store-distribution acceptance.' };
    fs.writeFileSync(path.join(output, 'chromium-session-result.json'), JSON.stringify(result, null, 2));
    console.log(JSON.stringify(result));
    passed = true;
  } finally {
    await context?.close();
    if (passed) {
      if (path.dirname(path.resolve(profile)) !== output) throw new Error('Unsafe profile cleanup path.');
      fs.rmSync(profile, { recursive: true, force: true });
    }
  }
})().catch(error => { console.error(error); process.exitCode = 1; });
