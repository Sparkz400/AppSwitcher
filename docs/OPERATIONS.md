# Operation and performance additions

## Configuration recovery

If saved settings cannot be read or validated, AppSwitcher opens a recovery screen before starting monitoring, kiosk enforcement or configured applications. Select a backup, enter that backup's administrator PIN if it has one, and review the confirmation. A successful restore keeps the original file under the data directory's `recovery` folder, outside automatic backup retention. The screen then asks you to close it and start AppSwitcher when ready; the restored startup and kiosk settings apply on that next launch. No automatic restart occurs during recovery.

Normal backup restoration in System shows the same configuration differences as import, including launch arguments, schedules, permissions and layouts. Argument values and PIN hashes are omitted from the comparison text. Current PINs remain in effect for a restore from an authenticated settings session. A backup changed after preview is rejected. A lost administrator session cannot authorize the restore. Backups use unique names, including multiple saves in the same second.

Startup recovery checks authenticate against the selected backup, because the current file may be unreadable. Application PINs control the AppSwitcher interface; they do not protect files from someone who can already modify that Windows user's data directory. Recovery copies can contain URLs, paths and PIN hashes: keep them within the user's existing Windows access controls and review them before sharing.

PIN verification rejects malformed or unreasonably expensive stored hash parameters. After three incorrect attempts, retries wait 2 seconds, increasing to at most 30 seconds. A successful login resets the delay. PIN-setting/removal dialogs close on session expiry without applying changes.

These features extend the existing maintenance sections; no new permission role or background network service is introduced. The application remains WPF/.NET 10. Config version 11 preserves older settings, including managed website identities. Readiness checks and the global shortcut are off by default.

## Normal closes, restart and repeated launches

Closing a native application normally now shows **Stopped**, without increasing the crash counter or scheduling watchdog recovery. AppSwitcher retains handles for observed processes and reads their exit codes: zero is normal, a nonzero result is an unexpected exit, and unavailable evidence is treated as an unconfirmed stop. This follows the Windows exit-code convention; an application can define its own nonzero success codes, and a process killed with exit code zero cannot be distinguished from a successful close. A confirmed closed managed browser tab also becomes Stopped; an unavailable browser connection remains Recovering.

**System → Launcher → Restart AppSwitcher** saves valid pending edits and restarts only AppSwitcher. Managed applications keep running, and applications already closed remain closed, including entries configured for autostart. The replacement acknowledges startup and waits for the previous process to finish before acquiring the instance lock. Normal Exit keeps its existing managed-application shutdown behavior. Restart requires administrator access and is disabled while inputs are invalid or a save is running.

Starting AppSwitcher again restores and activates its existing settings/dialog window, or opens the launcher menu when no secondary window is open. It does not create another runtime. Activation uses a local pipe restricted to the same Windows user, session and elevation level; it does not bypass authentication or Windows foreground restrictions.

## Settings search, undo and controls

The settings sidebar search accepts English/German labels and common terms such as `watchdog`, `bildschirm`, `theme` and `firefox`. **Ctrl+F** focuses search; Enter opens the first match and Escape clears the query. Selecting a result opens the relevant section/tab and focuses its field where available. Administrator-only results are hidden from operators.

**Undo** restores pending configuration edits without stopping or launching applications. History is limited to 30 snapshots; a short typing or slider-drag burst is one edit. Saving or closing the editor clears that history. Invalid field input must be corrected before undo is available. The existing Save button still disappears when there are no pending changes.

Checkboxes, slider tracks/thumbs, dropdowns and context menus use the application theme, including disabled, checked, hover and focus states. Standard WPF control behavior and accessibility actions are retained. The floating launcher keeps its compact borderless presentation.

## Browser connection checks

The administrator **Browser connections** section lists Chrome, Edge and Firefox, configured/connected profiles, native-host status, extension versions and managed-website checks. **Set up / configure** opens the relevant website settings or setup instructions. It refreshes on opening or explicitly on request, with at most four profile checks in parallel; it introduces no background polling and never creates a tab during diagnostics.

