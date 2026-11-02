# Maintenance and release policy

AppSwitcher is a community-maintained beta. There is no promised release schedule, response time, service-level agreement or machine certification. The repository is available for inspection, modification and independent builds under MIT. Report reproducible bugs with synthetic data; production deployments remain the operator's responsibility.

## Supported line

The latest `1.2.0-beta.*` and `main` receive fixes. Older betas are retained for controlled rollback, not maintained in parallel. Version numbers use `major.minor.patch` with a beta suffix while interfaces and configuration behavior are still being qualified. An incompatible configuration or bridge protocol change must be called out in the changelog with migration and rollback instructions.

The release target is Windows 11 x64 with a self-contained .NET runtime. The installer permits compatible Windows 10 build 19041 or later, but installation compatibility is not an operating-system support commitment. Use a Windows edition still supported under your organization's Microsoft servicing arrangement. ARM64 emulation, Windows Server, remote desktop, specific kiosk editions, unusual display drivers and physical touch panels require separate acceptance. The desktop app is not supported on macOS or Linux.

The extension manifests declare Chrome/Edge 120+ and Firefox 142+ compatibility. Deploy a browser with current vendor security support and test the exact version/profile/policy combination. Manifest minimums are API floors, not a promise to support obsolete browsers. Store approval and code signing are separate distribution steps; this beta is unsigned and uses offline extension installation.

## Before each release

1. Review open bug/security reports and update release notes, supported versions and known limitations.
2. Restore locked packages; review vulnerability results, dependency licenses and asset/source provenance. Review new dependencies before updating the inventory.
3. Run full Windows CI at the exact source commit and retain native captures, test reports and installer evidence. Re-run real browser/restart checks when browser integration changes.
4. For a deployment candidate, record a sustained-run result and complete [workstation acceptance](docs/WORKSTATION_ACCEPTANCE.md) for the target site. No unattended update is enabled.
5. Build from a clean tagged source revision. Verify the archive manifest, setup/ZIP checksums, unsigned status and bundled Firefox payload. Publish release notes with exact evidence boundaries.
6. Keep the previous release and a pre-update configuration backup for rollback. Publish a correction or replacement version if a release is faulty; never silently replace a published asset.

Dependency update proposals are checked weekly by automation. That cadence is a check, not a promise that updates will be merged weekly. Security reports and user-visible regressions take priority over feature work.

## Automated beta publication

1. Update `Directory.Build.props` and the matching version section in `CHANGELOG.md` in a normal pull request. Review dependency notices and complete the checks above.
2. Merge through the protected branch. Wait for **Windows verification** and **CodeQL** to succeed on the exact resulting `main` commit, not just the pull-request merge preview.
3. Create and push the matching `vX.Y.Z-beta.N` tag at that commit. The **Publish verified beta** workflow checks the version and main ancestry, refuses open critical/high-security or error-level code-scanning findings on `main`, then downloads the eight assets from that commit's successful Windows run. It verifies package/source manifests, checksums, the SPDX inventory, and signed build/SBOM attestations from the exact main revision. It creates a draft, checks uploaded asset digests, then publishes an immutable prerelease with links to the verification runs.

The workflow publishes the tested artifact without rebuilding it. Artifacts are retained for 30 days; if the artifact expired, rerun Windows verification for the exact commit before tagging. A tag pushed before checks finish fails publication; after resolving the cause, rerun the failed release workflow without moving the tag. Existing releases are never overwritten. A correction to a published release needs a new version.

If publication stops after uploading a complete draft, run **Publish verified beta → Run workflow** from `main` and supply its existing beta tag. This uses the current publication workflow with the original tagged source and rechecks the successful source runs, all manifests, hashes and attestations. It only publishes an existing draft when all eight server asset hashes match the verified build; it does not upload or replace assets on this recovery path. A missing, incomplete, changed or already-published draft is rejected. Investigate incomplete uploads separately; never move a protected tag or silently replace release files.

Keep repository immutable releases enabled and the active `v*` tag ruleset in place, with updates/deletion denied and creation allowed. Existing beta.1/beta.2 assets remain unchanged and do not gain immutable status retroactively. Main builds produce provenance for all eight assets and an SBOM attestation for the desktop ZIP/installer only; pull requests do not produce publication attestations. See [download verification and inventory scope](docs/VERIFY_DOWNLOADS.md).

The workflow currently accepts beta versions only and produces unsigned packages. Stable releases, publisher signing and store submission require the separate work tracked in [the roadmap](ROADMAP.md). Keep the old installer and a pre-update configuration backup when upgrading.
