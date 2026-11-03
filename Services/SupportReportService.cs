using System.Runtime.InteropServices;

namespace AppSwitcher.Services;

internal static class SupportReportService
{
    // Allowlisted structured data only. No raw log text, config serialization, names, URLs, paths or user/profile IDs.
    internal static string Build(IReadOnlyList<AppItem> apps, IReadOnlyList<BrowserProfileChoice> profiles,
        IReadOnlyList<AuditLogEntry> audit, bool maintenancePaused, int errors, int warnings)
    {
        using var process = Process.GetCurrentProcess();
        return JsonSerializer.Serialize(new
        {
            schema = 1, generatedUtc = DateTimeOffset.UtcNow,
            appVersion = typeof(SupportReportService).Assembly.GetName().Version?.ToString(),
            build = System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(
                typeof(SupportReportService).Assembly)?.InformationalVersion,
            runtime = RuntimeInformation.FrameworkDescription, architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            windowsVersion = Environment.OSVersion.Version.ToString(),
            launcher = new { workingSetBytes = process.WorkingSet64, cpuMilliseconds = process.TotalProcessorTime.TotalMilliseconds,
                uptimeSeconds = (DateTime.Now - process.StartTime).TotalSeconds, maintenancePaused },
            validation = new { errors, warnings },
            browsers = profiles.Select((p, i) => new { profile = $"Profile {i + 1}", browser = p.Browser.ToString(),
                extensionVersion = SafeVersion(p.ExtensionVersion), connection = "Connected" }),
            applications = apps.Select((a, i) => new { application = $"App {i + 1}", kind = a.TargetKind.ToString(), state = a.State.ToString(),
                a.IsResponding, a.CpuPercent, a.MemoryMB, a.CrashCountToday, a.TotalRestarts, a.RequiresWatchdogAcknowledgement }),
            recentEvents = audit.Take(50).Select(e => new { e.Timestamp, action = SafeAction(e.Action),
                outcome = e.Outcome is "Success" or "Failed" or "Denied" or "Cancelled" ? e.Outcome : "Other" }),
            redactions = new[] { "Names", "User and profile identities", "URLs", "Paths", "Arguments", "Credentials", "Raw logs and event details" }
        }, AppJsonOptions.Default);
    }
    private static string SafeVersion(string value) => Version.TryParse(value, out var version) ? version.ToString() : "Unknown";
    private static string SafeAction(string value) => value is "ConfigChanged" or "AppStopped" or "Authentication" or "LaunchApp" or "KillApp" or "StartGroup" or "StopGroup"
        or "StartWorkspace" or "MaintenancePause" or "MaintenanceResume" or "SaveConfig" or "BrowserCheck" or "BrowserSwitchTest"
        ? value : "Other";
}
