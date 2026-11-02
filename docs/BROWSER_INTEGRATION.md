# Managed website tabs

Website entries use the AppSwitcher extension in Chrome, Edge or Firefox. A click selects the owned tab, restores its minimized window and requests focus without navigating or reloading it. Titles, domains and IP addresses are never used to guess ownership. HTTP/HTTPS URLs, IPv4, IPv6, ports, queries, fragments and redirects use the same mechanism.

## Supported contract

Each website has a persistent application ID and a paired browser profile. Chrome/Edge keep one visible `AppSwitcher` identity tab beside one website tab in a named group. Keep this pair together; move the entire group when moving windows. Do not duplicate, ungroup, replace or navigate the identity tab, or put extra tabs in its group. Firefox also attaches a persistent session value to the website tab.

Enable the browser's **restore previous session / continue where you left off** setting. Normal AppSwitcher restarts reconnect to the same profile. Browser and Windows restarts recover the restored group, even when numeric tab/window/group IDs change. The extension must remain installed and enabled with its storage intact. Private browsing and copied browser profiles are unsupported.

The guarantee is that an uncertain result never falls back to opening the URL again. Missing connections, incomplete session restore, duplicate markers, a damaged group or a lost response show **Recovering** or an identity error. They do not authorize another creation. A tab that the browser permanently deleted cannot be recovered, and Windows can deny foreground activation. Neither limitation is reported as successful activation. Clearing both the desktop ownership journal and browser data also destroys the identity; no software can reconstruct the original tab from a URL alone.

Closing a website through AppSwitcher closes only its owned page and identity tab. Force-stop also uses this operation; it never kills a shared browser process. An explicit page close observed by the extension permits a new generation on the next launch. Closing an entire window or quitting the browser preserves the ownership claim for session restoration. Bring-to-front never creates a tab.

## Build and install

### Guided setup

In Settings → Browsers, choose **Set up** beside Chrome, Edge or Firefox. Website entries also have a **Set up** button beside their profile controls.

AppSwitcher installs or repairs the Windows native-messaging bridge for that browser and the current Windows user. No PowerShell command or administrator prompt is needed. For a local installation, it copies the permanent installation path to the clipboard before opening the browser's extension page. Three numbered instructions explain the remaining browser actions. The full path is available on demand if the clipboard is busy. Connected profiles are detected while the setup window remains open. For a new website, a single connected profile is selected automatically; several profiles require a choice. An existing website keeps its exact saved profile ID, even if another profile is online. Choose **Use connection**, then save your website changes.

Browser approval is still required:

- **Chrome / Edge, offline package:** enable Developer mode, select **Load unpacked**, then press Ctrl+L, Ctrl+V, Enter in the folder picker and choose **Select Folder**. Setup copies the path automatically. AppSwitcher keeps that extension folder under `%LOCALAPPDATA%\Programs\AppSwitcherBrowserSetup`, outside the downloaded application package. A previously connected extension skips installation and leaves the clipboard alone.
- **Explicitly online releases only:** if the distributor sets `allowOnlineInstall: true`, Setup may open an official store listing. Approve the extension in the browser; AppSwitcher detects its connection. A store link alone does not enable online installation.
- **Firefox:** bundle a Mozilla-signed XPI for permanent offline installation. The target PC needs neither internet access nor a Mozilla account. Signing happens on the release preparation machine, for each extension version. The approved, signed 1.1.0 XPI is now included in `browser-extension/signed` and bundled automatically. Setup now shows **Firefox setup needs an additional file**, hides the unusable install/finish actions, and explains that the offline package is incomplete. It does not present this as a user mistake or keep saying to wait for installation. A previously installed extension can still connect. A release containing the XPI guides the user to Firefox's **Install Add-on From File** command, with its path already copied; Firefox verifies the signature.

After the extension connects, Setup shows **Reopen the same tabs when the browser starts**. This means the browser reopens the tabs it had when it closed, allowing AppSwitcher to find the original website tab after a browser restart. The screen names the browser's exact setting and opens its settings page. Browser launch requests may open the last active profile; switch to the profile shown in Setup before changing this setting. A connected status verifies the extension/bridge connection, not session-restore settings or restart recovery. Setup does not alter browser preferences, enterprise policy, existing tabs, or ownership journals. Closing Setup cancels pending work; an already completed bridge installation remains available for the next attempt. Connection detection runs only while the dialog is open, for at most ten minutes; **Check connection** remains available afterward.

### Standalone, offline Windows PCs

The target environment is a standalone/workgroup Windows PC. A store download is not a deployment solution for this environment. The default package is offline and can be copied from USB; no setup server or network connection is used by the Windows bridge or unpacked-extension installation. Updates are delivered as a new offline package. The browser itself may independently attempt its normal network activity.

