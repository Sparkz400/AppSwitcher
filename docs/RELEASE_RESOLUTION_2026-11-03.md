# Release candidate hardening — 3 November 2026

Target: **1.2.0-rc.1**, followed by **1.2.0** after the remaining deployment gates. This resolves the software defects in the [audit of 4519900a](RELEASE_AUDIT_2026-11-03.md). The audit is historical; it is not the candidate's validation record.

## Resolved defects

| Finding | Result and regression coverage |
| --- | --- |
| B01 | Monotonic session clock; off/on restarts correctly; import/reload/Undo reconcile the actual timer. Queued expiry rechecks the current generation/activity. `ReleaseRegressionTests`, background-service and native expiry tests. |
| B02 | Schema 12 distinguishes unrestricted access from an empty restricted selection. Clear/retype, duplicate-name intermediate edits and removal of the last allowed app/group cannot implicitly grant all-app access. Explicit “Allow all applications” is required. Scope serialization and editor regressions. |
| B03 | Background placement leaves a manually minimized window minimized when continuous placement is off. Initial observation, explicit activation and continuous placement retain restoration. Native platform fixture tests both policies. |
| B04 | Cancellation kills/reaps the live owned hook process tree. The real-process regression checks both script and child PIDs; unrelated processes are never selected by name. |
| B05 | Scheduler pending entries are registered before execution and removed by identity. Immediate failure/completion can retry; concurrent callers share the pending operation. |
| B06 | Valid unique renames update dependency names and saved layout labels/IDs. Deletion shows affected dependencies/workspaces and removes their references only after confirmation. Duplicate intermediate names do not retarget permissions. |
| B07 | URL, process identity and running-state changes invalidate HTTP results. Clearing a failed URL clears its HTTP warning; obsolete in-flight responses cannot overwrite the new state. Responsiveness remains independent. |
| B08 | Websites hide native arguments/memory limits and display unavailable tab metrics honestly. Switching target kind clears incompatible native settings; migration removes previously ineffective website values; new invalid combinations are rejected. |
| B09 | Removed inactive border/glow controls; normal launcher remains borderless. Size minimum is 44px. Menu width/row height now have supported controls and consistent defaults. |
| B10 | Icon-only hides text while retaining its accessible name/tooltip; text-only hides the icon; combined shows both. Native visual/automation assertions cover the modes. |
| B11 | Validate finite dimensions/positions, opacity, supported theme/language/shape/icon/display modes, colors, future schemas and layout geometry. Known legacy size defaults migrate; malformed values fail before application. |
| B12 | Read CSV records backwards in bounded chunks, preserving UTF-8, quotes, commas and multiline details. A 100,000-row tail regression stays below 2 MiB allocation. Audit and crash logs rotate at 16 MiB with five archives. |

## Settings and performance

Settings remain a **live preview**, explicitly labeled once in the footer. Save persists; Close asks to discard pending edits; session expiry discards and closes privileged dialogs. Background pin/position saves are deferred while an editor is open. Undo retains runtime objects. A fully transactional operational editor remains an optional future architecture change, not a claim made by this candidate.

Settings search now covers retained visual, security and application controls, chooses an applicable native/website entry, opens its disclosure and focuses the field. Every nonempty catalog target is exercised through native keyboard focus. Compact navigation wraps labels, duplicate headings are reduced, numeric input errors appear inline, and audit display labels are localized without changing stored codes. Browser setup explicitly distinguishes connection from unverified restart recovery. Existing “from open window”, desktop and website entry paths remain available.

The 10/100/500-app editor benchmark uses actual WPF rendering and 20 combined name/size/search edits. At 500 entries, allocation dropped from **1,727,522,488 to 70,815,008 bytes** and median edit+render time from **465.45 to 82.28 ms**; p95 dropped from **532.71 to 117.41 ms**. The harness includes a render delay. These are development-machine comparisons, not target-panel budgets or production startup measurements. Removed duplicate snapshots and unnecessary list rebuilding; no language rewrite or new runtime dependency was needed.

Per-app operation locks now use 64 fixed stripes instead of retaining every historical identity. Collisions serialize independent operations without weakening same-key exclusion. Browser bootstrap cooldown entries expire. Editor subscriptions are detached on disposal. Immutable browser-host versions are deliberately retained for registration ownership and rollback rather than deleted speculatively.

Legacy `AppSettings.BorderColor`, `GlowEnabled` and `AppItem.FgColor` are tolerated for compatibility but are retired visual inputs. Normal UI colors use supported launcher colors, app accents and the active theme. High contrast intentionally supplies its own visible outline and colors. Menu dimensions are no longer hidden configuration-only options.

Hook contract: scripts must wait for their work/children before returning. Cancellation stops a still-running script's process tree. A script that intentionally launches a detached service and exits has transferred ownership; AppSwitcher does not claim to discover or terminate unrelated background services afterward.