In **Applications → General**, a website entry has **Check connection** and **Test switching to existing tab**. The check examines the native-host registration/files, selected profile connection and, when already owned, the tab identity. It does not reserve ownership or create a tab. The switch test restores and activates an existing owned tab, using the same identity validation as normal operation. Missing connections and ambiguous identity remain errors; they never cause a URL fallback. The expandable setup instructions and [browser guide](BROWSER_INTEGRATION.md) explain installation, profile pairing and session restore. The package includes the signed Firefox 1.1.0 extension; changed extension versions require new signing and validation.

## Display identity and layout recovery

Display choices show the Windows-provided name, resolution and primary-display marker. Some drivers report a generic name; the saved identity is still distinct. New explicit launcher/application monitor selections and newly captured layouts store the Windows monitor-device interface alongside the legacy monitor number. Reordering monitors therefore keeps the same identified display. Older configurations without an identity continue to use their saved index; select the intended display again and save to adopt identity-based selection.

If the saved display is absent or its identity is ambiguous, placement falls back to the primary display. The editor shows an unavailable selection and retains the saved identity, so reconnecting it can recover the original choice. Reopen the application/system settings section to refresh the available display list. Explicitly selecting another display replaces the saved identity; choosing automatic/no forced display clears it. Window policies detect changed target bounds, while saved layouts clamp windows inside the available work area. Native layout capture and restoration now consistently use native pixels; only launcher positioning converts to WPF coordinates.

Windows device-interface identity is not an immutable hardware serial number: a different connector, docking station, driver or remote session may expose a new identity. Review placement after those changes. Mirrored displays can share one desktop view and are not assigned an invented unique panel identity. See Microsoft's [monitor-device enumeration documentation](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-enumdisplaydevicesw). Physical hot-plug, mixed-DPI and customer-screen qualification remain workstation checks.

## Setup checklist and attention filter

New configurations open the setup checklist after the existing administrator PIN flow. Existing users can open **Settings → Setup checklist** at any time. Its six steps link to the normal editors; the checklist does not turn on kiosk mode, launch applications or mark a workstation qualified. “Settings present” is not a successful connection test. Review applications, access/session timeout, optional browser pairing/workspaces, recovery copies and daily operation. Operator sessions do not expose setup or privileged destinations.

In **Monitoring**, **Needs attention only** shows crashes, restarting/recovering or unhealthy entries, unresponsive running applications and outstanding watchdog acknowledgements. It updates with live state and preserves the operator's allowed scope. Deliberately stopped applications remain available with the filter off. Maintenance controls remain visible in either view.

The browser connection overview now changes its recovery hint with the diagnosed fault. In particular, ambiguous tabs, duplicated profiles and an unreadable ownership journal require restoring the original identity or a known-good backup; repeatedly launching or creating another website entry is not a recovery step.

## Accessibility

AppSwitcher follows Windows high-contrast mode at startup and when the mode changes. Windows system colors replace the normal theme; the launcher becomes opaque with a visible outline and its decorative glow is hidden. The user's selected colors, opacity and glow remain saved and return when high contrast is turned off. List and menu selections have visible outlines, so selection is not communicated through fill color alone. System colors are [dynamic WPF resources](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/graphics-multimedia/how-to-paint-an-area-with-a-system-brush), allowing the application to follow the user's palette.

Use Tab/Shift+Tab to move through controls, Space/Enter for actions and checkboxes, and Escape to leave popups/dialogs. PIN input supports digits and Backspace without exposing the entered digits in its display. Settings search opens and focuses its target control. Native UI checks verify accessible names/control patterns, keyboard operations and 44-pixel action targets in both languages and compact layouts.