Chrome and Edge restrict automatic installation of private, non-store extensions on Windows to eligible managed deployments. Edge specifically requires an Active Directory domain join for self-hosted extensions. Adding a local registry policy does not remove that prerequisite on a workgroup PC. The remaining Developer mode / Load unpacked actions therefore remain a one-time user task per browser profile. AppSwitcher does not modify protected browser preference files, disable signature checks, or add enterprise policy to simulate a managed deployment. See [Chrome's Windows deployment rules](https://support.google.com/chrome/a/answer/7532015) and [Edge's self-hosting requirements](https://learn.microsoft.com/en-us/deployedge/microsoft-edge-manage-extensions-webstore).

Firefox supports [self-distribution and installation from a local signed XPI](https://extensionworkshop.com/documentation/publish/self-distribution/). A publisher account is needed for release signing, not on each offline PC, and a public AMO listing is not required. The signed 1.1.0 release artifact is included, so the default package supports permanent Firefox installation offline.

### Building a distributable package

Requirements: Windows, .NET 10 SDK, Node.js 20+ and PowerShell 7 for the scripts. Supported extension API floors: Chromium 120+, Firefox 142+. Validate actual browser releases before deployment.

```powershell
pwsh -NoProfile -File scripts/build-browser-package.ps1
```

This writes `artifacts/browser-package` with self-contained Windows x64 app and native-host builds, Chromium/Firefox extension sources, an unsigned Firefox ZIP, and installer scripts. The `app/browser-setup` directory contains everything needed by guided setup; distribute it alongside `AppSwitcher.exe`. A plain `dotnet publish` without this packaging step does not include setup payloads and the app reports that they are missing.

Optional release inputs:

```powershell
pwsh -NoProfile -File scripts/build-browser-package.ps1 -OutputPath artifacts/release-browser -RequireFirefox
```

The builder automatically selects `browser-extension/signed/appswitcher-firefox-<version>.xpi` for the source manifest version. `-FirefoxSignedXpi` can provide another release artifact. `-RequireFirefox` fails if neither is available. Before publishing, the builder compares every non-signature file against the generated extension; a changed payload requires a new signed release. It does not modify the signed archive. Builds without an XPI explicitly warn that Firefox installation is unavailable. Use `browser-extension/distribution.json` as the optional `-DistributionFile` schema. Offline installation is the default even when `allowOnlineInstall` is omitted; keep it false for standalone offline deployments. Chromium unpacked IDs must match the bundled public key; Firefox's ID must match its manifest. A bundled Firefox XPI takes precedence over a store link even in an online-enabled release. The XPI must contain the matching Firefox manifest and signature metadata; final cryptographic verification remains Firefox's responsibility. Mozilla signing requires the distributor's account and is not performed by the build script. No placeholder store listing is shipped.

For a separate online distribution, explicitly set `allowOnlineInstall: true`, each browser's issued `extensionId`, and its real official `installUrl` (Chrome Web Store, Edge Add-ons, or AMO). Store URLs are validated before registration changes. Those optional online releases are not suitable for the standalone offline target described above.

The setup bridge uses immutable, versioned directories so a repair does not overwrite a running host. Registration is published only after files are prepared, in the current user's 32-bit and 64-bit registry views. Previous host directories are retained because another process may still use them. Unpacked extensions keep a stable key and path. Browser data and profile identities are preserved.

### Manual installation (advanced)

1. Run the generated `Install-BrowserHost.ps1` as the normal Windows user. It installs under `%LOCALAPPDATA%\Programs\AppSwitcherBrowserHost` and registers HKCU native-messaging manifests for all three browsers. No administrator or listening TCP port is required.
2. Chrome/Edge development deployment: open `chrome://extensions` / `edge://extensions`, enable developer mode and use **Load unpacked** on the `chromium` directory. The source manifest's public key fixes the unpacked ID to `afmmehaldghpcccgholfklbeemokekoj`.
3. Firefox development: use `about:debugging` to load `firefox/manifest.json` temporarily. **Temporary installation does not survive restart.** Production Firefox requires a Mozilla-signed XPI (AMO listed or unlisted) with ID `browser@appswitcher.local`. The default build includes the approved signed 1.1.0 XPI and also produces an unsigned ZIP for future submissions. Signing a new extension version requires the distributor's account and is not performed by the build script.
4. For store-issued Chrome/Edge IDs, run the installer with `-ChromeExtensionId <id> -EdgeExtensionId <id>`. Reinstall the native manifest whenever the distribution ID changes. Chrome/Edge store distribution and enterprise deployment remain distributor actions.
5. Open AppSwitcher and the intended browser profile. Optionally name the profile using the extension popup. In application settings choose **Website in a managed browser tab**, enter the URL, refresh connected profiles and select the correct profile. Save.
6. Use **Check connection** to verify the host/profile and **Test switching to existing tab** after the first launch. These checks never create tabs.
7. Enable session restoration in that profile. Launch once, keep its group intact, minimize the window and click the AppSwitcher entry again.

An existing direct URL or Chrome/Edge/Firefox executable entry with one URL can use **Convert existing URL to website**. Conversion requires explicit profile pairing; legacy URL launchers are blocked from silently using executable/title matching. Config version 10 introduced persistent IDs for migrated entries; version 11 preserves them.

Optional automatic startup requires the exact browser executable and profile folder. For Chromium this is the profile directory (for example `...\User Data\Default`), not just `User Data`; for Firefox use its profile root. AppSwitcher starts the configured profile without a URL only when that browser has no running processes. If another profile is already running or the connection is unavailable, open the paired profile normally. AppSwitcher waits for its extension instead of spawning another browser repeatedly.

## Recovery and operation

- **Disconnected:** open the paired profile, enable its extension, check the host registration, then retry. The extension retries every five seconds while active, with an alarm fallback.
- **Recovering after restart:** restore the browser session or original closed window. Leave the original group intact. A startup delay is not proof that the website is absent.
- **Identity conflict:** restore the original two-tab group. Never resolve this by choosing whichever page has the same URL. With Firefox, the session value can still identify the original page when its marker is missing.
- **Changing profiles:** stop the website through AppSwitcher on the original profile first, then change its pairing. Do not clone a profile containing the same extension identity.
- **Permanently lost identity:** recover from the browser/session and application backups. If recovery is intentionally abandoned, remove the old entry and create a new one only after manually checking and closing its old managed tabs. This is an administrative reset with a new identity, not automatic recovery.
- **Damaged journal:** preserve `%LOCALAPPDATA%\AppSwitcher\browser-ownership.json` for recovery; automatic creation is disabled. Do not clear it to force a launch. Back it up along with the application configuration and browser profile.

Website entries do not use native window placement, per-process resource control or AppSwitcher kiosk enforcement because browser windows/processes can contain unrelated pages. Validation rejects incompatible placement/kiosk configurations. Browser kiosk deployments need their own restricted profile and policy design.

`Uninstall-BrowserHost.ps1` removes only registrations pointing to this installation. It intentionally preserves extension state, browser profiles and the desktop ownership journal.

## Implementation

`BrowserBridgeService` owns a current-user named pipe and per-application command locks. Browser-launched native hosts forward bounded, length-prefixed JSON over that pipe. Host manifests and `allowed-origins.json` restrict extension IDs; the host writes diagnostics only to stderr. No content scripts, all-sites host access, remote debugging or web-accessible control endpoint are used in production. The trust boundary is the Windows user account, not protection from other programs already running as that user.

The desktop journals a generation and `Issued` state before a create request. The extension separately journals each creation stage before side effects. It creates identifiable placeholders, groups them and navigates the primary once. Retries inspect those identities and continue only provable steps. Both journals survive process restarts. Hooks run for a new creation/explicit stop, not ordinary activation. Unknown browser state suspends watchdog recovery.

Foreground permission is granted to the browser process derived from the OS-authenticated pipe client's ancestry. The extension activates the tab, restores its window, requests focus and verifies the browser-reported state. Failure preserves the existing tab and displays an error.

## Verification

```powershell
dotnet test AppSwitcher.sln -c Release
node --test browser-extension/tests/core.test.mjs
dotnet run --project AppSwitcher.UiChecks -c Release
```

The regression suites cover concurrent clicks, lost responses at creation/close stages, title/URL changes, same-URL unrelated tabs, new IDs after restart, missing/duplicated identities, profile collisions, foreground denial, protocol framing, durable ownership and activation-only behavior. UI checks render the website editor in both languages/themes and confirm that the user's saved configuration is untouched.

Guided setup validation on 2026-11-02: 200 .NET tests and 3,886 native UI assertions passed, including owner close/session expiry during installation, disconnected/duplicate profiles, preservation of an existing pairing, and compact English/German views in both themes. Thirteen package checks installed the published payload into isolated temporary directories for all three browsers, exercised idempotent preparation and started each self-contained host to verify rejection of unapproved origins. They did not modify the user's browser registrations. Store-install approval and signed-XPI installation remain release-specific checks once those distribution artifacts exist.

The subsequent standalone/offline revision passed 207 .NET tests and 3,850 native UI assertions. Additional coverage verifies that store links cannot override the offline default, local Firefox files take precedence over online links, installation copies the path before opening the browser, a busy clipboard has a manual fallback, an existing connection leaves the clipboard alone, and missing Firefox files show a blocked package rather than an installation wait. Fewer visible controls in the simplified dialog account for the lower UI assertion count. The strict Firefox release preflight also rejected a missing XPI before creating build output. The saved user configuration remained unchanged.

```powershell
dotnet run --project AppSwitcher.UiChecks -c Release -- --setup-package-check artifacts/browser-package/app/browser-setup
```

Validation on 2026-11-02: 124 .NET tests, 29 extension tests and 1,267 native UI assertions passed. Chrome 156, Edge 156 and Firefox 159 passed real native-host checks with 20 minimize/activate cycles each and unrelated same-URL tabs. Full restart recovery passed in bundled Chromium and Firefox Developer Edition 160 with a persistent test installation. Firefox also recovered using its session identity after marker removal. Production signed Firefox and store-distributed Chrome/Edge packages still require distribution-specific qualification; temporary debugging installs cannot establish that deployment result.

Real browser testing uses isolated profiles and the synthetic page served by `scripts/browser-check-broker.mjs`. `prepare-browser-checks.ps1` registers a separate `com.appswitcher.browser.checks` host and copies extensions into `output/playwright`; it refuses to run while AppSwitcher is running. The copied extensions receive a fresh per-run profile marker, and the broker rejects profiles without that exact marker, so a normal browser profile cannot replace the test connection. Start the broker using its printed command, including the identity-file argument. The normal app and other real-host checks must remain closed during these checks because the test broker occupies the current-user pipe.

`check-chromium-browser.js` and `check-chromium-restart.js` are Playwright CLI `run-code` templates: replace `__BROKER_URL__` / `__BROKER_TOKEN__` from `output/playwright/broker.json` and `__BROWSER__` with `Chrome` or `Edge`. Launch a persistent, headed test profile. CDP `Extensions.loadUnpacked` needs `--enable-unsafe-extension-debugging` and a browser-level CDP session. This debugging install is temporary; for restart tests use an extension installed persistently or bundled test Chromium with `--load-extension` on each startup and `--restore-last-session`.

For Firefox, install the Python `marionette-driver` package and run `py scripts/check-firefox-browser.py <firefox.exe>`. `--persistent-addon` tests full restart using an unsigned-capable development/test Firefox with an isolated test preference; use a signed package for production release qualification. No preference is changed in the user's normal profile. Close the test browsers and broker, then run `cleanup-browser-checks.ps1` to remove only the test host registrations.

Run browser scenarios sequentially: the synthetic broker's intentional disconnect step disconnects its test clients. For full Chromium session restoration, use `node scripts/check-chromium-session.cjs <bundled-chromium.exe> [playwright-package-directory]` after preparing and starting the broker. The optional second argument identifies an already installed Playwright package, such as the CLI's package dependency. The helper launches a fresh isolated profile, reuses the same check templates, closes/reopens the browser without navigating restored pages, and retains a compact result before removing its successful test profile. Opening a restored session with a tool that automatically navigates a selected tab to `about:blank` can invalidate an identity marker and does not test an untouched browser restart.

## API references

- [Chrome native messaging](https://developer.chrome.com/docs/extensions/develop/concepts/native-messaging)
- [Chrome tabs](https://developer.chrome.com/docs/extensions/reference/api/tabs), [windows](https://developer.chrome.com/docs/extensions/reference/api/windows), [tab groups](https://developer.chrome.com/docs/extensions/reference/api/tabGroups)
- [Firefox persistent tab values](https://developer.mozilla.org/en-US/docs/Mozilla/Add-ons/WebExtensions/API/sessions/getTabValue) and [tab groups](https://developer.mozilla.org/en-US/docs/Mozilla/Add-ons/WebExtensions/API/tabGroups)
- [Firefox signing and distribution](https://extensionworkshop.com/documentation/publish/signing-and-distribution-overview/)
- [Chrome installation and user approval on Windows](https://developer.chrome.com/docs/extensions/how-to/distribute/install-extensions)
- [Edge alternative distribution](https://learn.microsoft.com/en-us/microsoft-edge/extensions/developer-guide/alternate-distribution-options)
- [Firefox extensions accompanying desktop applications](https://extensionworkshop.com/documentation/publish/distribute-for-desktop-apps/)
- [Playwright extension testing](https://playwright.dev/docs/chrome-extensions)

Signed release integration on 2026-11-02: Mozilla file 5198435 / add-on 3115878 was approved, then installed by the user in regular Firefox. Firefox reports version 1.1.0 active and signed (`signedState: 2`). All extension payload bytes match the submitted ZIP. See [signed release provenance](../browser-extension/signed/README.md). This confirms the installed artifact and source match; it is not a claim of a fresh installation test on an air-gapped PC.

The complete signed package passed 15 native package assertions, including copying the XPI byte for byte into the permanent setup directory. A modified-payload fixture was rejected before app publication. The installed AppSwitcher package received only the new signed setup asset and updated documentation; no browser profile or application configuration was changed.
