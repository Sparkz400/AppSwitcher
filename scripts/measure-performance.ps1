[CmdletBinding(DefaultParameterSetName='Existing')]
param(
    [Parameter(Mandatory, ParameterSetName='Existing')][int]$TargetProcessId,
    [Parameter(Mandatory, ParameterSetName='Launch')][string]$ExecutablePath,
    [ValidateRange(5, 600)][int]$SampleSeconds = 60,
    [ValidateRange(0, 120)][int]$SettleSeconds = 10,
    [string]$OutputPath = (Join-Path $PSScriptRoot '..\artifacts\performance-current.json')
)
$ErrorActionPreference = 'Stop'
$startupMilliseconds = $null
if ($PSCmdlet.ParameterSetName -eq 'Launch') {
    $resolved = (Resolve-Path -LiteralPath $ExecutablePath).Path
    if (Get-Process -Name AppSwitcher -ErrorAction SilentlyContinue) { throw 'AppSwitcher is already running; use -TargetProcessId to measure that instance.' }
    # Observation only: a launcher without a taskbar entry has no Process.MainWindowHandle.
    if (-not ('AppSwitcherPerformanceWindow' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class AppSwitcherPerformanceWindow {
    private delegate bool EnumProc(IntPtr hwnd, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    public static bool HasVisibleWindow(int processId) {
        bool found = false;
        EnumWindows((hwnd, parameter) => {
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == processId && IsWindowVisible(hwnd)) { found = true; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }
}
'@
    }
    $startup = [Diagnostics.Stopwatch]::StartNew()
    # The interactive app is intentionally visible; this is not a background helper.
    $launched = Start-Process -FilePath $resolved -PassThru
    $TargetProcessId = $launched.Id
    while (-not [AppSwitcherPerformanceWindow]::HasVisibleWindow($TargetProcessId)) {
        if ($launched.HasExited -or $startup.Elapsed.TotalSeconds -gt 30) { throw 'AppSwitcher did not show a window within 30 seconds.' }
        Start-Sleep -Milliseconds 25
    }
    $startupMilliseconds = $startup.Elapsed.TotalMilliseconds
}
if ($SettleSeconds -gt 0) { Start-Sleep -Seconds $SettleSeconds }
$samples = [Collections.Generic.List[object]]::new()
$timer = [Diagnostics.Stopwatch]::StartNew()
for ($index = 0; $index -le $SampleSeconds; $index++) {
    $instance = Get-Process -Id $TargetProcessId
    $samples.Add([pscustomobject]@{
        ElapsedSeconds = $timer.Elapsed.TotalSeconds
        Processes = @([pscustomobject]@{
            Id = $instance.Id; Name = $instance.ProcessName; CpuSeconds = $instance.TotalProcessorTime.TotalSeconds
            WorkingSetBytes = $instance.WorkingSet64; PrivateBytes = $instance.PrivateMemorySize64
            Handles = $instance.HandleCount; Threads = $instance.Threads.Count; Responding = $instance.Responding
        })
    })
    $instance.Dispose()
    if ($index -lt $SampleSeconds) { Start-Sleep -Seconds 1 }
}
$elapsed = $samples[-1].ElapsedSeconds - $samples[0].ElapsedSeconds
$cpu = 100 * ($samples[-1].Processes[0].CpuSeconds - $samples[0].Processes[0].CpuSeconds) / $elapsed
$privateBytes = @($samples | ForEach-Object { $_.Processes[0].PrivateBytes } | Sort-Object)
$workingSet = @($samples | ForEach-Object { $_.Processes[0].WorkingSetBytes } | Sort-Object)
$report = [ordered]@{
    CapturedUtc = [DateTime]::UtcNow; Scenario = 'Published AppSwitcher, no automated interaction during sampling'
    LogicalProcessors = [Environment]::ProcessorCount; TargetProcessId = $TargetProcessId
    StartupToVisibleWindowMilliseconds = $startupMilliseconds; SettleSeconds = $SettleSeconds
    Summary = [ordered]@{
        ElapsedSeconds = $elapsed; CpuPercentOfOneCore = $cpu; CpuPercentOfMachine = $cpu / [Environment]::ProcessorCount
        MedianPrivateMiB = $privateBytes[[int][Math]::Floor($privateBytes.Count / 2)] / 1MB
        MedianWorkingSetMiB = $workingSet[[int][Math]::Floor($workingSet.Count / 2)] / 1MB
        HandlesFirst = $samples[0].Processes[0].Handles; HandlesLast = $samples[-1].Processes[0].Handles
    }
    Samples = $samples
    Scope = 'One AppSwitcher process. Managed apps and browser/native-host processes are excluded. Startup is one local launch, not a reboot/cold-cache benchmark.'
}
$outputFile = [IO.Path]::GetFullPath($OutputPath)
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($outputFile)) | Out-Null
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $outputFile -Encoding utf8
$report.Summary | ConvertTo-Json
Write-Output "Startup to visible window (ms): $startupMilliseconds"
Write-Output "Raw samples: $outputFile"
