# Release readiness audit — 3 November 2026

AppSwitcher has a strong beta test and packaging foundation, but it is **not ready for a stable release yet**. Targeted transition tests found defects that the existing suite misses, particularly in session expiry, operator scopes, window placement and script cancellation. Fix those before adding substantial features. The actionable checklist is in [RELEASE_TODO.md](RELEASE_TODO.md).

Reviewed source: `4519900aba8b31b2347baa11b01e55ff567598d8`, clean `main` before this report. Product version: `1.2.0-beta.3`. “1.0” below means the first stable release goal; the actual version still needs a decision.

## Evidence and limits

| Check run on this revision | Result |
| --- | --- |
| Locked restore and Release solution build | Passed; zero build warnings/errors |
| Existing .NET tests | 253 passed; zero skipped/failed |
| Existing Node extension/broker tests | 32 passed |
| Native WPF checks | 9,955 assertions; no binding warnings/errors |
| Native process lifecycle | Normal exit remains stopped; failed exit recovers; repeated invocation reuses settings; restart preserves managed PID; detach preserves managed app |
| Native switching | 30/30 synthetic-window restores and foreground confirmations; median restore 15.9 ms, p95 25.4 ms |
| Startup recovery | Authenticated backup, explicit confirmation, original preserved, no launcher before recovery |
| Complete offline package | Built successfully with required signed Firefox payload |
| Package setup checks | 15 assertions passed |
| Installer | 23 checks passed: fresh install, blocked running/locked-host updates, upgrade, rollback, uninstall and data preservation |
| Release integrity | Manifest, hashes and SPDX inventory verified for the reviewed commit; 13 notice dependencies and 19 SBOM packages are different inventory scopes |
| Additional property checks | All 26 `AppSettings` and 45 persisted `AppItem` properties retained assigned values through JSON serialization/deserialization |
| Additional behavior probes | Reproduced the defects below; these observations are **not** passing regression tests |

The UI harness navigated sections/tabs/disclosures in English/German, dark/light themes, compact sizes and simulated high contrast. It exercised custom title-bar controls, scrollbars, validation, Save/Undo, search, dialogs and accessibility patterns. Visual inspection covered all main sections, all application/system/access tabs, compact website editing, browser setup, window picker, launcher, PIN, support and configuration recovery. This is not a claim that every combination of settings or every third-party program was exercised.

The settings matrix below combines runtime/source tracing, the existing tests, additional probes and visual inspection. A serialization pass proves persistence, **not** the setting's effect. Physical touch/gloves, real mixed-DPI docking, OS kiosk enforcement/escape, actual Narrator speech, power interruption and a full shift remain open. Actual installed Chrome/Edge/Firefox restart flows were not rerun in this audit; earlier browser evidence is recorded in [VALIDATION.md](VALIDATION.md), and does not close those acceptance gaps.

The production launcher was briefly stopped for exclusive lifecycle checks and restarted from the published build at this commit. The production configuration hash remained unchanged. Managed user applications were not stopped. Kiosk policies, browser profiles and production browser registrations were not changed. Installer checks used their own product identity and installation directory.

Local raw evidence is under `artifacts/release-audit-20261103/`: `verify.log`, `probes.log`, `observations.json`, `settings-roundtrips.json`, `production-idle.json`, `release-build.log`, and `release-check.log`. The isolated probe source is in its `probes/` subdirectory. Run it with `dotnet run --project artifacts/release-audit-20261103/probes/Probes.csproj -c Release`. Rendered captures are in `artifacts/ui-checks/`; installer results are in `artifacts/test-results/installer/results.json`. These ignored local artifacts are not a public release or committed regression suite. Audit screenshots can contain the local Windows actor name; sanitize before publication.

## Confirmed defects

P1 means high-priority correctness work before stable. P2 means a reproducible defect to fix or explicitly resolve before stable, with lower immediate impact. References are to the reviewed revision.

