namespace AppSwitcher.Services;

public static class AppPaths
{
    // Set only by the friend test executable, never by production startup arguments.
    internal static string? IsolatedDataDirectory { get; set; }
    private const string AppDirectoryName = "AppSwitcher";

    public static string DataDirectory
    {
        get
        {
            if (IsolatedDataDirectory is { } isolated)
            {
                Directory.CreateDirectory(isolated);
                return isolated;
            }
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                AppDirectoryName);
            Directory.CreateDirectory(path);
            return path;
        }
    }

    public static string ConfigFilePath => Path.Combine(DataDirectory, "app_config.json");

    public static string ConfigBackupDirectory
    {
        get
        {
            var path = Path.Combine(DataDirectory, "config_backups");
            Directory.CreateDirectory(path);
            return path;
        }
    }

    public static string LogDirectory
    {
        get
        {
            var path = Path.Combine(DataDirectory, "logs");
            Directory.CreateDirectory(path);
            return path;
        }
    }

    public static string AuditLogPath => Path.Combine(DataDirectory, "audit_log.csv");

    public static string MachineDataDirectory
    {
        get
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                AppDirectoryName);
            Directory.CreateDirectory(path);
            return path;
        }
    }

    public static string KioskMachineStateFilePath => Path.Combine(MachineDataDirectory, "kiosk-machine-state.json");

    public static string WatchdogCircuitFilePath => Path.Combine(DataDirectory, "watchdog_circuits.json");

    public static IEnumerable<string> GetLegacyConfigPaths()
    {
        var candidates = new[]
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app_config.json"),
            Path.GetFullPath("app_config.json")
        };

        return candidates
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(path => !string.Equals(path, ConfigFilePath, StringComparison.OrdinalIgnoreCase));
    }
}
