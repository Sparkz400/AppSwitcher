# Changelog

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
