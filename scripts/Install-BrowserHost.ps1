[CmdletBinding()]
param(
    [string]$ChromeExtensionId,
    [string]$EdgeExtensionId,
    [string]$FirefoxExtensionId = 'browser@appswitcher.local'
)
$ErrorActionPreference = 'Stop'
$ids = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'extension-ids.json') -Raw | ConvertFrom-Json
if (-not $ChromeExtensionId) { $ChromeExtensionId = $ids.chrome }
if (-not $EdgeExtensionId) { $EdgeExtensionId = $ids.edge }
foreach ($extensionId in @($ChromeExtensionId, $EdgeExtensionId)) {
    if ($extensionId -notmatch '^[a-p]{32}$') { throw 'Invalid Chromium extension ID.' }
}
if ($FirefoxExtensionId -notmatch '^[A-Za-z0-9@._{}-]+$') { throw 'Invalid Firefox extension ID.' }
$sourcePath = Join-Path $PSScriptRoot 'host'
if (-not (Test-Path -LiteralPath (Join-Path $sourcePath 'AppSwitcher.BrowserHost.exe'))) { throw 'Build the browser package first.' }
$installPath = Join-Path $env:LOCALAPPDATA 'Programs\AppSwitcherBrowserHost'
New-Item -ItemType Directory -Path $installPath -Force | Out-Null
Get-ChildItem -LiteralPath $sourcePath -File | Copy-Item -Destination $installPath -Force
$origins = @("chrome-extension://$ChromeExtensionId/", "chrome-extension://$EdgeExtensionId/", $FirefoxExtensionId) | Select-Object -Unique
ConvertTo-Json -InputObject @($origins) | Set-Content -LiteralPath (Join-Path $installPath 'allowed-origins.json') -Encoding utf8NoBOM
foreach ($browser in @('Chrome', 'Edge', 'Firefox')) {
    $manifest = @{ name='com.appswitcher.browser'; description='AppSwitcher browser bridge'; path=(Join-Path $installPath 'AppSwitcher.BrowserHost.exe'); type='stdio' }
    if ($browser -eq 'Firefox') { $manifest.allowed_extensions = @($FirefoxExtensionId); $vendor = 'Mozilla' }
    else {
        $extensionId = if ($browser -eq 'Chrome') { $ChromeExtensionId } else { $EdgeExtensionId }
        $manifest.allowed_origins = @("chrome-extension://$extensionId/")
        $vendor = if ($browser -eq 'Chrome') { 'Google\Chrome' } else { 'Microsoft\Edge' }
    }
    $manifestPath = Join-Path $installPath "$browser.json"
    $manifest | ConvertTo-Json | Set-Content -LiteralPath $manifestPath -Encoding utf8NoBOM
    $registryPath = "HKCU:\Software\$vendor\NativeMessagingHosts\com.appswitcher.browser"
    New-Item -Path $registryPath -Force | Out-Null
    Set-Item -LiteralPath $registryPath -Value $manifestPath
}
Write-Output "Native host installed for the current user in $installPath. Install the extensions, open AppSwitcher, then pair the browser profile."
