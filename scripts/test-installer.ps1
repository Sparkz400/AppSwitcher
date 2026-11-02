#requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string]$PackagePath)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$repoPath = Split-Path -Parent $PSScriptRoot
$package = (Resolve-Path -LiteralPath $PackagePath).Path
$testId = [Guid]::NewGuid().ToString('N')
$testRoot = Join-Path $repoPath "artifacts/installer-checks/$testId"
$installPath = Join-Path $testRoot 'installed'
$reportPath = Join-Path $repoPath 'artifacts/test-results/installer'
New-Item -ItemType Directory -Path $reportPath, $testRoot -Force | Out-Null
$checks = [Collections.Generic.List[object]]::new()
$passed = $false
$config = Join-Path $env:LOCALAPPDATA 'AppSwitcher/app_config.json'
$configHash = if (Test-Path -LiteralPath $config) { (Get-FileHash -LiteralPath $config).Hash } else { $null }

function Assert-Check([bool]$Condition, [string]$Name) {
    $checks.Add([ordered]@{ name = $Name; passed = $Condition })
    if (-not $Condition) { throw "Installer check failed: $Name" }
}
function Invoke-Setup([string]$Executable, [string]$Label, [switch]$Blocked, [switch]$Uninstall) {
    $log = Join-Path $reportPath "$Label.log"
    $arguments = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /NOICONS /LOG="' + $log + '"'
    if (-not $Uninstall) { $arguments += ' /DIR="' + $installPath + '"' }
    $process = Start-Process -FilePath $Executable -ArgumentList $arguments -WindowStyle Hidden -PassThru
    # A cold hosted run took over two minutes just to open its setup log; keep a bounded allowance for startup and installation.
    if (-not $process.WaitForExit(300000)) { $process.Kill($true); throw "$Label timed out; test processes were stopped." }
    Assert-Check ($Blocked ? ($process.ExitCode -ne 0) : ($process.ExitCode -eq 0)) "$Label exit code ($($process.ExitCode))"
}
function Assert-Payload([string]$Source, [string]$Label) {
    $files = @(Get-ChildItem -LiteralPath $Source -File -Recurse)
    foreach ($file in $files) {
        $relative = [IO.Path]::GetRelativePath($Source, $file.FullName)
        $installed = Join-Path $installPath $relative
        if (-not (Test-Path -LiteralPath $installed) -or (Get-FileHash -LiteralPath $installed).Hash -ne (Get-FileHash -LiteralPath $file.FullName).Hash) {
            throw "$Label payload mismatch: $relative"
        }
    }
    Assert-Check ($files.Count -gt 0) "$Label all $($files.Count) payload hashes match"
}

$uninstaller = Join-Path $installPath 'unins000.exe'
try {
    # An isolated product ID prevents tests from changing a real installation's registration.
    $original = & "$PSScriptRoot/build-installer.ps1" -PackagePath $package -OutputPath $testRoot -Name original -Version 1.2.0-beta.1 -TestId $testId
    $upgradePackage = Join-Path $testRoot 'upgrade-package'
    Copy-Item -LiteralPath $package -Destination $upgradePackage -Recurse
    Add-Content -LiteralPath (Join-Path $upgradePackage 'README.md') -Value "`nInstaller test upgrade $testId"
    $upgrade = & "$PSScriptRoot/build-installer.ps1" -PackagePath $upgradePackage -OutputPath $testRoot -Name upgrade -Version 1.2.0-beta.2 -TestId $testId

    Invoke-Setup $original 'fresh-install'
    Assert-Payload $package 'fresh install'
    $legacyConfig = Join-Path $installPath 'app/app_config.json'
    $userNote = Join-Path $installPath 'operator-notes.txt'
    '{"installer-test":"preserve"}' | Set-Content -LiteralPath $legacyConfig
    'Keep user-created files.' | Set-Content -LiteralPath $userNote
    $legacyHash = (Get-FileHash -LiteralPath $legacyConfig).Hash
    $mutex = [Threading.Mutex]::new($false, "AppSwitcher.Verification.$testId")
    try {
        Invoke-Setup $upgrade 'running-app-upgrade' -Blocked
        Invoke-Setup $uninstaller 'running-app-uninstall' -Blocked -Uninstall
        Assert-Payload $package 'running app blocked before mutation'
    } finally { $mutex.Dispose() }

    foreach ($hostFile in @('app/browser-setup/host/AppSwitcher.BrowserHost.exe', 'host/AppSwitcher.BrowserHost.exe')) {
        $stream = [IO.File]::Open((Join-Path $installPath $hostFile), 'Open', 'Read', 'None')
        try {
            $label = $hostFile.StartsWith('app/') ? 'nested-host' : 'root-host'
            Invoke-Setup $upgrade "$label-upgrade" -Blocked
            Invoke-Setup $uninstaller "$label-uninstall" -Blocked -Uninstall
        } finally { $stream.Dispose() }
        Assert-Payload $package "$label blocked before mutation"
    }

    Invoke-Setup $upgrade 'upgrade'
    Assert-Payload $upgradePackage 'upgrade'
    Assert-Check ((Get-FileHash -LiteralPath $legacyConfig).Hash -eq $legacyHash) 'upgrade preserves legacy configuration'
    Invoke-Setup $original 'rollback'
    Assert-Payload $package 'rollback'
    Assert-Check ((Get-FileHash -LiteralPath $legacyConfig).Hash -eq $legacyHash) 'rollback preserves legacy configuration'
    Invoke-Setup $uninstaller 'uninstall' -Uninstall
    Assert-Check (-not (Test-Path -LiteralPath (Join-Path $installPath 'app/AppSwitcher.exe'))) 'uninstall removes installed application'
    Assert-Check ((Get-FileHash -LiteralPath $legacyConfig).Hash -eq $legacyHash) 'uninstall preserves legacy configuration'
    Assert-Check (Test-Path -LiteralPath $userNote) 'uninstall preserves user-created files'
    Assert-Check (-not (Test-Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\AppSwitcher.Verification.${testId}_is1")) 'uninstall removes isolated product registration'
    $afterHash = if (Test-Path -LiteralPath $config) { (Get-FileHash -LiteralPath $config).Hash } else { $null }
    Assert-Check ($configHash -eq $afterHash) 'production configuration is unchanged'
    $resolved = [IO.Path]::GetFullPath($testRoot)
    $allowed = [IO.Path]::GetFullPath((Join-Path $repoPath 'artifacts/installer-checks')) + [IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase)) { throw 'Refusing cleanup outside the owned test directory.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
    $passed = $true
} finally {
    [ordered]@{ passed = $passed; testId = $testId; package = $package; checkedUtc = [DateTime]::UtcNow.ToString('o'); checks = $checks.ToArray() } |
        ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $reportPath 'results.json') -Encoding utf8NoBOM
}
Write-Output "Installer verification passed: $($checks.Count) checks."
