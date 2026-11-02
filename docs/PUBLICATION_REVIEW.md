# Public snapshot review

Review date: 2026-11-02. This records the technical provenance and publication checks for the first public beta; it is not an independent ownership opinion or proof that no undiscovered issue exists.

## Source and history

The original repository's reachable development history was scanned with Gitleaks 8.30.1. A historical local administrator PIN hash was found. That history stays in the private development repository; it is not rewritten or pushed into the public repository. The public project starts from a clean snapshot containing only reviewed tracked source, safe defaults, examples, documentation and release inputs.

The current snapshot scan produced one finding: the Chromium manifest's `key`. It was parsed successfully as a 2048-bit RSA **public** SubjectPublicKeyInfo value (294 bytes), used to keep the unpacked extension identity stable. It is not a private signing key or access credential. No additional secret finding appeared in that snapshot. The default configuration has empty PIN hashes, and test/example data is synthetic. A separate tracked-file inventory found old `.dotnet` machine/telemetry caches, `.idea` workspace metadata and nested test build caches; those are removed from tracked source and ignored while the original local copies remain preserved. Build output, actual user data, browser profiles, local logs, private history and scanner reports are excluded from public source.

Reachable original commits name the repository owner's name variants as authors. The current source review found no separate copied-code copyright headers outside the identified dependencies. Project artwork's generation prompt is retained; the public icon and losslessly extracted store sizes come from that committed artwork. The four README/UI images and the store screenshot were visually inspected and show synthetic names, paths and profile identity. They contain no customer workstation capture.

## Dependencies and distribution

The application and native host publish 13 exact third-party dependency/runtime entries, recorded with versions, licenses and source commits in [the inventory](../third-party/dependencies.json). Their notices and the installer-engine license are retained in [third-party notices](../THIRD_PARTY_NOTICES.md). The release check rejects an unknown dependency/version, a stale inventory or a missing notice; both positive and negative cases are exercised.

The offline package's signed Firefox 1.1.0 payload is compared byte-for-byte with its generated JavaScript/HTML/CSS/manifest inputs before packaging. Mozilla performs cryptographic installation verification. Store submission files are separately generated as unsigned 1.1.1 metadata/icon updates and are not represented as an approved or signed distribution.

The user's accepted scope is an unsigned Windows beta and browser-store submission files. No publisher certificate, account, commercial support commitment or machine certification is implied. The release manifest records the exact source commit, dirty state, file hashes and lengths. Public release publication requires a clean source checkout and passing Windows verification at that source commit.

## Continuing review

Review new source/assets and dependency notices before each release. Do not add customer configuration to source or issue attachments. Keep sensitive vulnerability reports on the [private reporting route](../SECURITY.md). Follow [maintenance policy](../MAINTENANCE.md), and retain site-specific test evidence privately with the deployment owner.