| ID | Priority | Reproduction and observed behavior | Source and required result |
| --- | --- | --- | --- |
| B01 | P1 | Set session timeout to 5, then 0, then 5. The displayed value is 5 but the timer is inactive. Separately, set 5, change to 10, Undo: display returns to 5 while timer stays at 10. Applying configuration with 2 also leaves it at 10. | `Services/SessionTimeoutService.cs:21,63`; `ViewModels/MainViewModel.Editor.cs:36`; `ViewModels/MainViewModel.cs:848,1469`. Disabling must update remembered state; undo/import/reload must reconcile the actual session timer. Test expiry, not only labels. |
| B02 | P1 | Allow an operator only app A. In the editor clear A's name, then type a replacement. The allowed-name list becomes empty and the operator can now access both A and previously restricted B. | `Models/Models.cs:73,96`; `ViewModels/SettingsViewModel.cs:311`; `ViewModels/MainViewModel.cs:728`. Intermediate text editing must preserve scope. Use stable IDs and an explicit all-apps mode; deletion of the last allowed app/group must not silently grant wider management access. This concerns AppSwitcher's role controls, not an OS security boundary. |
| B03 | P1 | Apply maximize placement with continuous reapplication **off**; manually minimize the window; run another enforcement pass. It is restored anyway. | `Services/WindowPolicyEnforcementService.cs:181` restores before evaluating the continuous-placement condition at line 204. Preserve manual minimization unless a deliberate activation or applicable continuous policy requests restoration. The probe used a fake window platform. |
| B04 | P1 | Start an owned PowerShell pre-launch hook that waits; cancel its lifecycle token. The operation cancels but the child is still running. The audit explicitly terminated its own test child afterward. | `Services/AppLifecycleService.cs:117,134,148`. Timeout kills the child, caller cancellation only disposes the process handle. Cancel and reap the owned process tree under an explicit hook contract; test maintenance pause/shutdown during every hook stage. |
| B05 | P2 | Track an operation that fails synchronously, then retry the same key. Two requests execute the delegate only once. | `Services/SchedulerService.cs:240,244,263`. `GetOrAdd` runs the async factory before insertion; synchronous completion removes nothing and the completed task is inserted afterward. Register ownership before execution and remove that exact entry on completion; test immediate success/failure/cancellation and delayed completion. |
| B06 | P2 | Configure Client → Server dependency, then rename Server. Client still references the old name and configuration validation blocks saving. | `ViewModels/SettingsViewModel.cs:311`; `Services/AppDependencyPlanner.cs`. Preserve references across rename. Prefer dependency IDs with migration; at minimum update dependents atomically and show deletion impact before removing a referenced app. |
| B07 | P2 | A running app is unhealthy from its HTTP check. Clear the health URL and run process monitoring again. It remains `Unhealthy`, with old health text and `NeedsAttention=true`. | `Services/HealthCheckService.cs:40`; `ViewModels/MainViewModel.cs:502,1353`. Clearing/replacing a check must reset obsolete health state; reject late results belonging to the old URL/target. |
| B08 | P2 | Website entries expose and accept a positive memory-alert threshold, but resource monitoring skips websites entirely. Native launch arguments are also exposed in the common Advanced tab although website startup follows a separate path. | `Controls/AppSettingsPanel.xaml:202`; `Services/ResourceMonitorService.cs:69`; `Services/ProcessHelper.cs:106,152`. Hide or disable unsupported website controls and migrate/validate stale values. Do not invent a per-tab memory number from browser-wide process memory. Memory acceptance was probed; the arguments mismatch was traced in UI/runtime source. |
| B09 | P2 | Changing launcher border color or enabling glow produced identical rendered pixels. A background-color change did change pixels, validating the probe. At the slider minimum of 40, the visual footprint is 44×44. | `Controls/LauncherVisual.xaml:13,28`; `Controls/GlobalSettingsPanel.xaml:68,94`. Border color has no normal-mode stroke consumer, and the glow effect is attached to transparent geometry. Keep the requested borderless launcher: remove the dead border control. Either implement/test glow within the intended footprint or retire it. Align size limits and labels. |
| B10 | P2 | Select “Icon only”: the name remains visible, merely reduced to 12-point text. | `MainWindow.xaml:37`. Make the mode match its label, retaining tooltip/UIA names; alternatively rename the option to describe its actual behavior. Text-only does hide the icon. |
| B11 | P2 | The validator reports zero errors for negative button/menu dimensions, opacity 2 and unknown shape/theme/language strings. | `Services/ConfigValidationService.cs` global validation; `Services/ConfigService.cs` import/load validation path. Validate persisted UI ranges and enumerated strings before application; define migration for old defaults. This probe establishes missing validation, not a reproduced crash. |
| B12 | P2 | Audit detail `First line\nSecond line` reads back only `First line`. Reading the latest 200 events from a synthetic 100,000-row, 18.5 MiB history allocates 42.0 MiB. | `Services/AuditLogService.cs:101,104,138`. Writing supports quoted newlines but reading splits physical lines. Use a correct record format/parser and bounded recent-event reads/rotation. Retain required history without loading it all on the UI thread. |

## Complete persisted-settings inventory