Before deploying to an operator panel, test keyboard operation and visible feedback: launcher discovery, search and selection, PIN errors, save/restore confirmation, session expiry, scrolling, and all kiosk exit routes. Repeat with the intended gloves and panel scaling. The target workstations have no audio output; Narrator speech is optional follow-up work, not a release gate. Automated UI Automation checks and scaled renders do not certify physical touch accuracy or machine operation.

## Timed maintenance

An administrator can select **Pause for 30 minutes** in **Monitoring**, or resume early. The countdown also appears in the launcher. This pauses automatic startup, watchdog recovery and scheduled launch/restart/shutdown. Manual actions, software health monitoring and kiosk enforcement continue.

The expiry and recent pause intervals are atomically persisted in `%LOCALAPPDATA%\AppSwitcher\maintenance.json`. AppSwitcher restarts preserve the remaining time. A scheduled action due during any retained pause interval is skipped, including after a restart or early resume; it is not replayed on resume. Autostart skipped during a pause is not replayed either. In-flight automatic operations receive cancellation; an OS/browser action already issued cannot be undone. A failed save is reported instead of claiming the pause was saved. An unreadable pause file produces a visible, bounded 30-minute fallback pause.

While AppSwitcher stays running, an unexpected stop observed during the pause is held for recovery when it expires. Existing watchdog acknowledgement and restart limits remain authoritative. An intentional stop through AppSwitcher retains its expected-stop behavior. An application that was already stopped before monitoring began is not assumed to have crashed.

## Readiness and workspaces

In **Applications → Startup**, select process/managed-tab presence, native window availability, or HTTP health success. HTTP uses the existing health URL in Recovery and accepts 2xx response headers without downloading the body. Readiness has a 1–600 second timeout, defaults to 30, and applies to already-running dependencies as well as new starts. A timeout blocks dependent launches without killing the process that did start. Existing launch delays and hooks remain available. The launcher appears before autostart readiness waits begin.

In **Layouts**, saving records the currently running applications, native window positions and an optional group. **Start workspace** resolves all members and dependencies before starting anything, reuses running applications, waits for readiness, then waits up to 30 seconds for saved native windows and restores their positions. Application IDs survive renames; old layouts still resolve names. Group membership is evaluated when starting. Missing applications/groups and dependency cycles fail visibly. Operator scope is checked for the entire plan and again during execution. A partial startup failure leaves successfully started applications running. Browser windows retain the existing restriction on native window-placement policies.

## Keyboard access and support reports

The launcher filter matches application and localized group names without changing the configured order or filtering the monitoring dashboard. Enter opens the first visible match; Escape clears the filter, then closes the menu. **System → Ctrl+Alt+Space** optionally registers a Windows hotkey to open the launcher and focus its filter. Conflicts are reported and the normal launcher button remains usable. The hotkey does not change authentication or operator scope and is unregistered when disabled or on shutdown.

Administrators can open **System → Preview support report**, review the exact JSON and save it locally. The report includes runtime versions, connected browser extension versions, process metrics, validation counts, application fault counters and a bounded list of structured audit event summaries. It uses report-local App/Profile labels. Names, identities, URLs, paths, arguments, credentials, raw logs and free-form event details are excluded by construction. There is no upload or automatic transmission. Session expiry closes the privileged preview and the save action rechecks administrator access.

## Performance behavior

- Native title matching first selects the executable name instead of scanning every process. Monitoring shares candidate discovery for one second, subscribes to process exits and periodically reconciles. PID birth times, executable paths and titles are revalidated. Control actions use fresh discovery; cached absence cannot authorize a new launch.
- Responsiveness probes run separately from process-presence/recovery checks, with at most four probes at once and no overlapping probe for the same target. Hidden telemetry probes run every five seconds; visible telemetry can refresh each second. Shutdown cancels queued probes and drains current probes.
- Resource telemetry runs every five seconds when visible or when memory thresholds need checking, otherwise every 30 seconds. Kiosk enforcement and window policy deadlines are unchanged. HTTP health checks are bounded to four concurrent requests, dispose responses and do not buffer response bodies; timeouts mark health unhealthy.
- The browser extension captures tabs once per snapshot and reads each Firefox session identity once, with eight concurrent identity reads at most. Events are coalesced, irrelevant title/favicon changes are ignored, disconnected snapshots are skipped, and a five-second reconciliation remains. Create/close/activation commands capture fresh state. Identity journals and duplicate-prevention rules are unchanged.

