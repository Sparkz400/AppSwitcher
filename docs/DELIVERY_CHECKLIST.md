# Public beta delivery checklist

Scope: every item agreed in the 2026-11-02 improvement list. An implementation is complete only after its relevant checks pass and it is integrated into `main`. Local automation does not establish physical-panel, customer, publisher or store acceptance.

| ID | Deliverable | Status | Completion evidence / remaining work |
| --- | --- | --- | --- |
| 01 | Automated Windows builds and tests | Complete | PR #1 merged as `19b8d3a`; exact-main GitHub run `41493425059` passed. Pinned actions/SDK, locked dependencies, reports/captures and full offline archive are retained. |
| 02 | Startup configuration recovery | Complete | PR #2 merged as `666a642`; both branch/PR Windows checks passed. Real isolated startup recovery requires the backup PIN and confirmation, preserves the failed file and exits without opening the launcher. Compact/long-error layouts pass. |
| 03 | Complete versioned release package | Complete | PR #1 includes archive/manifest/hash generation. All 842 archive files verified at clean `4a41a18`; fresh hosted checkout and exact merged main both passed, including 15 native package assertions. Subsequent release must use the final source commit. |
| 04 | Installer, signing, offline upgrade and rollback | Complete | PR #6 merged as `04a67f3`; branch/PR Windows runs `41508349018` and `41508356718` passed. All 23 isolated checks also pass on the complete clean `e1584156` package, including hashes, upgrade/rollback, in-use guards and preservation. The installer is verified unsigned, as selected by the user. |
| 05 | Access-control hardening and tests | Complete | PRs #1/#2 add bounded hash validation, retry delay, legacy/operator migration, PIN-dialog expiry and import/restore authorization rechecks. Regression and actual modal-expiry tests pass. The PIN remains an application UI guard, not a Windows security boundary. |
| 06 | Deployment qualification | Software checks complete; workstation acceptance open | A 30-minute isolated run passed with 177 samples, five abrupt host restarts and the same managed PID throughout. Chrome 157, Edge 156 and Firefox 159 native-host scenarios passed; bundled Chromium 156 full session restart passed. Real site kiosk, power-loss, offline network and full-shift acceptance remain under WORKSTATION_ACCEPTANCE.md. |
| 07 | First-run checklist | Complete | PR #3 merged as `e8ccb6e`; exact-main run `41499509416` passed. Six localized steps use existing controls and distinguish present settings from required workstation tests. New installations open setup through the administrator flow; existing configurations are not interrupted. |
| 08 | Needs-attention filter | Complete | PR #3; live faults and watchdog acknowledgements update without exposing out-of-scope apps. Maintenance stays visible. Ordinary stopped apps are excluded with an explicit empty-state explanation. |
| 09 | Browser recovery guidance | Complete | PR #3; fault-specific next steps cover disconnected/copied profiles, changed pairing, damaged/ambiguous identity, unreadable journal, invalid URL and closed tabs. No identity reset or duplicate creation is introduced. |
| 10 | Stable display identification | Complete | PR #4 merged as `1d6488b`; exact-main Windows run `41501851643` passed. Display-interface identities cover launcher, window policies and layouts. Windows labels and unavailable selections preserve identity; simulated topology/scaling and native editor checks pass. Physical qualification remains under #06. |
| 11 | Backup restore preview | Complete | PR #2 adds app/schedule/access/layout comparison and fingerprint revalidation. Restore preserves old data outside retention; unique backup filenames never overwrite. Regression and real startup tests pass. |
| 12 | Example configurations | Complete | PR #6; desktop, touch-workstation and browser examples use safe defaults, import through the real validator and include adaptation instructions. Three new import tests pass, for 253 total .NET tests. |
| 13 | Accessibility and touch qualification | Software complete; workstation acceptance open | PR #5 merged as `b21ce478`; exact-main Windows run `41505187602` passed. High contrast, live theme updates, opaque launcher and labelled controls are covered by UI Automation, keyboard, contrast and touch-target checks. Actual Narrator speech and physical touch/glove acceptance remain workstation checks. |
| 14 | Optional browser-store distribution | Complete | PR #7 merged as `a34ea13`; submission ZIPs, icons, actual screenshot, English/German listings, privacy/permission disclosures and reviewer instructions are included. Firefox upload lint reports zero errors/warnings/notices. Publisher submission/approval is outside the accepted scope. |
| 15 | Open-source license | Complete | PR #7; MIT project license, exact inventory of 13 shipped dependencies, retained upstream notices and installer license. Positive/negative checks reject an unreviewed or stale dependency inventory. |
| 16 | Publication rights and history review | Prepared; final snapshot pending | Historical PIN hash stays in private history. Current snapshot scan finds only the verified 2048-bit Chromium public key. Author/source, dependency, artwork and synthetic screenshot review recorded in PUBLICATION_REVIEW.md; repeat scan on the final clean public snapshot. |
| 17 | Public README | Complete | PR #7; audience, safe quick start, screenshots, release links, requirements and documentation map added; local documentation links checked. |
| 18 | Contribution and security guidance | Prepared; reporting route pending | Build/contribution guide, security boundaries, issue/PR templates and private-reporting instructions added. Enable and verify the public repository's reporting endpoint before publication handoff. |
| 19 | Published versioned beta | Open | Verified complete package, release notes, hashes, limitations and upgrade instructions. |
| 20 | Maintenance commitment | Complete | PR #7; latest-beta support policy, best-effort response expectations, required release checks, rollback retention and weekly dependency-update proposals documented. |

