# AppSwitcher Browser Bridge privacy information

Effective: 2026-11-02. Applies to the AppSwitcher Browser Bridge extension and its local Windows bridge.

The extension's purpose is to let the locally installed AppSwitcher application manage its assigned website tabs. There is no project-operated server, advertising, analytics, account service or sale of data. Extension code does not send browsing data to an external service or download executable code.

## Data used locally

- A random browser-profile identifier, a profile name chosen by you, and a session identifier distinguish the connected browser profile.
- Local managed-tab records contain application/generation identifiers, requested website addresses and names, tab/window/group identifiers, recovery stages and ownership markers. Firefox can also keep ownership values in browser session storage.
- The extension queries open-tab metadata, including addresses and identifiers, to find its own markers and avoid taking over an unrelated tab. It does not read page contents, passwords, form inputs or browser history databases. Browser APIs may expose titles alongside tab metadata; they are not a telemetry feed.
- The native messaging bridge exchanges requests and managed-tab status with AppSwitcher through a pipe restricted to the current Windows user. It does not connect to an internet server. AppSwitcher stores configuration, profile/ownership records, operational logs and audit history locally under that user's application-data directory.

Opening a configured website is normal browser navigation. That website and the browser vendor have their own privacy practices. Website addresses may contain sensitive query values, and local configuration/logs may identify applications or workstations. Avoid putting secrets in URLs and protect these files using Windows permissions and your organization's policy.

## Retention and removal

Profile and recovery records remain locally to support reconnection and restart recovery. Uninstalling the extension removes its extension storage through the browser; browser session/history records follow the browser's own clearing settings. Remove managed entries and profiles from AppSwitcher as needed. Its exported backups and user-data directory are deliberately preserved by the Windows uninstaller; delete your own retained copies separately when they are no longer required. Removing the browser bridge registration does not erase application backups or browser history.

The project does not receive this local data unless you deliberately share it in a support or security report. Public issue attachments are public: use synthetic data and redact identifiers, customer URLs, paths and secrets. For security concerns use [private vulnerability reporting](https://github.com/Sparkz400/AppSwitcher/security/advisories/new); for general questions use [project issues](https://github.com/Sparkz400/AppSwitcher/issues).
