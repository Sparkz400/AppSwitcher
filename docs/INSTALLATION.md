# Install, update and roll back

Use a versioned release from the project's GitHub Releases page. The setup executable and portable ZIP contain the application, .NET Windows runtime, native browser bridge, offline extensions, guides and examples. The target PC needs no SDK, publisher account or internet connection for installation. A browser must already be installed; websites retain their own connectivity requirements.

AppSwitcher Windows releases are **unsigned**, including stable releases, under the project's zero-cost distribution policy. Windows may display an unknown-publisher or reputation warning. Check the release source and compare the downloaded file with its adjacent SHA-256 file (`Get-FileHash -Algorithm SHA256 <download>`). A matching checksum detects a changed download; it does not replace publisher authentication. Follow your organization's software approval policy. Beta.3 and later also provide [signed build/release records and a dependency inventory](VERIFY_DOWNLOADS.md). For common warnings, connection and display problems, see [Troubleshooting](TROUBLESHOOTING.md).

## Install

1. Keep a copy of the release ZIP/setup and its checksum on your deployment media.
2. Close AppSwitcher and paired browser profiles. Setup never closes managed applications for you. Running AppSwitcher or a locked browser bridge blocks installation before payload changes.
3. Run the setup executable as the intended Windows user. It installs per user to `%LOCALAPPDATA%\Programs\AppSwitcher` without requiring administrator access. The Start menu shortcut opens AppSwitcher; setup does not launch it automatically.
4. Open AppSwitcher, set an administrator PIN, and follow **Settings → Setup checklist**. Add applications or import one of the examples. Review automatic launch, scripts and kiosk choices before enabling them.
5. Pair browser profiles using the in-app setup. An application install does not change browser preferences or install extensions automatically.

The ZIP is an alternative: extract the entire directory and run `app\AppSwitcher.exe`. Do not copy only the EXE or overlay different versions of a portable directory. Keep old and new portable packages in separate directories. The setup installer and portable copy share the current Windows user's application data; run only one at a time.

## Offline update

1. Export the configuration from Settings, and preserve a copy of `%LOCALAPPDATA%\AppSwitcher`, including backups, separately from the installation. Keep the previous release installer or complete ZIP.
2. Test the candidate on a representative spare workstation. Check browser pairing, display placement, operator access and kiosk escape before production deployment.
3. Close AppSwitcher and paired browsers, then run the new installer into the same installation directory. It replaces shipped files and keeps configuration, logs and backups in the separate user-data directory. It also preserves user-created files in the installation directory.
4. Start AppSwitcher deliberately. Saved automatic launch and kiosk settings apply at startup. Verify the setup checklist and the site's acceptance record before returning the workstation to service.

Moving a portable package or changing an installation path can leave browser native-host registration pointing to the previous path. Re-run browser setup from the new location; do not remove a still-registered old package first.

## Roll back

Close AppSwitcher and paired browsers and run the retained older installer into the same directory (or start the retained complete portable directory). If an older app cannot read the newer configuration, use its recovery screen with the pre-update backup and that backup's administrator PIN. Preserve current data before restoring. Compatibility with an unspecified future configuration format is not promised.

The automated installer check exercises a complete real payload, a changed candidate payload and the previous payload again, checking every installed file's hash. This verifies the installation mechanism and preservation, not every possible future schema migration. Startup recovery and backup validation have separate application tests.

## Uninstall

Close AppSwitcher and paired browsers. If the browser bridge was installed, use the documented bridge removal procedure in [Browser integration](BROWSER_INTEGRATION.md) and remove the extension from the relevant profiles. Then use Windows **Installed apps → AppSwitcher → Uninstall**. AppSwitcher and bridge file locks also block uninstall. The uninstaller removes only installed files and shortcuts, and keeps user data and user-created files. It does not disable Windows kiosk policy; restore the site's normal shell and exit policy using the kiosk maintenance workflow before removing a kiosk deployment.

## Reproduce installer checks

`scripts/build-release.ps1` runs `scripts/test-installer.ps1` against its complete package before emitting the distributable installer. The checks use a unique product ID, mutex, registration and installation directory, leave the production configuration unchanged, and remove their own successful test installation. Reports and setup logs are retained under `artifacts/test-results/installer`. Failed runs keep their isolated directory for investigation.

The compiler is pinned to Inno Setup 7.1.1 and its official SHA-256 and valid publisher signature are checked before a portable workspace-local installation. No system PATH changes or global compiler installation are required. Installer behavior follows [Inno Setup's application mutex](https://jrsoftware.org/ishelp/topic_setup_appmutex.htm) and [installation/uninstallation events](https://jrsoftware.org/ishelp/topic_scriptevents.htm).