## Evidence rules

- Test reports identify the tested commit or explicitly describe uncommitted changes.
- Release artifacts include their source commit and file hashes.
- Physical or external checks stay open until performed; a procedure is not a passing result.
- Preserve production configuration, browser profiles, active processes and unique evidence.

## Foundation validation

The expanded suite exposed a pre-existing start/stop race: queued background work read a cancellation source after shutdown cleared it. Scheduler, health, resources, session timeout, version monitoring, window policy and process monitoring now capture their token before queueing. A regression exercises 100 immediate start/stop cycles across the six standalone services.

The complete local verification passed on 2026-11-02. Thirty native window switches restored the same window and all thirty obtained foreground activation; four of five repeated-launch trials obtained foreground activation. These are synthetic native Windows processes, not industrial hardware or browser qualification. The run produced no WPF binding warnings or errors.

## Recovery validation

The full recovery pass completed 228 .NET tests, 31 extension tests, native UI and lifecycle checks, and a real startup recovery process. A subsequent privacy/diff regression brought the focused recovery suite to nine passing tests; the final render pass completed 3,920 assertions with no binding warnings/errors. The startup process began with a deliberately damaged configuration, required the backup's PIN and confirmation, preserved the original bytes and closed without opening the launcher. English/German and both themes include compact, invalid-backup, empty-backup and long-error presentation checks. PIN-setting and removal dialogs were closed by the actual session-expiry callback without changing credentials.

## Setup and monitoring validation

The local milestone pass completed 244 .NET tests, 31 extension tests and 4,665 native UI assertions with no binding warnings/errors. Native controls exercised all six setup destinations in English/German, both themes and compact sizes; the attention filter retained maintenance controls and explained its empty state. The real process recovery check also passed. Native window switching obtained foreground activation in 30/30 trials; repeated-launch trials restored the same settings window but obtained foreground in 0/5 trials on this interactive desktop. Foreground restrictions remain a separately reported Windows boundary.

The first merged recovery-main run (`41497074312`) exposed a pre-existing responsiveness test's fixed 200 ms scheduling assumption. The setup milestone replaces it with a bounded wait for the actual worker-start signal and checks the concurrency bound again after draining all work. It does not weaken or retry the failing assertion. The subsequent full local suite passed; final integrated Windows checks must pass before release.

## Display validation

The full local display pass completed 250 .NET tests, 31 extension tests and 4,880 native UI assertions with no binding warnings/errors. Native discovery found two usable display interfaces on this desktop. Tests exercise changed enumeration order, a missing or ambiguous identity, reconnect, negative desktop coordinates, changed resolution, oversized windows, 150%/200% coordinate conversion and legacy index-only layouts. Native UI checks keep a disconnected selection intact and show its reconnection without overwriting configuration. Lifecycle/recovery checks passed; foreground was obtained in 30/30 native switches and 5/5 repeated launches in this run. Simulated topology/scaling cases are separate from physical hot-plug and mixed-DPI workstation qualification.

## Accessibility validation

The full local accessibility pass completed 250 .NET tests, 31 extension tests and 9,948 native UI assertions with no binding warnings/errors. Visible input/action controls expose nonempty UI Automation names and native invoke/value/selection/toggle patterns. The expanded check caught and fixed unnamed operator-scope group choices and eight kiosk status indicators. Existing keyboard navigation/search/PIN/session-expiry checks remain enabled.

Native renders exercise yellow-on-black and black-on-white high-contrast resource palettes in English/German, all five launcher icons and PIN/settings/monitoring screens. Leaving high contrast restores configured colors, opacity and glow without rewriting settings. The palettes are overridden within the isolated test application; this does not change Windows accessibility settings or establish a live Narrator speech/physical touch result. Process/recovery checks passed; native foreground was 30/30, repeated-launch foreground 0/5 while the same settings window was restored. Windows foreground restrictions continue to be reported separately.
