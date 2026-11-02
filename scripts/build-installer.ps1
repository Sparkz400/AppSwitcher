#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PackagePath,
    [Parameter(Mandatory)][string]$OutputPath,
    [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9._-]+$')][string]$Name,
    [Parameter(Mandatory)][ValidatePattern('^[0-9][A-Za-z0-9.+-]*$')][string]$Version,
    [ValidatePattern('^[a-f0-9]{32}$')][string]$TestId
)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$package = (Resolve-Path -LiteralPath $PackagePath).Path
if (-not (Test-Path -LiteralPath (Join-Path $package 'app/AppSwitcher.exe'))) { throw 'The complete application package is required.' }
$output = [IO.Path]::GetFullPath($OutputPath)
New-Item -ItemType Directory -Path $output -Force | Out-Null
$installer = Join-Path $output "$Name.exe"
if (Test-Path -LiteralPath $installer) { throw "Installer already exists: $installer" }
$compiler = & (Join-Path $PSScriptRoot 'get-inno-setup.ps1')
$arguments = @('--quiet', '--no-signing', '--no-ide-signtools', "--define=PackageRoot=$package", "--define=ProductVersion=$Version", "--output-dir=$output", "--output-filename=$Name")
if ($TestId) { $arguments += "--define=ProductId=AppSwitcher.Verification.$TestId", "--define=InstanceMutex=AppSwitcher.Verification.$TestId" }
& $compiler @arguments (Join-Path (Split-Path -Parent $PSScriptRoot) 'installer/AppSwitcher.iss') | Out-Host
if (-not (Test-Path -LiteralPath $installer)) { throw 'Installer compiler produced no executable.' }
"$((Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash)  $Name.exe" | Set-Content -LiteralPath (Join-Path $output "$Name.exe.sha256") -Encoding ascii
Write-Output $installer