## Validation and measurements

Run:

```powershell
dotnet test AppSwitcher.Tests/AppSwitcher.Tests.csproj
node --test browser-extension/tests/core.test.mjs
dotnet run --project AppSwitcher.UiChecks/AppSwitcher.UiChecks.csproj
dotnet run --project AppSwitcher.UiChecks/AppSwitcher.UiChecks.csproj -- --benchmark
dotnet run --project AppSwitcher.UiChecks/AppSwitcher.UiChecks.csproj -- --process-checks
pwsh -NoProfile -File scripts/measure-performance.ps1 -TargetProcessId <AppSwitcher-PID>
pwsh -NoProfile -File scripts/build-browser-package.ps1
```

The isolated benchmark writes `artifacts/performance.json`. On the development machine (.NET 10.0.14, 22 logical processors), five warmed trials of 50 native title-filter lookups had a median of **766 ms before vs 5.6 ms after** the discovery change. Allocations over those trials were about **172 MB vs 0.36 MB**. Twelve simulated 100-ms responsiveness probes queued in 3.5 ms and completed in 328 ms with four workers. The extension regression workload (20 managed sites, 120 tabs) verifies **one tab query instead of 20** per snapshot and **120 instead of 2,400 Firefox identity reads**.

Release validation on 2026-11-02 passed 145 .NET tests, 31 extension tests and 1,420 native UI assertions with no binding errors. The native UI matrix covers English/German, light/dark, compact layouts and 150% rendering. Real isolated Chrome and Edge profiles each passed 20 minimized activations, original document/unsaved-state preservation and native-host reconnection; Firefox Developer Edition 160 passed 20 activations, persistent browser restart, session-tag identity and unrelated-tab preservation. Firefox lint reported no errors, notices or warnings. The release package includes the self-contained app/host and both extension builds. Production configuration and browser profiles were not changed by these tests.

Those discovery/probe measurements describe bounded workloads, rather than a whole-application speedup. Real target CPU, total process-tree memory, startup and switching latency depend on the target machine and managed applications. No Rust comparison was performed. The improved paths are dominated by Windows/browser discovery and waits; keeping C# avoids adding a second runtime/component boundary without a demonstrated benefit. A native-host AOT/Rust comparison remains an optional future experiment.

