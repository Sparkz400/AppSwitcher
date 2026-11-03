#requires -Version 7.0
[CmdletBinding()]
param([switch]$Fast)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$repoPath = Split-Path -Parent $PSScriptRoot
Push-Location $repoPath
try {
    New-Item -ItemType Directory -Path artifacts/test-results -Force | Out-Null
    dotnet restore AppSwitcher.sln --locked-mode | Tee-Object -FilePath artifacts/test-results/restore.txt
    dotnet build AppSwitcher.sln -c Release --no-restore | Tee-Object -FilePath artifacts/test-results/build.txt
    dotnet test AppSwitcher.sln -c Release --no-build --no-restore --logger 'trx;LogFileName=tests.trx' --results-directory artifacts/test-results
    & (Join-Path $PSScriptRoot 'test-release-notices.ps1')
    & (Join-Path $PSScriptRoot 'test-release-artifacts.ps1') | Tee-Object -FilePath artifacts/test-results/release-artifacts.txt
    & (Join-Path $PSScriptRoot 'test-stable-readiness.ps1') | Tee-Object -FilePath artifacts/test-results/stable-readiness.txt
    & (Join-Path $PSScriptRoot 'test-sign-release-files.ps1') | Tee-Object -FilePath artifacts/test-results/signing-adapter.txt
    node --test browser-extension/tests/core.test.mjs scripts/tests/browser-check-broker.test.mjs | Tee-Object -FilePath artifacts/test-results/browser-tests.txt
    if (-not $Fast) {
    dotnet run --project AppSwitcher.UiChecks -c Release --no-build --no-restore | Tee-Object -FilePath artifacts/test-results/native-ui.txt
    dotnet run --project AppSwitcher.UiChecks -c Release --no-build --no-restore -- --process-checks | Tee-Object -FilePath artifacts/test-results/process-checks.txt
    dotnet run --project AppSwitcher.UiChecks -c Release --no-build --no-restore -- --recovery-process-check | Tee-Object -FilePath artifacts/test-results/recovery-process.txt
    }
} finally {
    Pop-Location
}
