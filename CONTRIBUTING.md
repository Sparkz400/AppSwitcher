# Contributing

Start with an issue for a substantial behavior change; small fixes can go straight to a pull request. Explain the operator's problem, the expected result and how you checked it. Keep each change focused. By submitting a contribution, you agree that your contribution is licensed under the repository's MIT license and that you have the right to contribute it.

Read the [code of conduct](CODE_OF_CONDUCT.md). The [roadmap](ROADMAP.md) links priorities and starter tasks; [Support](SUPPORT.md) explains where to ask questions.

## Build on Windows

Install Git, PowerShell 7, the SDK specified by `global.json` and Node.js 24. From a clean checkout:

```powershell
./scripts/verify.ps1
./scripts/build-release.ps1
```

The first command restores locked packages, builds, runs .NET/extension tests, renders native UI, and exercises isolated process/recovery behavior. It opens synthetic windows and checks that the real saved configuration is unchanged. The second builds the self-contained Windows x64 package, validates bundled Firefox source/signature payload correspondence, checks installer install/upgrade/rollback/uninstall, and emits the ZIP, unsigned setup EXE, submission materials and checksums under `artifacts/releases`.

A build with uncommitted changes records `dirty: true` in its release manifest and must not be published as a release. Output directories are revision-specific and existing release artifacts are never overwritten. Build a new revision or use a fresh checkout when repeating a release build. Build-time package/compiler downloads need internet access; deployment does not.

For an individual change, use the relevant existing tests first. Full Windows verification is required before merging. Browser integration changes also need actual isolated-browser checks described in [the browser guide](docs/BROWSER_INTEGRATION.md). The longer optional check is:

```powershell
dotnet run --project AppSwitcher.UiChecks -c Release -- --soak-check 30
```

Close AppSwitcher and other process-check runs first because the isolated real application host still uses the normal single-instance mutex/current-user pipe. The harness only launches synthetic managed windows and uses a unique data directory. Its reports distinguish process termination from OS reboot or physical power loss. Do not run native process checks on an operating machine.

## Change expectations

- Keep English and German resources aligned. Preserve keyboard access, UI Automation names, focus visibility, high contrast and compact/touch layouts.
- Reuse existing services and validation. Preserve configuration migration and preview/authorization checks; add a focused regression for meaningful new behavior.
- Avoid new dependencies for small helpers. Dependency updates require lockfile, audit and `third-party` inventory/notice updates together.
- Never commit real settings, PIN hashes, certificates, private keys, browser profiles, logs from a customer, build output or personal paths. Use safe synthetic examples.
- Do not modify a bundled signed Firefox payload. Extension source changes require a new extension version and new Mozilla signing before an offline production package can be released. Store submission sources are a separate, unsigned preparation artifact.
- Include test evidence and remaining limitations. Simulated hardware, native UI automation and hosted CI do not establish workstation or machine acceptance.

## Updating dependencies

All four projects belong to `AppSwitcher.sln`. After changing a package version, run `dotnet restore AppSwitcher.sln --force-evaluate` to refresh every affected lockfile, including the UI-check project's transitive references. Commit the project changes and all affected `packages.lock.json` files together. Then run the normal locked verification; do not remove locked mode or weaken vulnerability checks to make an update pass.

For shipped dependencies, inspect the new package's license, source commit and notices. Update `third-party/dependencies.json`, the retained upstream notices and `THIRD_PARTY_NOTICES.md` before building the release package. Test-tool major updates also need a successful full test run. Dependabot groups NuGet updates across project directories into one proposal; its PR is a proposal, not an automatic approval of versions or licenses.

Changes reach `main` through a pull request with up-to-date Windows verification and both CodeQL language checks. Resolve review conversations before merging. A second reviewer is welcome but is not mandatory for this single-maintainer project; administrators follow the same checks. Release automation is documented in [Maintenance](MAINTENANCE.md).
