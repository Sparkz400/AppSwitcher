namespace AppSwitcher.ViewModels;

public sealed record SettingsSearchResult(string LabelKey, string Section, int Tab, string PropertyPath, string Keywords, bool AdminOnly = false)
{
    public string Label => LocalizationService.T(LabelKey);
    public string SectionLabel => LocalizationService.T("UI_" + Section);
}

public partial class SettingsViewModel
{
    [ObservableProperty] private string _settingsSearch = "";
    public ObservableCollection<SettingsSearchResult> SearchResults { get; } = [];
    public bool HasSettingsSearch => !string.IsNullOrWhiteSpace(SettingsSearch);
    public event Action<SettingsSearchResult>? NavigateToSetting;

    internal static readonly SettingsSearchResult[] SearchCatalog =
    [
        new("UI_Setup", "Setup", 0, "", "setup checklist first start einrichtung checkliste erster start", true),
        new("Browser_TargetKind", "Applications", 0, "SelectedApp.TargetKind", "website native type typ anwendung webseite"),
        new("Browser_Url", "Applications", 0, "SelectedApp.WebsiteUrl", "url address adresse http https ip"),
        new("Browser_Profile", "Applications", 0, "SelectedBrowserProfile", "chrome edge firefox profile profil browser tab"),
        new("Browser_Executable", "Applications", 0, "SelectedApp.BrowserExecutablePath", "browser exe startup start"),
        new("Settings_ExePath", "Applications", 0, "SelectedApp.Path", "exe path pfad program programm"),
        new("Settings_WorkDir", "Applications", 0, "SelectedApp.WorkingDirectory", "directory folder ordner verzeichnis"),
        new("Browser_ProfileId", "Applications", 0, "SelectedApp.BrowserProfileId", "profile identity profil id"),
        new("Browser_ProfileDirectory", "Applications", 0, "SelectedApp.BrowserProfileDirectory", "browser folder ordner verzeichnis"),
        new("UI_Startup", "Applications", 1, "SelectedApp.AutoLaunch", "autostart startup start automatisch"),
        new("UI_DailySchedules", "Applications", 1, "SelectedApp.ScheduleTime", "schedule daily time zeitplan täglich uhrzeit start", true),
        new("Settings_ScheduleRestartTime", "Applications", 1, "SelectedApp.ScheduleRestartTime", "schedule restart zeitplan neustart", true),
        new("Settings_ScheduleShutdownTime", "Applications", 1, "SelectedApp.ScheduleShutdownTime", "schedule stop shutdown zeitplan beenden", true),
        new("Readiness_Title", "Applications", 1, "SelectedApp.Readiness", "readiness ready bereitschaft bereit", true),
        new("Settings_DependsOn", "Applications", 1, "SelectedApp.DependsOn", "dependency order abhängig reihenfolge zuerst", true),
        new("Settings_LaunchDelay", "Applications", 1, "SelectedApp.LaunchDelay", "delay wait verzögerung warten", true),
        new("UI_Recovery", "Applications", 2, "SelectedApp.WatchdogEnabled", "watchdog crash restart absturz neustart überwachung"),
        new("UI_WindowPlacement", "Applications", 3, "AppMonitorSelection", "monitor screen bildschirm position maximize maximieren fullscreen vollbild"),
        new("Settings_HealthCheckUrl", "Applications", 2, "SelectedApp.HealthCheckUrl", "health http gesundheit check connection verbindung"),
        new("Settings_MemoryThreshold", "Applications", 2, "SelectedApp.MemoryThresholdMB", "memory threshold alert ram speicher warnung grenzwert", true),
        new("UI_Advanced", "Applications", 4, "SelectedApp.Args", "arguments argumente icon symbol color farbe"),
        new("UI_LifecycleScripts", "Applications", 4, "SelectedApp.PreLaunchHookPath", "script hook lifecycle skript before pre vorher start", true),
        new("Settings_PostLaunchHook", "Applications", 4, "SelectedApp.PostLaunchHookPath", "script hook skript after post nach start", true),
        new("Settings_PreStopHook", "Applications", 4, "SelectedApp.PreStopHookPath", "script hook skript before pre vorher stop", true),
        new("Settings_PostStopHook", "Applications", 4, "SelectedApp.PostStopHookPath", "script hook skript after post nach stop", true),
        new("UI_Launcher", "System", 0, "Settings.ButtonSize", "size größe button taste launcher starter"),
        new("Settings_Shape", "System", 0, "Settings.Shape", "shape form kreis hexagon"),
        new("Shortcut_Enable", "System", 0, "Settings.GlobalShortcutEnabled", "shortcut hotkey tastatur tastenkürzel strg ctrl space"),
        new("UI_RestartLauncher", "System", 0, "RestartLauncherCommand", "restart appswitcher neu starten", true),
        new("Support_Title", "System", 0, "PreviewSupportReportCommand", "support report bericht diagnose", true),
        new("UI_Appearance", "System", 1, "Settings.Theme", "theme dark light dunkel hell appearance darstellung design"),
        new("Settings_Language", "System", 1, "Settings.Language", "language sprache deutsch english german"),
        new("UI_Groups", "System", 2, "", "groups gruppen sort order reihenfolge"),
        new("UI_Backups", "System", 3, "", "backup restore import export config sicherung wiederherstellen konfiguration", true),
        new("UI_Security", "Access", 0, "Settings.SessionTimeoutMinutes", "pin password passwort timeout sitzung zugriff operator", true),
        new("UI_Kiosk", "Access", 1, "Settings.KioskMode", "kiosk lock sperren windows keyboard tastatur", true),
        new("UI_Monitoring", "Monitoring", 0, "", "monitor health status cpu memory speicher pause wartung maintenance"),
        new("UI_Layouts", "Layouts", 0, "", "layout workspace arbeitsbereich fenster monitor position"),
        new("UI_Audit", "Audit", 0, "", "audit log history protokoll verlauf", true),
        new("UI_Browsers", "Browsers", 0, "", "browser chrome edge firefox connection verbindung extension erweiterung profile profil", true)
    ];

    partial void OnSettingsSearchChanged(string value)
    {
        SearchResults.Clear();
        OnPropertyChanged(nameof(HasSettingsSearch));
        var words = value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0) return;
        foreach (var entry in SearchCatalog.Where(entry => !entry.AdminOnly || IsAdmin))
        {
            var haystack = entry.Label + " " + entry.SectionLabel + " " + entry.Keywords;
            if (words.All(word => haystack.Contains(word, StringComparison.CurrentCultureIgnoreCase))) SearchResults.Add(entry);
        }
    }

    [RelayCommand]
    private void OpenSearchResult(SettingsSearchResult? result)
    {
        if (result == null || (result.AdminOnly && !IsAdmin)) return;
        if (result.Section == "Applications" && SelectedApp == null) SelectedApp = VisibleApps.FirstOrDefault();
        ActiveSection = result.Section;
        SettingsSearch = "";
        NavigateToSetting?.Invoke(result);
    }
}
