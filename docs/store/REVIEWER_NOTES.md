# Submission checklist and reviewer notes

Prepared on 2026-11-02. These are submission files, not an approved store release. No publisher account is required to build them. Account registration, owner/contact verification, dashboard attestations and actual store review belong to the publisher's later submission.

## Files

- `chrome-upload-1.1.1.zip` and `edge-upload-1.1.1.zip`: Manifest V3 packages with 16/32/48/128 PNG icons. The offline development key is omitted so a new listing can receive its store identity.
- `firefox-upload-1.1.1.zip`: unsigned Mozilla submission source, preserving `browser@appswitcher.local`. The owner must use the account that controls that existing add-on identity, or arrange the appropriate transfer/new identity and update desktop registration. It cannot be installed permanently on normal Firefox until Mozilla signs it.
- `assets/icon-128.png`: store logo, extracted losslessly from the project's multi-size ICO. The symbol is irregular and uses transparency; inspect it in each dashboard's light/dark preview.
- `assets/browser-connection-1280x800.png`: actual extension profile page in an isolated Chromium profile, with synthetic profile name/ID. The capture copy used a nonexistent native-host name to avoid contacting a real desktop instance; its UI was unchanged. It demonstrates profile naming, not a successful desktop pairing.
- `LISTING.md`, `PRIVACY.md` and these notes: listing text, privacy disclosure, permission justifications and reviewer steps. Copy the appropriate fields into the dashboard; do not upload this whole preparation bundle as an extension.

The upload preparation changes only manifest version/icons, adds icon files and removes the Chromium development key. Its JavaScript/HTML/CSS is byte-for-byte the offline 1.1.0 source. The signed offline Firefox XPI and offline source manifests remain unchanged. A future signed 1.1.1 release must be reconciled with source manifests and the package's strict signed-payload check before it replaces the offline XPI.

## Single purpose and permissions

Single purpose: connect the local AppSwitcher launcher to its assigned website tabs and recover/activate those tabs without guessing ownership.

| Permission | Reason |
| --- | --- |
| `tabs` | Inspect tab identifiers/URLs and operate the explicitly managed tab; compare ownership markers while leaving unrelated tabs alone. |
| `storage` | Retain local profile identity and a durable managed-tab journal across browser restarts. |
| `nativeMessaging` | Exchange commands and status with the separately installed local Windows bridge. |
| `tabGroups` | Keep the primary/identity tabs in an identifiable managed group for safe recovery. |
| `alarms` | Reconnect and refresh state when an extension background worker has been suspended. |
| `sessions` (Firefox) | Store/recover tab ownership identity across supported session restoration. |

No host permissions, content scripts, remote code, analytics, advertisements or external data-collection endpoints are requested. Incognito operation is disabled. Privacy-form answers must still disclose local access to tab URLs/identifiers and communication with the desktop companion; do not describe this as having no data access. The Firefox manifest declares no off-device collection. Re-check the exact dashboard definitions before making publisher attestations.

## Reviewer setup

1. Use a disposable Windows user/profile and the public AppSwitcher beta package. Install the app and its local bridge using the documented browser setup. No website credentials are required for testing.
2. For a Chrome/Edge draft listing, record the **issued store extension ID**. From the complete desktop package, run `Install-BrowserHost.ps1` with the corresponding `-ChromeExtensionId` / `-EdgeExtensionId`. This registers the real store origin; the default offline ID does not identify the new store listing.
3. Start AppSwitcher, choose a PIN for the disposable configuration, and pair the test profile. Add a website named `Example dashboard` using `https://example.com` and the connected profile.
4. Activate it repeatedly, then minimize and activate again. Verify that the same managed tab is restored. Open an unrelated tab at the same URL; it must not become the managed tab.
5. Enable browser session restoration, restart the browser and verify ownership recovery. Remove/duplicate an identity marker only in the disposable test profile and verify that the app reports uncertainty rather than opening duplicates.
6. Close AppSwitcher and confirm the extension tolerates a disconnected local host. Restart AppSwitcher and reconnect. Remove the test extension/host registration and test profile afterward.

Before a public online desktop distribution, put the actual issued IDs and approved listing URLs into a separate `distribution.json`, set `allowOnlineInstall: true`, and build with `build-browser-package.ps1 -DistributionFile`. Verify the resulting real store packages and pairing. The unsigned beta remains offline by default; its identifiers and installation behavior are not changed by generating these files.

## Current publisher references

Store image requirements and dashboard fields can change. These sources were checked when preparing the files: [Chrome store images](https://developer.chrome.com/docs/webstore/images), [Chrome extension identity](https://developer.chrome.com/docs/extensions/reference/manifest/key), [Edge submission](https://learn.microsoft.com/en-us/microsoft-edge/extensions/publish/publish-extension), and [Mozilla submission](https://extensionworkshop.com/documentation/publish/submitting-an-add-on/). A 128-pixel logo and 1280×800 screenshot cover the required prepared assets; optional promotional tiles/video are omitted.
