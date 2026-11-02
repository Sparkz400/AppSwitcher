# Verify a download

Download from [AppSwitcher Releases](https://github.com/Sparkz400/AppSwitcher/releases). Each distribution has an adjacent SHA-256 file. In PowerShell, run `Get-FileHash -Algorithm SHA256` on the downloaded file and compare the complete hash and filename with that checksum. A mismatch means you should not use the download.

## Signed build and release records

Starting with **1.2.0-beta.3**, the main Windows workflow creates GitHub build-provenance attestations for all eight release assets. Publication verifies the signing workflow, exact source commit and `main` reference before uploading a draft. After upload digests are checked, publication locks the release and its version tag. The release also has GitHub's own signed release attestation.

With an authenticated, current [GitHub CLI](https://cli.github.com/), use the tag, full source commit from the release notes, and exact downloaded filename:

```powershell
$tag = 'vX.Y.Z-beta.N'                       # Replace with the release tag
$commit = 'FULL_40_CHARACTER_SOURCE_COMMIT'  # From that release's notes
$file = '.\DOWNLOADED_ASSET_FILENAME'        # ZIP, setup EXE or another asset
gh release verify $tag --repo Sparkz400/AppSwitcher
gh release verify-asset $tag $file --repo Sparkz400/AppSwitcher
gh attestation verify $file --repo Sparkz400/AppSwitcher --signer-workflow Sparkz400/AppSwitcher/.github/workflows/verify.yml --source-ref refs/heads/main --source-digest $commit --deny-self-hosted-runners
```

All commands must succeed. Do not treat a network, authentication or verification error as a pass. The release record checks which bytes were published; the build record checks which workflow and source produced them. These are separate checks from Windows Authenticode signing: the installer is still **unsigned**.

The older beta.1 and beta.2 releases retain their original assets and checksums. They do not gain attestations or immutable status retroactively. Version tags are protected against updates and deletion. Repository protections can still be changed by an authorized administrator; they are not a guarantee against every account compromise.

## Dependency inventory (SBOM)

The `AppSwitcher-…-win-x64-….spdx.json` asset is an SPDX 2.3 software bill of materials. It describes the portable desktop ZIP and installer, binds their SHA-256 hashes, and lists the application, browser host/bridge, reviewed runtime dependencies and installer engine with versions and licenses. CI checks the official pinned SPDX schema, reviewed inventory and distribution hashes.

Runtime packs are listed as packages; their internal assemblies are not individually expanded. Build/test tools, Windows, installed browsers and the separately prepared store-submission ZIP are outside this desktop inventory. The package's `release-manifest.json` supplies individual file hashes. An SBOM is an inventory, not a vulnerability scan or proof that every upstream component is safe.

For the desktop ZIP or setup EXE, verify its signed SBOM association as well:

```powershell
gh attestation verify $file --repo Sparkz400/AppSwitcher --signer-workflow Sparkz400/AppSwitcher/.github/workflows/verify.yml --source-ref refs/heads/main --source-digest $commit --deny-self-hosted-runners --predicate-type https://spdx.dev/Document/v2.3
```

The store-submission ZIP has build provenance but no desktop-SBOM attestation. It remains unsigned, unsubmitted preparation material, not an installed store package. See [store submission](store/README.md).
