[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
if (Get-Process AppSwitcher -ErrorAction SilentlyContinue) { throw 'Close AppSwitcher before running the isolated native-host checks.' }
$repoPath = Split-Path -Parent $PSScriptRoot
$checkPath = Join-Path $repoPath 'output\playwright'
New-Item -ItemType Directory -Path $checkPath -Force | Out-Null
$checkIdentity = 'AppSwitcher checks ' + [Guid]::NewGuid().ToString('N')
$checkIdentity | Set-Content -LiteralPath (Join-Path $checkPath 'check-identity.txt') -Encoding utf8NoBOM
node (Join-Path $PSScriptRoot 'build-browser-extension.mjs')
dotnet build (Join-Path $repoPath 'AppSwitcher.BrowserHost\AppSwitcher.BrowserHost.csproj') -v minimal
if ($LASTEXITCODE -ne 0) { throw 'Host build failed.' }
$hostPath = Join-Path $repoPath 'AppSwitcher.BrowserHost\bin\Debug\net10.0-windows\AppSwitcher.BrowserHost.exe'
$origins = @('chrome-extension://afmmehaldghpcccgholfklbeemokekoj/', 'browser@appswitcher.local')
ConvertTo-Json -InputObject $origins | Set-Content -LiteralPath (Join-Path (Split-Path $hostPath) 'allowed-origins.json') -Encoding utf8NoBOM
foreach ($kind in @('chromium', 'firefox')) {
    $extensionPath = Join-Path $checkPath "$kind-extension"
    New-Item -ItemType Directory -Path $extensionPath -Force | Out-Null
    Copy-Item -Path (Join-Path $repoPath "artifacts\browser-package\$kind\*") -Destination $extensionPath -Force
    $backgroundPath = Join-Path $extensionPath 'background.js'
    (Get-Content -LiteralPath $backgroundPath -Raw).Replace('com.appswitcher.browser', 'com.appswitcher.browser.checks').Replace('profileName: local.profileName || browserName', ('profileName: "' + $checkIdentity + '"')) | Set-Content -LiteralPath $backgroundPath -Encoding utf8NoBOM
}
foreach ($vendor in @('Google\Chrome', 'Microsoft\Edge', 'Mozilla')) {
    $manifest = @{ name='com.appswitcher.browser.checks'; description='AppSwitcher isolated checks'; path=$hostPath; type='stdio' }
    if ($vendor -eq 'Mozilla') { $manifest.allowed_extensions = @('browser@appswitcher.local') }
    else { $manifest.allowed_origins = @($origins[0]) }
    $manifestPath = Join-Path $checkPath ($vendor.Replace('\','-') + '-host.json')
    $manifest | ConvertTo-Json | Set-Content -LiteralPath $manifestPath -Encoding utf8NoBOM
    $registryPath = "HKCU:\Software\$vendor\NativeMessagingHosts\com.appswitcher.browser.checks"
    if (Test-Path -LiteralPath $registryPath) {
        $existing = (Get-Item -LiteralPath $registryPath).GetValue('')
        if ($existing -ne $manifestPath) { throw "Check host registration already owned by $existing" }
    }
    New-Item -Path $registryPath -Force | Out-Null
    Set-Item -LiteralPath $registryPath -Value $manifestPath
}
$sid = [System.Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$digest = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData([System.Text.Encoding]::UTF8.GetBytes($sid))).Substring(0,24)
Write-Output "Ready. Run: node scripts/browser-check-broker.mjs AppSwitcher.Browser.v1.$digest output/playwright/broker.json output/playwright/check-identity.txt"
