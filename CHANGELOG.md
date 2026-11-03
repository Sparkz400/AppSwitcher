# Changelog

## 1.2.0 — 2026-11-03

- Support unsigned stable Windows releases under the owner's zero-cost distribution policy. Remove mandatory publisher signing from stable builds and acceptance records; retain workstation and offline-browser acceptance, exact-commit verification, security scans, checksums, manifests, SPDX inventories, attestations and immutable release assets. Optional installer signatures must still be valid.
- Update release and installation guidance to disclose unsigned stable distribution. The existing Mozilla-signed Firefox payload is unchanged.
- Scope acceptance to the intended workstations with no audio output: make actual Narrator speech testing optional while retaining keyboard navigation, visible focus/errors/status, high contrast, touch usability and automated accessibility checks.
- Record offline VM qualification of the published RC, including actual installer upgrade/rollback/uninstall, released browser-bridge recovery, and persistent Chrome/Edge installations with two unattended offline restarts each. Keep their manual folder-picker walkthrough and target-workstation acceptance open. Update installer wording for beta, RC and stable packages.
- Fix the browser test helper's disconnect handling so it consumes POST bodies before replying and clears stale connections before a reconnect check; add a regression that fails against the old helper. Released application and extension behavior is unchanged.

This first **unsigned Windows x64 stable release** includes the behavior fixes and UI improvements listed under 1.2.0-rc.1 below. The Mozilla-signed offline Firefox bridge remains byte-for-byte 1.1.0, and configuration remains schema 12. Roll back with the previous binary and its pre-upgrade configuration backup.

The owner authorized publication with [documented validation limits](https://github.com/Sparkz400/AppSwitcher/blob/main/docs/RELEASE_ACCEPTANCE_1.2.0.md): physical workstation/full-shift acceptance and the Chrome/Edge manual folder-picker walkthrough remain unperformed. Persistent browser installation and unattended offline restarts passed. Site deployment still requires its applicable acceptance checks. Windows publisher signing, browser-store publication and Narrator speech are not required for the agreed zero-cost, silent-workstation scope. Windows may show an unknown-publisher warning; verify the downloaded checksums and build/release attestations.

## 1.2.0-rc.1 — 2026-11-03

- Repair session timeout transitions, scheduler retries, lifecycle cancellation, manual minimization and stale HTTP health results.
- Preserve operator restrictions and dependency/workspace references through app renames and deletion; configuration schema 12 distinguishes all-app access from an empty restricted scope.
- Make Save explicit: closing offers discard, expiry discards unsaved changes, and background pin/position saves cannot persist unfinished edits.
- Remove ineffective appearance controls and native-only website fields; make icon-only mode accurate and validate imported visual values and geometry.
- Read audit/crash history from the tail with CSV multiline support and rotate at 16 MiB, retaining five archives.
- Support beta, release-candidate and stable artifacts while gating stable publication on reviewed acceptance evidence and a valid publisher signature. Retire conflicting legacy host-install scripts.

This is an **unsigned Windows x64 release candidate**, not a qualified stable release. The Mozilla-signed offline Firefox bridge remains byte-for-byte 1.1.0. Physical workstation, Narrator, offline-browser qualification and Windows publisher signing remain explicit release gates in `docs/release-readiness.json`.

## 1.2.0-beta.3 — 2026-11-02

- Lock newly published releases and protect version tags against updates and deletion; retain earlier release bytes unchanged.
- Generate and validate an SPDX 2.3 desktop dependency inventory, with hashes for the ZIP and installer.
- Attest the exact main build and its desktop inventory; verify those records before uploading, checking and publishing a release draft.
- Add download verification, troubleshooting and keyboard guides, plus a real-app walkthrough with synthetic applications and README build/release badges.

This remains an **unsigned Windows x64 beta**. GitHub attestations do not replace Windows publisher signing. The SBOM covers desktop packages at package level, not the separate store-submission bundle or every assembly inside runtime packs. Browser-store publication, full Narrator testing and target-workstation acceptance remain open. No production application behavior or configuration format changes are introduced.

## 1.2.0-beta.2 — 2026-11-02

- Update the proposed NuGet runtime and test dependencies, refresh all four projects' lockfiles, and review shipped dependency notices.
- Fix dependency maintenance so UI-check transitive dependencies are included in the solution and grouped update proposals.
- Add C# and browser JavaScript CodeQL checks, protected pull-request integration, secret scanning and push protection.
- Add a code of conduct, support question form and public roadmap with tracked priorities and starter tasks.
- Publish tagged betas from the exact successful main CI artifact, checking source, manifests and all asset checksums without replacing earlier releases.

This remains an **unsigned Windows x64 beta**. Browser-store bundles are prepared but not submitted; the offline Mozilla-signed Firefox bridge remains 1.1.0. No application configuration format changes are introduced. Real Narrator, touch, docking/mixed-DPI, reboot/power-loss, kiosk and sustained site acceptance remain open. Automated checks do not establish production qualification.

## 1.2.0-beta.1 — 2026-11-02

First public source snapshot and unsigned Windows x64 beta.

- Add a complete offline ZIP and per-user installer, integrity manifests/checksums, and verified install/upgrade/rollback/uninstall behavior.
- Recover invalid startup configuration through an authenticated backup preview, preserving the original bytes before restoration.
- Harden PIN validation, migration, retry delays and session-expiry handling; fix background-service shutdown races.
- Add a first-run setup checklist, a live needs-attention filter, and actionable browser recovery guidance.
- Keep monitor placement tied to Windows display identities across enumeration changes, with safe fallback for unavailable displays.
- Support Windows high contrast, clearer keyboard focus, and additional screen-reader names and control patterns.
- Include desktop, touch-workstation and browser-console examples with safe defaults.
- Add pinned Windows CI, native UI/process/recovery checks, a sustained-run harness and release/installer validation.
- Publish MIT source, dependency notices, contribution/security/maintenance guidance and browser-store submission materials.

Existing features include English/German UI, application groups/favourites, operator scopes, optional schedules/hooks/watchdog, layouts, audit history and paired Chrome/Edge/Firefox tabs. The offline Firefox bridge remains the bundled Mozilla-signed 1.1.0 payload. Store preparation adds upload-only icons/metadata as version 1.1.1 and does not replace that signed offline payload.

Known limits: this beta has no Windows publisher signature or public browser-store listing. Windows can deny foreground activation from background processes. A PIN is an application control, not an OS security boundary. Real Narrator speech, touch/gloves, mixed-DPI hot-plug, reboot/power-loss and site kiosk recovery need the target workstation's acceptance record. Automated results must not be read as production certification.
