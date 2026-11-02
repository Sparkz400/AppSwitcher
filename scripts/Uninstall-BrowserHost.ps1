[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$installPaths = @('Programs\AppSwitcherBrowserHost', 'Programs\AppSwitcherBrowserSetup') | ForEach-Object { [System.IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA $_)) }
foreach ($vendor in @('Google\Chrome', 'Microsoft\Edge', 'Mozilla')) {
    foreach ($view in @([Microsoft.Win32.RegistryView]::Registry32, [Microsoft.Win32.RegistryView]::Registry64)) {
        $root = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser, $view)
        try {
            $registryPath = "Software\$vendor\NativeMessagingHosts\com.appswitcher.browser"
            $key = $root.OpenSubKey($registryPath)
            if ($null -eq $key) { continue }
            try { $manifestPath = $key.GetValue('') } finally { $key.Dispose() }
            if ($manifestPath -is [string]) {
                $resolvedManifest = [System.IO.Path]::GetFullPath($manifestPath)
                if ($installPaths | Where-Object { $resolvedManifest.StartsWith($_ + '\', [StringComparison]::OrdinalIgnoreCase) }) {
                    $root.DeleteSubKey($registryPath, $false)
                }
            }
        }
        finally { $root.Dispose() }
    }
}
Write-Output 'Host registrations removed. Extension state, ownership records and browser profiles were preserved.'