All 71 top-level properties below passed the additional JSON round trip. “Covered” means relevant existing tests and source paths were reviewed; it does not mean all inputs and external environments passed an end-to-end test.

### Global settings — 26 properties

| Properties | UI / consumer | Audit outcome |
| --- | --- | --- |
| `ButtonSize`, `Shape`, `IconStyle` | System → launcher; `LauncherVisual`, shape/icon converters | Shape/icon presentation covered; size minimum disagrees with rendered footprint, B09. Imported values need B11. |
| `BgColor`, `FgColor` | Launcher color pickers → shape/icon | Live binding works; background pixel-change control passed. High contrast intentionally overrides custom colors. |
| `BorderColor`, `GlowEnabled` | Launcher appearance → visual | Both controls currently have no rendered effect, B09. |
| `ButtonOpacity` | Launcher opacity | Bound live, overridden to opaque in high contrast; range validation missing for imported config, B11. |
| `MenuWidth`, `MenuItemHeight` | Launcher popup/row dimensions | Persisted configuration fields, no current settings controls. Width is clamped by 360–600 UI limits while model default is 220. Decide supported sizing contract and migrate defaults. |
| `AutoClose` | Launcher behavior → launch command | Native launches respect it. Website switching closes the popup regardless so browser activation can take focus; make the exception discoverable. |
| `PosX`, `PosY`, `PositionLocked` | Launcher drag/save/positioning | Edge positioning and footprint covered. Dragging is disabled when locked. Physical mixed-DPI movement remains acceptance work. |
| `PreferredMonitor`, `PreferredMonitorId` | Monitor picker → positioning/display identity | Reorder, unavailable display and reconnect covered with synthetic displays; saved identity retained. |
| `KioskMode` | Access → kiosk service | Fake-platform policy/activation/deactivation tests pass. Real edition-specific policy, UAC/reboot and independent escape not tested here; website+kiosk configuration is rejected. |
| `AdminPasswordHash`, `OperatorPasswordHash` | PIN dialogs/security service | Hash formats, validation, retry delay, migration and recovery authorization covered; password material not included in report. |
| `OperatorScope` (`AllowedAppNames`, `AllowedGroupKeys`) | Access → allowed apps/groups | Normal scope filtering covered; rename can accidentally remove restrictions, B02. Empty lists currently mean unrestricted. |
| `ConfigEncryption` | Access → config save/load | DPAPI save/load covered. Portable export removes PIN hashes and is plaintext; this is local protection, not a portable encrypted backup format. |
| `SessionTimeoutMinutes` | Access → session service | Normal expiry/modal closure covered; re-enable/undo/reload defects, B01. |
| `GlobalShortcutEnabled` | System → native shortcut | Ctrl+Alt+Space registration/conflict feedback and disposal traced; add an explicit occupied-hotkey and re-enable integration check. |
| `Theme`, `Language` | System → appearance/resource dictionaries | EN/DE and dark/light navigation covered. Audit action/detail strings remain technical English; localization cleanup remains. |
| `MaxConfigBackups` | System → backups/config persistence | Retention and recovery paths covered. Zero disables new automatic backups; it does not delete existing backups. Recovery copies are retained separately. |

### Per-application settings — 45 properties