History retention: the current audit/crash CSV plus `.1`–`.5` archives are kept. Older archives are replaced only on rotation; archive/export them before the retention limit if longer history is required. Recent-history calls cap at 5,000 records and skip malformed or over-1-MiB records rather than allocating unbounded memory. Existing archives remain readable as CSV.

## Verification boundaries

- Locked Release restore/build passed with zero compiler warnings/errors; 273 .NET regressions and 32 Node extension/broker tests passed.
- Final native UI verification passed 10,088 assertions with no binding warnings; counts include visible-control enumeration and vary with layout. Process tests restored/focused the same synthetic window 30/30 times (median 19.10 ms, p95 38.30 ms). Authenticated startup recovery passed. The final CI run validates the committed candidate independently.
- Chrome and Edge each passed 20 minimize/activate cycles, preserved the document/unsaved value and unrelated same-URL tab, and recovered after host disconnect. Firefox 159 passed 20 cycles, session identity recovery and unrelated-tab preservation. Bundled Chromium 156 and Firefox Developer Edition 160 passed full close/reopen recovery. These use isolated test profiles/extension copies, not a fresh offline production-package installation.
- The isolated real-app churn run passed **605.46 seconds**, **59 samples** and **one abrupt launcher restart**, preserving the synthetic managed process. Tested AppSwitcher assembly SHA-256: `44F6A757B16889C0014CA0AAB82B764C69DDE50C6EC23744BD31D17112222F77`. It includes test dependencies and predates final UI/build-script changes; current-commit CI validates the final source. This is not a full-shift or production-EXE startup measurement.
- All isolated check registrations were removed; normal browser profiles/registrations were preserved. The user's encrypted saved configuration hash stayed unchanged.
- Stable artifact validation accepts unsigned or validly signed installers and rejects 28 malformed inventories/packages or invalid signatures. Readiness tests accept qualified unsigned silent-workstation evidence and reject eight incomplete/invalid qualifications. Signing adapter tests cover unsigned candidates, a synthetic configured signer and five rejected signing failures; they do not establish a real publisher signature.

## Release and repository contract

Windows 11 x64 is the release target; the installer retains its existing compatible Windows 10 build 19041+ allowance, without asserting vendor support or site qualification. Chromium extension API minimum is 120; signed Firefox minimum is 142. Qualify the exact deployed browser versions, especially on offline machines. Desktop runtime is bundled. Chrome/Edge unpacked setup and Mozilla-signed Firefox 1.1.0 remain the offline distribution routes; store accounts are optional.

Beta, RC and stable tags use the same exact-main-CI artifact, manifest/checksum/SPDX, scanner and build-attestation gates. Stable additionally requires reviewed qualification records. Following the owner's zero-cost distribution decision on 3 November 2026, unsigned stable releases are supported; Windows publisher signing is optional, not a release gate. Any optional installer signature must be valid. Credentials are neither supplied nor installed by the repository. See [Maintenance](../MAINTENANCE.md).

Use `scripts/verify.ps1 -Fast` for the build/unit/browser/package-gate layer; the default command and required Windows CI retain the complete UI/process/recovery checks. The manual **Isolated sustained performance** workflow offers editor and 1–45 minute synthetic churn runs. Branch/tag protection is retained. No invented second reviewer or unstaffed approval rule is added. Legacy standalone host scripts stop safely and direct users to the maintained UI/installer paths.

Rollback requires the prior binary **and pre-upgrade configuration backup**. Older binaries cannot represent an empty restricted scope safely. Preserve browser profiles/ownership journals; never reset them merely to force a new tab.

## Publication decision and remaining deployment acceptance

The [offline Windows VM qualification](VALIDATION.md#offline-windows-vm-qualification--3-november-2026) covers published installer installation/upgrade/rollback/uninstall, installed-EXE startup, signed Firefox restart and released bridge recovery. Chrome/Edge persistent unpacked installations passed two unattended offline restarts each without extension-loading commands or unsafe extension flags, including published desktop-bridge restart and unrelated-tab preservation. Installation used browser APIs after a one-time test bootstrap; their native folder-picker/manual installation walkthrough remains untested. The owner subsequently [authorized public 1.2.0 publication with these deployment limits disclosed](RELEASE_ACCEPTANCE_1.2.0.md); the [readiness record](release-readiness.json) records explicit deferral without claiming passed site acceptance. Exact-main CI, scanners and artifact/attestation validation remain required. The final workstations have no audio output, so actual Narrator speech testing is optional and unverified. Windows publisher signing is optional under the zero-cost decision. Keyboard/visual feedback, high contrast, touch/gloves, physical docking/mixed DPI, reboot/power interruption, kiosk escape and representative full-shift operation still require the intended workstation's acceptance record.

Useful follow-ups, not prerequisites to this candidate: a full workspace membership/monitor editor, stable-ID dependencies/scopes, transactional operational settings, portable configuration bundle/preview, richer commissioning and schedule preview. Current reference repair and capture/restore remain supported. Track these separately rather than adding a cloud account, updater, store requirement or Rust component to the offline switcher.
