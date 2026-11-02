using Microsoft.Win32;

namespace AppSwitcher.Services;

internal static class BrowserSetupDiagnostics
{
    public static string HostStatus(BrowserKind browser)
    {
        var vendor = browser switch { BrowserKind.Chrome => @"Google\Chrome", BrowserKind.Edge => @"Microsoft\Edge", _ => "Mozilla" };
        try
        {
            foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
            foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var key = root.OpenSubKey($@"Software\{vendor}\NativeMessagingHosts\com.appswitcher.browser");
                if (key?.GetValue(null) is not string path) continue;
                if (!File.Exists(path)) return "HostBroken";
                using var manifest = JsonDocument.Parse(File.ReadAllText(path));
                if (!manifest.RootElement.TryGetProperty("path", out var host) || !File.Exists(host.GetString())) return "HostBroken";
                return "HostInstalled";
            }
            return "HostMissing";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or System.Security.SecurityException or ArgumentException)
        { return "HostBroken"; }
    }
}
