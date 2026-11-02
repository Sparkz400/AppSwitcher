# Useful additions and a quieter interface

Reviewed 2026-11-02 against the existing WPF screens, the product's operator/maintenance workflows, and the sources below. These are design recommendations and native UI checks, not findings from a user study.

## Decision

Improve setting up, finding and recovering an application before adding more destinations to the sidebar. AppSwitcher already has groups, favorites, workspaces, schedules, recovery, access control and diagnostics. A second implementation of those features would make the problem worse.

| Priority | Addition | Why it earns its place | Scope and acceptance |
| --- | --- | --- | --- |
| Delivered | Live launcher preview | Shows the result of shape, icon, size, color and opacity choices without explanatory prose. | Shares the actual launcher drawing and settings bindings; remains visible while its controls scroll. The preview scales down to fit and is not a draggable launcher. No timer, process or new package. |
| Delivered | Search that opens secondary settings | Collapsing rarely used options must not make them difficult to find. | Search reveals schedules, dependencies, readiness, checks, scripts, connection overrides and support actions, then focuses the control. It never executes an action. |
| Delivered | Browser state and a relevant next action | Repeated installation paragraphs obscured whether a browser was ready or needed attention. | Short normal/offline states, setup/profile/website actions, details on demand, and concrete fault messages still visible. A refresh preserves the saved profile identity, including offline. No automatic tab creation or ownership reassignment. |
| Delivered | Add an app from an open window | Avoids locating executables during initial setup. | Add app → Add from an open window; searchable titles, icon where available, editable name and executable for review. Process/window identity is revalidated before accepting. Existing entries are reused and browsers go through website setup. Scans only on open/Refresh. [Implementation and validation](OPEN_WINDOW_PICKER.md). |
| Later, only if operators need it | “Needs attention” filter in Monitoring | Helps larger installations find faults among many healthy entries. | Keep the normal complete overview as the default; show the active filter clearly; never filter away watchdog acknowledgements or a maintenance pause indicator. Reuse existing state, without another monitor. |

Do not add a file indexer, calculator, cloud account/sync or plugin marketplace for this workflow. Their utility has not been established for operators, and each introduces additional navigation, maintenance or background work. No language rewrite is needed for these UI changes.

## Interface changes

- Keep common app choices visible. Name/type and browser/profile share a row where space permits and stack on small windows.
- Put optional schedules, startup ordering, health checks, working folders, connection overrides and scripts in named, keyboard/touch-accessible disclosures. Configured per-app options open automatically when the app is selected; presentation state does not enter the saved configuration.
- Keep start, recovery and confirmation switches, validation errors, active maintenance state, security impact and concrete fault/recovery information visible. Detailed kiosk behavior remains available beside its switch. The health view still distinguishes software status from machine safety.
- Replace repeated paragraphs with short labels and relevant help. Retain explicit labels on actions; an unlabeled icon is not a substitute for clarity, particularly on a touch panel.
- Keep CPU/RAM and current application state in monitoring cards. Historical counters remain under Details. Show only the currently available pause/resume action.
- Remove redundant shell headings and empty status gaps. Shorten English and German copy, including save state and button labels. Preserve immediate application of edits, save-on-close behavior, undo and validation.
- Relabel dropdown choices in place when the language changes. Rebuilding bound lists could reset monitor selections to -1; the language round-trip now preserves every configuration value and keeps offline profile labels localized.
- Use the same themed disclosure controls everywhere, with 44-pixel header targets and visible keyboard focus. Read-only users may reveal permitted settings without gaining editing rights.

## Evidence behind the choices

- Microsoft recommends making critical functions discoverable and using contextual help to support usability rather than compensate for it: [Guidelines for app help](https://learn.microsoft.com/en-us/windows/apps/design/in-app-help/guidelines-for-app-help).
- Microsoft's expander guidance fits related secondary content while keeping primary content visible: [Expander](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/expander). The implementation here uses native WPF controls, not a WinUI dependency.
- Nielsen Norman Group describes exposing the main choices first and revealing less-used complexity when needed: [Progressive disclosure](https://www.nngroup.com/articles/progressive-disclosure/). Short, descriptive disclosure names and search that opens the target are necessary safeguards against hiding settings.
- Icons often need labels to communicate reliably: [Icon usability](https://www.nngroup.com/articles/icon-usability/). The design therefore reduces repetition rather than turning the interface into an icon-only toolbar.
- Choosing an existing window follows recognition rather than remembering file paths: [Recognition and recall](https://www.nngroup.com/articles/recognition-and-recall/). Microsoft's [PowerToys Workspaces](https://learn.microsoft.com/en-us/windows/powertoys/workspaces) is a relevant example of capturing a running desktop and then reviewing it. This supports a setup picker, not adding another workspace feature to AppSwitcher.

## Validation

The native UI runner checks English/German and light/dark themes at 1180×800, 1024×768, 900×700 and 640×560, plus scaled operator rendering. Both closed and expanded settings receive layout/touch coverage. Interaction checks cover automatic disclosure of configured values, search/focus, editable dependency selection, hidden-field validation, preview bindings, offline profile preservation, browser conflict text, undo/save visibility and role restrictions. Captures are in `artifacts/ui-checks`; the prior screen set is retained locally in `artifacts/ux-before`.

Release validation passed 163 unit/integration tests and 3,473 native UI assertions with no WPF binding warnings. Isolated process checks also passed: normal exit versus failed exit/recovery, repeated second launches, restart preserving live processes and stopped autostart entries, detach, and 30 restores of the same existing window. Those process/window tests use a synthetic native application, not a live browser or industrial visualization. Windows did not grant foreground activation to those background-console requests in this run, so it does not establish foreground-switch success. The production configuration hash remained unchanged.

The published production build was measured for 60 seconds after a 10-second settle, with settings closed and the existing configuration unchanged: median private memory **59.3 MiB**, working set **115.7 MiB**, CPU **1.87% of one core / 0.085% of this 22-logical-processor machine**, and **1,045 ms** from launch to a visible window. These are one local run, not a cold-boot benchmark or an estimate for every installation; browser/native-host and managed-app processes are excluded. The previous optimized run was 61.0 MiB private and 1,037 ms to visible. No further memory-saving mechanism was added in this UI change. Raw samples are retained in `artifacts/ux-work/production-final-performance.json`.

Follow-up usability testing should ask an operator to add an app, connect a website, find a daily restart, identify a fault and change the launcher without coaching. Observe completion, misclicks and help openings before deciding whether to add further options. No task-time or usability-score improvement is claimed without that testing.
