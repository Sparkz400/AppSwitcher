[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$checkPath = [System.IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $PSScriptRoot) 'output\playwright'))
foreach ($vendor in @('Google\Chrome', 'Microsoft\Edge', 'Mozilla')) {
    $registryPath = "HKCU:\Software\$vendor\NativeMessagingHosts\com.appswitcher.browser.checks"
    if (Test-Path -LiteralPath $registryPath) {
        $value = (Get-Item -LiteralPath $registryPath).GetValue('')
        if ($value -and [System.IO.Path]::GetFullPath($value).StartsWith($checkPath + '\', [StringComparison]::OrdinalIgnoreCase)) {
            Remove-Item -LiteralPath $registryPath
        }
    }
}
Write-Output 'Removed the isolated check registrations. Production registrations and browser profiles were not changed.'