| Properties | UI / consumer | Audit outcome |
| --- | --- | --- |
| `AppId`, `TargetKind` | Generated identity/type picker | Native/website routing and identity validation covered. Native-only state must be handled when converting types. |
| `WebsiteUrl`, `Browser`, `BrowserProfileId` | Website editor/pairing → bridge | URL validation, unique pairing, conflicting profiles, ownership and no URL-based duplicate fallback covered by tests. Editing a URL does not navigate an already-owned live tab; provide an explicit address-change flow if desired. |
| `BrowserProfileDirectory`, `BrowserExecutablePath` | Advanced browser startup → bootstrap | Used only when bootstrapping a stopped browser. A different running profile does not authorize creating another browser session. Actual deployed-browser restart acceptance remains open. |
| `Name` | App list/editor → labels and name references | Basic editing works; dependent/scoped rename issues B02/B06. |
| `Path`, `Args`, `WorkingDirectory` | Native application fields → process start | Native execution/activation and path matching covered; invalid paths surface warnings/errors. Website arguments mismatch is B08. |
| `WinPattern` | Native window matching | Matching tests cover exact executable identity and window filtering. Help text saying only “process name” is outdated; use a short, accurate description. |
| `BgColor`, `FgColor`, `Mode`, `CustomIconPath` | Advanced → launcher row | Background is an accent strip. Custom-icon and display bindings reviewed. `FgColor` is a persisted legacy app field without a current editor/runtime row binding; retire/migrate or define support. `IconOnly` is B10. |
| `Group`, `SortOrder`, `IsPinned` | Group selector, move/drag, favorites | Grouping, localized names, order and pin persistence covered; filters preserve order. Pinning saves configuration, so include it in the future draft-editing contract. |
| `AutoLaunch` | Startup → startup dependency plan | Startup/restart handoff tested, including leaving a manually stopped autostart app stopped across launcher restart. Real Windows boot remains acceptance work. |
| `WatchdogEnabled`, `MaxRestarts` | Recovery → watchdog/circuit store | Crash vs normal exit, bounded recovery, cooldown/acknowledgement and persistence covered. Add a concise indication of the retry-window semantics. |
| `PreLaunchHookPath`, `PreLaunchHookArgs`, `PostLaunchHookPath`, `PostLaunchHookArgs`, `PreStopHookPath`, `PreStopHookArgs`, `PostStopHookPath`, `PostStopHookArgs` | Advanced scripts → lifecycle runner | Stage ordering/errors/timeouts covered; cancellation leaves an owned child running, B04. Real user scripts are not run by this audit. |
| `DependsOn`, `LaunchDelay`, `Readiness`, `ReadinessTimeoutSeconds` | Startup → dependency/readiness services | Order, delay, timeout, cancellation and already-running dependency checks covered. Rename breaks dependencies, B06. Multi-dependency support would be a new feature. |
| `ScheduleTime`, `ScheduleRestartTime`, `ScheduleShutdownTime` | Startup → scheduler | HH:mm parsing and maintenance cancellation/skip covered. Retry defect B05; add deterministic restart/DST/conflicting-time tests and define once-per-day behavior across launcher restart. |
| `HealthCheckUrl`, `MemoryThresholdMB` | Recovery → health/resource services | HTTP state clearing defect B07; website threshold silently ignored, B08. Memory alert is an alert, not a restart trigger. Local TLS/site endpoints need deployment tests. |
| `RequireConfirmation` | Recovery → manual stop action | Manual stop confirmation and cancel covered. Scheduled stops do not prompt; make the label explicitly refer to manual termination. |
| `PlacementMode`, `ForcedMonitor`, `ForcedMonitorId`, `AlwaysOnTop`, `ReapplyPlacementContinuously` | Window placement → enforcement service | Monitor mapping/fullscreen/topmost restoration covered with fakes. Manual minimize regression B03. Website placement is rejected. |

### Other saved data and functions

| Surface/function | Reviewed behavior and remaining work |
| --- | --- |
| Groups: key and EN/DE names | Add/delete/rename labels, dirty state and operator choices covered. Group deletion affects memberships; make workspace/scope impact visible. |
| Layout name, app IDs, group keys | Capture, start workspace with dependencies, restore, replace and delete traced/tested. Missing members fail explicitly. Offer edit/repair for stale members; currently users must recapture. |
| Saved positions, monitor identity and saved monitor geometry | Capture/map/clamp/reconnect tests pass. Actual hardware scale/docking tests remain open. Websites can be workspace members but native browser-window geometry is not managed. |
| Launcher open/close, drag, filter, groups, favorites, action menus | Native UI checks pass; borderless closed footprint and no extra caption/label verified. Add a clear empty-search cue and reduce redundant launcher instructions. |
| Add manually / add from open window / legacy website conversion | Synthetic candidate selection, stale HWND/PID birth/path revalidation, duplicates, denied reads, browser routing and closing/session expiry covered. Picker does not guess a browser tab URL. |
| Save, dirty indicator, close, Undo, restart launcher | Save hides without changes; runtime metrics do not dirty config; invalid inputs block save/close. Close autosaves valid edits. Undo does not reconcile every runtime service, B01. No whole-session Discard/Redo yet. |
| Search/navigation | Search opens sections and expands/focuses tested hidden fields. Catalog is hand-maintained and does not describe every setting; add catalog-to-control tests. |
| Custom title bar / scrollbars / dialogs | Maximize/restore/minimize/close, work-area bounds, wheel/thumb/page scrolling and fixed-dialog chrome covered. Keyboard focus and UIA patterns covered; actual Narrator not established. |
| Browser setup, repair, copy path, profile selection, finish | Offline default, signed Firefox packaging, missing-file blocked state, immutable host repair, selected-browser-only registration and session cancellation covered. Finishing proves connection, not browser session-restore preference or a restart drill. |
| Browser overview, check, test switch | Diagnostic mapping and explicit refresh covered. A successful switch check is not evidence that a future browser restart will restore the session. |
| Monitoring, attention filter, acknowledge recovery | Dynamic filter/metrics/scope tested. B07 can keep a stale fault visible. Website CPU/RAM should display unavailable, not a misleading zero. |
| Maintenance pause/resume | Bounded persistence, expiry, corrupted-state fallback and skipped schedules covered. It is not a kiosk escape and does not disable monitoring; hook cancellation needs B04. |
| Import/export/backups/recovery | Validation, PIN-safe export, preview/fingerprint/confirmation, original preservation and blocked startup covered. B01/B11 affect application of imported settings. |
| Audit/support | Sensitive support fields are allowlisted/redacted and previewed locally. B12 affects history. Some real event names (`ConfigChanged`, `AppStopped`) become `Other` in support output; align the safe action vocabulary without exposing raw detail. |
| Single instance, detach/exit/restart | Process tests pass. Synthetic managed processes only; third-party save prompts and site shutdown behavior still need qualification. |
| Executable update notice | File metadata polling traced; add a synthetic replacement test and retain only currently configured executable snapshots if long-lived churn warrants it. |

