# Common setup problems

Start with the complete package from [Releases](https://github.com/Sparkz400/AppSwitcher/releases). These checks preserve your configuration and browser data. For installation, updates and rollback, use the [installation guide](INSTALLATION.md).

## Windows shows an unknown publisher or reputation warning

The Windows beta is unsigned. An unknown-publisher message is expected; a changed download or a malware detection still needs investigation.

1. Confirm that the download came from `Sparkz400/AppSwitcher` on GitHub and that its version matches the release you intended to install.
2. Compare its SHA-256 with the adjacent checksum file. For beta.3 and later, you can also [verify the signed build and release records](VERIFY_DOWNLOADS.md).
3. If the checksum differs, discard that download and download the same version again. If the warning is a malware detection or your organization's policy blocks the file, stop and ask the workstation administrator to review it. Do not disable Windows protection to install it.

A matching checksum detects a changed file. GitHub attestations identify the build and release; they do not provide a Windows publisher signature or certify the application as safe for your workstation.

## The browser is disconnected

Open **Settings → Browsers** and select **Setup** for the browser you use. The extension and native host must be available to the intended Windows user and browser profile.

1. Start that browser profile and check that the AppSwitcher bridge extension is installed and enabled there. Chrome, Edge and Firefox use separate installation steps; follow the [browser setup guide](BROWSER_INTEGRATION.md).
2. If you moved a portable installation or changed its path, run **Setup** from the new complete package. Registration may still point to the old native host. Keep the old package until the new connection works.
3. If setup says **Setup files are missing. Use the complete AppSwitcher browser package.**, restore the complete distribution. For **Firefox setup needs an additional file**, use a package containing the signed Firefox XPI. A temporary Firefox extension disappears after restart.
4. Restart the intended browser profile and check the connection again. If it stays disconnected, inspect the diagnostic status and browser extension errors. Browser policy can prevent native messaging or extension installation; ask the administrator responsible for that policy.

**Recovering** or an identity conflict is different from a disconnected bridge: preserve the pairing records and follow the recovery guidance in [Browser integration](BROWSER_INTEGRATION.md). Do not erase ownership records, reuse another profile's identity or try to repair pairing by matching tab URLs manually.

## A saved display is missing

**Saved display unavailable — using primary** means the saved Windows display identity is not currently available. AppSwitcher falls back to the primary display; it does not silently replace the saved identity.

1. Check the cable, dock and Windows display arrangement. Reconnect the intended display, then check the affected application or layout again.
2. If the replacement display is intentional, select the connected display in the relevant window-placement or layout controls and review the result. For the launcher itself, review **Preferred Monitor** in settings.
3. Save and recheck placement before returning the workstation to use. Dock, connector and driver changes can change display identity. Never assume that a reused display number identifies the same physical monitor.

For an invalid saved display identity, select a connected display again or restore a known-good configuration using the normal recovery flow. Keep a backup before changing layouts. See [Operations](OPERATIONS.md) and the display checks in [workstation acceptance](WORKSTATION_ACCEPTANCE.md).

## Still stuck?

Use [Getting help](../SUPPORT.md). Include the version, exact message and a small synthetic reproduction. Inspect and remove PIN hashes, credentials, private URLs, machine names and personal paths before sharing logs or screenshots publicly.
