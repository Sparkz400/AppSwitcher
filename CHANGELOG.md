# Changelog

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