## Performance findings

The running **published** launcher at this commit was sampled for 30.45 seconds after settling naturally: median private memory **76.32 MiB**, working set **151.85 MiB**, CPU **1.64% of one core / 0.075% of this 22-logical-processor machine**, handles **679 → 677**. These measurements exclude managed applications and browser/native-host processes. They do not establish cold startup or leak freedom.

The isolated lifecycle test host became visible/ready in **1,113.9 ms**. That host includes test dependencies and is not a production cold-start benchmark. Repeated invocations took 368–459 ms. Do not compare those numbers directly to a Windows reboot startup.

Prioritize bounded audit-log reads (B12), then measure large configurations and repeated editor open/close. Dirty tracking serializes configuration snapshots multiple times per edit; a 100/500-app typing/slider benchmark should determine whether incremental tracking/debouncing is needed. Continue avoiding idle polling where existing events suffice. Keep C#/WPF for now: no Rust comparison was performed, and no measured result justifies migration. Fix semantics and measure specific hotspots first.

## Repository and release review

- Current main's [Windows verification](https://github.com/Sparkz400/AppSwitcher/actions/runs/41545481282) and [CodeQL](https://github.com/Sparkz400/AppSwitcher/actions/runs/41545481331) succeeded.
- Main requires an up-to-date PR and Windows/C#/JavaScript/aggregate CodeQL checks; protection applies to administrators, forbids force push/deletion and requires resolved conversations. It currently requires **zero approving reviews**. A second reviewer is a useful release policy when available, not a substitute for regression tests.
- Active `v*` rules prohibit tag updates/deletions with no bypass actors. [v1.2.0-beta.3](https://github.com/Sparkz400/AppSwitcher/releases/tag/v1.2.0-beta.3) is a published immutable prerelease with eight assets. Its tag resolves to `b6942729f4e63caf62a16262d28474107438a3b1`, not current main; current-main build results must not be attributed to the already published binary.
- Read-only GitHub queries returned zero open CodeQL alerts on main, zero open Dependabot alerts, zero open secret-scanning alerts and no open PRs. This is a point-in-time scanner result, not proof that the newly found logic defects do not exist.
- Secret scanning, push protection and security updates are enabled. Actions are SHA-pinned; NuGet lockfiles and vulnerability warnings-as-errors are enabled. Weekly dependency updates include all four projects.
- Release automation accepts **beta versions only**. A stable tag will need an intentional workflow/version/notes change while retaining exact-commit artifact checks, attestations, complete-draft verification and immutability.
- Open issues cover [workstation acceptance #9](https://github.com/Sparkz400/AppSwitcher/issues/9), [Narrator #10](https://github.com/Sparkz400/AppSwitcher/issues/10), [publisher signing #11](https://github.com/Sparkz400/AppSwitcher/issues/11), and [store distribution #12](https://github.com/Sparkz400/AppSwitcher/issues/12). Store publication is **optional for the required offline standalone deployment**. The signed Firefox 1.1.0 payload already exists; distinguish that from pending store-submission packages/accounts.
- The older delivery checklist records earlier completed scope; it is not a stable-release signoff. Create focused regression work from this audit, refresh validation on the fixed release candidate, and retain historical evidence.
- Consolidate or clearly deprecate the legacy `Install-BrowserHost.ps1` / `Uninstall-BrowserHost.ps1` route. It has a different host-update/registration strategy from the UI setup service and should not drift into a second installation contract.

No remote issue, PR, repository setting, tag or release was changed during this audit.