Primary API references: [Windows hotkey registration](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey), [process exit events](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process.enableraisingevents), [HTTP headers-only completion](https://learn.microsoft.com/en-us/dotnet/api/system.net.http.httpcompletionoption).

## Desktop measurements and reliability checks — 2026-11-02

Measured the self-contained Release app on this Windows workstation with .NET 10.0.14 and 22 logical processors. The existing user configuration was retained byte-for-byte. Startup was timed from process creation until a visible native window, with 25-ms polling. After a 10-second settling period, 61 samples were collected at roughly one-second intervals with no automated interaction.

| Measurement | Result | Scope |
| --- | --- | --- |
| Startup to visible launcher | 1.44 s | One local launch; not a reboot/cold-cache test |
| CPU during 60.9 s sampling | 0.87% of one core / 0.040% of the whole machine | AppSwitcher process only |
| Median private memory | 162.8 MiB | Excludes managed applications/browser hosts |
| Median working set | 224.0 MiB | Includes resident shared pages |
| Repeated launch → existing settings restored | 336.9 ms median | Five isolated Release-host trials; all restored, foreground not confirmed in that run |
| Existing minimized native window restored/activated | 19.0 ms median, 30.2 ms p95 | 30 trials; all 30 confirmed foreground activation |

The original production session sampled before these changes used about 0.76% of one core (0.034% total CPU), 293.5 MiB private memory and 419.6 MiB working set. That older session and the fresh launch differ in age and prior UI usage, so this is **not a controlled memory-reduction or CPU-speedup claim**. During the new sample, private memory fluctuated around 159–165 MiB and ended at 161.9 MiB; handles changed from 650 to 659. A one-minute sample does not establish long-term leak behavior.

The native activation fixture uses real isolated WPF processes with unique window titles, not a browser tab or an industrial visualization. Windows foreground permission can be denied to a background test driver or revoked by user input elsewhere; the runner reports restoration and successful foreground activation separately. An earlier run confirmed foreground activation for all five repeated launches (331.5 ms median); the final lifecycle run restored the same settings window five times but did not confirm foreground activation. Browser tab latency was not remeasured in this pass. The production executable's startup measurement is separate from the test host's startup time.

The current release passed **163 .NET tests**, **31 extension tests** and **2,298 native UI assertions** with no WPF binding warnings/errors. Full-process checks additionally confirmed: a normal zero-code exit remains Stopped without watchdog restart/crash count; a nonzero exit still triggers failure handling; five launches reuse the same settings handle; restart preserves the running application's PID and does not relaunch a stopped autostart entry; detach shutdown preserves the managed process; and 30 native switches reuse the existing window. Test configurations and fixture processes are isolated from production.

Raw local receipts are `artifacts/performance-before.json`, `artifacts/performance-after.json`, `artifacts/process-performance.json`, and the UI/test outputs. `scripts/measure-performance.ps1 -TargetProcessId <PID>` samples an existing app. Its alternative `-ExecutablePath <path>` mode starts the app and measures startup, and requires AppSwitcher to be stopped first; normal configuration startup behavior applies. `--process-checks` runs the isolated lifecycle/activation workload.

Exit/activation references: [Process.ExitCode convention](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process.exitcode?view=net-10.0), [same-user pipe restrictions](https://learn.microsoft.com/en-us/dotnet/api/system.io.pipes.pipeoptions?view=net-10.0), [Windows foreground permission](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-allowsetforegroundwindow).

## Memory and startup optimization — 2026-11-02

Profiling the previous published process found about **4.1 MiB of live managed objects** and **18.8 MiB of GC-committed memory**, while the process had more than 90 threads and loaded the Intel graphics stack. Controlled rendering comparisons identified hardware-device initialization and its native resources as a large part of the overhead.

AppSwitcher now selects WPF software rendering before creating its windows. Its compact 2D launcher and settings retain the same controls, transparency, effects and input behavior. This preference affects AppSwitcher only; it does not change rendering in managed applications, browsers, or Windows. Reapplying the current theme/language reuses the existing resource dictionaries. RID-specific Release publishes use **ReadyToRun**, which precompiles code to reduce startup JIT work; no trimming, GC forcing, working-set trimming, memory limits or monitoring slowdowns were introduced. Microsoft documents the [process rendering preference](https://learn.microsoft.com/en-us/dotnet/api/system.windows.media.renderoptions.processrendermode?view=windowsdesktop-10.0) and [ReadyToRun's startup/size tradeoff](https://learn.microsoft.com/en-us/dotnet/core/deploying/ready-to-run).

The final published executable was measured with the **same saved production configuration**, a 10-second settling period and 61 samples over 60.9 seconds. No other application was stopped, and the configuration's SHA-256 remained unchanged.

| Production measurement | Previous published app | Optimized app |
| --- | --- | --- |
| Median private memory | 162.8 MiB | **61.0 MiB** (63% lower) |
| Median working set | 224.0 MiB | **120.4 MiB** |
| Startup to visible launcher | 1.44 s | **1.04 s** (28% faster in these launches) |
| CPU, percentage of one core | 0.87% | **0.69%** |
| CPU, percentage of all 22 logical processors | 0.040% | **0.031%** |

The production startup figures are one launch each, not cold-boot guarantees. To check that the result was not just a fresh-process effect, the isolated harness alternated hardware and software rendering for three launches each using the same precompiled build and synthetic three-app configuration. Hardware/software medians were **169.9/72.9 MiB private memory**, **227.4/127.0 MiB working set**, **100/25 threads**, and **1.434/0.905 s to the first rendered frame**. These test-host values are distinct from the production executable values above. A separate precompilation comparison measured 918/892 ms median startup without/with ReadyToRun; most of the gain came from rendering, not precompilation.

A continuous-scroll workload made 120 top/bottom scroll changes in the 720×640 settings window. Hardware/software layout p95 was **0.492/0.502 ms** and process CPU was **91.8/84.9% of one core** during that deliberately intensive workload. Open-editor private memory was **212.1/88.6 MiB**. This measures layout work and process CPU, not end-to-end frame latency; it is not a physical touch or 4K-display qualification. Rendering costs depend on the processor, display and graphics driver. A hardware fallback remains available: add `"AppSwitcher.UseHardwareRendering": true` inside `runtimeOptions.configProperties` in `AppSwitcher.runtimeconfig.json`, then restart AppSwitcher. Default is software rendering; no Windows registry setting is changed.

Validation passed **163 .NET tests** and **2,300 native UI assertions**, covering both themes/languages, compact/scaled layouts, custom controls, search, undo, saving and session expiry, with zero WPF binding errors. The process suite again verified normal-close classification, confirmed-failure recovery, repeated launches, restart preserving the managed PID and 30 switches to the existing native window. Native activation was 11.7 ms median/13.9 ms p95 in that fixture; browser-tab latency was not remeasured. Browser control, recovery rules, scheduler intervals and kiosk enforcement remain unchanged. Benchmark browser pipes are isolated from the production bridge.

Local receipts: `artifacts/runtime-memory-before.json`, `artifacts/startup-memory/20261102-101456/results.json` (precompilation), `artifacts/startup-memory/20261102-101651/results.json` (rendering/scrolling), `artifacts/performance-memory-after.json`, `artifacts/memory-unit-tests.txt`, `artifacts/memory-process-checks.txt`, and `artifacts/ui-checks/memory-ui-checks.txt`.

To repeat the isolated rendering experiment:

```powershell
dotnet publish AppSwitcher.UiChecks/AppSwitcher.UiChecks.csproj -c Release -p:RuntimeIdentifier=win-x64 -p:SelfContained=true -p:IsTestProject=false -p:PublishReadyToRun=true -o artifacts/perfhost
artifacts/perfhost/AppSwitcher.UiChecks.exe --startup-memory --exercise
```

The test-only `IsTestProject=false` publish property keeps runtime identifiers consistent through the harness's test-project reference. Normal unit tests still use `dotnet test` as above. The experiment writes a fresh results directory, uses separate configuration/instance/browser-bridge scopes, starts no configured applications and cleans up its own hosts. For comparing two published hosts, append `--compare-hosts <baseline-exe> <candidate-exe>`; that comparison uses software rendering for both.

## Candidate settings and history behavior

Settings preview live. Save persists; Close offers discard; session expiry discards pending edits. Background pinning/position updates do not persist the editor's unfinished configuration. Empty restricted operator scope grants no apps; select **Allow all applications** explicitly for unrestricted operator management. Rollback from schema 12 requires a pre-upgrade configuration backup as well as the older binary.

Lifecycle scripts should wait for their child work before returning. Cancelling a running hook stops its process tree; a service intentionally detached by an already completed script is outside that lifetime. Audit and crash CSVs rotate at 16 MiB and retain five numbered archives. Export older history before rotation if longer retention is required. See the [release resolution](RELEASE_RESOLUTION_2026-11-03.md) for precise limits and outstanding deployment qualification.
