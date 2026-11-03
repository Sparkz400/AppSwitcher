# Workstation acceptance record

Complete this record on the intended spare/commissioning workstation before enabling production autostart or kiosk restrictions. Keep the completed record with the site's deployment evidence, not in a public issue. An unchecked item is not a pass.

Record release filename/SHA-256/source commit, Windows edition/build, user/account policy, exact browser versions, display models/ports/scales, touch hardware/glove type, configured applications, tester/date and recovery owner. Use synthetic data until site authorization permits production inputs.

Deployment scope confirmed on 3 November 2026: the final workstations have no audio output. Narrator speech testing is outside this acceptance scope and remains optional, unverified follow-up work. Keyboard, visual feedback and touch checks below are still required; errors and recovery instructions must be usable without sound. Automated accessible-name/UI Automation checks remain enabled.

| Check | Procedure and required outcome | Result / evidence |
| --- | --- | --- |
| Install offline | Disconnect the test PC from external networks under site procedures; install the complete release and launch it without SDK/runtime downloads. Distinguish browser background traffic and website connectivity from application installation. | Not run |
| Update and restore | Export data, retain the prior installer, update, verify PIN/scopes/layouts/browser pairing, then roll back and restore the pre-update backup. Preserve current data before restoring. | Not run |
| Startup recovery | On a copy of the test configuration, verify malformed configuration shows recovery before any managed app or kiosk enforcement starts. Require the backup PIN and confirmation; verify original bytes are preserved. | Not run |
| Process interruption | During a planned test, terminate AppSwitcher and restart it. Managed applications must remain discoverable without duplicate launches. Review any watchdog recovery and audit trail. | Not run |
| Reboot/power loss | Use the site's approved controlled shutdown/reboot and power-interruption procedure on disposable test data. Check configuration/backups, browser journals, automatic launch and recovery. Process-kill automation is not evidence for this row. | Not run |
| Browser restart/offline recovery | Pair each deployed browser/profile, manage a synthetic page, minimize/activate, restart with session restoration, temporarily disconnect the host/network, and reconnect. Retain the same owned tab; unrelated same-URL tabs must be untouched. Test the actual signed/offline or store package being deployed. | Not run |
| Display change | Verify launcher, forced placement and saved layouts with the site's monitors, negative coordinates, changed primary display, mixed DPI, unplug/reconnect and docking. Missing displays must fall back safely without silently changing the saved identity. | Not run |
| Keyboard and visual feedback | In English and German, navigate launcher/search/settings/PIN/recovery/dialogs and custom scrollbars with keyboard only; confirm a keyboard path for every action and predictable visible focus. Verify PIN errors, session expiry, states and recovery instructions are visible and usable without sound. Toggle Windows contrast themes live and restore the user's theme. | Not run |
| Touch/gloves | Use the intended panel, scaling and gloves for launch, monitoring, PIN, scrolling, confirmation and escape. Record missed targets or accidental actions, and adjust supported sizing settings before retrying. | Not run |
| Operator/admin boundaries | Test assigned app/group scopes, expired sessions, wrong-PIN delays, restore/import and privileged dialogs. Verify Windows user permissions separately from the application's PIN. | Not run |
| Kiosk and independent escape | With a second administrator/recovery path available, verify the exact Windows edition's policies, reboot behavior, maintenance access and restoration of the normal shell. Do not enable restrictions on an active production PC during testing. | Not run |
| Sustained operation | Run the representative site's workload for its agreed acceptance duration (a full shift at minimum is a useful starting point), including browser/application recovery. Record elapsed wall time, failures, responsiveness and memory/handles. A short synthetic run does not close this row. | Not run |

The [offline VM results](VALIDATION.md#offline-windows-vm-qualification--3-november-2026) provide software evidence for the published RC. They do not fill in this site's unperformed hardware/workflow rows.

Decision: **not accepted until the responsible operator records the applicable results and approves the site deployment**. Track failures with a minimal reproduction and retest the corrected exact release. Windows publisher signing and browser-store publication are optional under the zero-cost offline distribution policy, including stable releases.
