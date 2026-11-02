# Browser-store submission materials

The first desktop beta uses offline extensions. This folder prepares a later Chrome Web Store, Edge Add-ons or Mozilla submission without requiring publisher accounts now.

Run `scripts/build-store-submission.ps1 -OutputPath <new-directory>` from the repository, or use the `store-submission.zip` asset from the desktop release. Upload only the browser-specific `*-upload-1.1.1.zip` inside that bundle. The preparation archive itself is not an installable extension.

Use [listing copy](LISTING.md), [privacy information](PRIVACY.md), and [reviewer notes/checklist](REVIEWER_NOTES.md). `assets` contains the actual extension screenshot and project icon sizes. `source/chromium` and `source/firefox` contain the exact unpacked submission payloads. `submission-manifest.json` records source and hashes.

The files are unsigned and have not been submitted or approved. Actual publisher identity, issued Chrome/Edge IDs, Mozilla ownership/signing, dashboard attestations and store acceptance are later publisher actions. The offline signed Firefox 1.1.0 package is preserved unchanged.
