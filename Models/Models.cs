namespace AppSwitcher.Models;

public enum DisplayMode
{
    Combined,   // Icon + Text
    IconOnly,   // Nur Icon
    TextOnly    // Nur Text
}

public enum AccessLevel
{
    None,
    Operator,
    Admin
}

public enum AppState
{
    Stopped,
    Running,
    Crashed,
    Restarting,
    Unhealthy,
    Recovering
}

public enum AppTargetKind { Native, Website }
public enum BrowserKind { Chrome, Edge, Firefox }
public enum ReadinessMode { None, Process, Window, Http }

public enum WindowPlacementMode
{
    None,
    Maximize,
    Fullscreen
}

public readonly record struct AppProcessTarget(string Path, string? WinPattern = null,
    string? WebsiteId = null, string? BrowserProfileId = null, BrowserKind Browser = BrowserKind.Edge)
{
    public bool IsWebsite => !string.IsNullOrWhiteSpace(WebsiteId);
    public string Key =>
        IsWebsite ? $"website:{WebsiteId}:{Browser}:{BrowserProfileId}".ToLowerInvariant()
        : $"{(Path ?? string.Empty).Trim()}|{(WinPattern ?? string.Empty).Trim()}".ToLowerInvariant();
}

public partial class KioskStatus : ObservableObject
{
    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private bool _keyboardHookInstalled;
    [ObservableProperty] private bool _taskbarHidden;
    [ObservableProperty] private bool _edgeSwipePolicyApplied;
    [ObservableProperty] private bool _keyboardFilterSupported;
    [ObservableProperty] private bool _keyboardFilterConfigured;
    [ObservableProperty] private bool _keyboardFilterRestartRequired;
    [ObservableProperty] private bool _enforcementLoopRunning;
    [ObservableProperty] private string _keyboardFilterStatusMessage = "";
    [ObservableProperty] private string _lastSelfHealAction = "";
    [ObservableProperty] private string _lastSelfHealResult = "";
    [ObservableProperty] private string _lastBlockedSurface = "";
    [ObservableProperty] private string _lastAllowedForeground = "";
    [ObservableProperty] private int _shellInterventionCount;
    [ObservableProperty] private string _limitedProtectionMessage = "";
    [ObservableProperty] private string _deploymentNotes = "";
}

public partial class OperatorScopeSettings : ObservableObject
{
    [ObservableProperty] private string[] _allowedAppNames = [];
    [ObservableProperty] private string[] _allowedGroupKeys = [];

    [JsonIgnore]
    public bool HasRestrictions => AllowedAppNames.Length > 0 || AllowedGroupKeys.Length > 0;

    public void Normalize()
    {
        AllowedAppNames = NormalizeEntries(AllowedAppNames);
        AllowedGroupKeys = NormalizeEntries(AllowedGroupKeys);
    }

    public void SetAllowedAppNames(IEnumerable<string>? names) => AllowedAppNames = NormalizeEntries(names);

    public void SetAllowedGroupKeys(IEnumerable<string>? keys) => AllowedGroupKeys = NormalizeEntries(keys);

    public void RemoveAppName(string? appName)
    {
        var normalizedName = NormalizeEntry(appName);
        if (string.IsNullOrEmpty(normalizedName))
            return;

        AllowedAppNames = AllowedAppNames
            .Where(name => !string.Equals(NormalizeEntry(name), normalizedName, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    public void RenameAppName(string? oldName, string? newName)
    {
        var normalizedOldName = NormalizeEntry(oldName);
        if (string.IsNullOrEmpty(normalizedOldName))
            return;

        var updated = AllowedAppNames
            .Select(name => string.Equals(NormalizeEntry(name), normalizedOldName, StringComparison.OrdinalIgnoreCase)
                ? newName
                : name);

        AllowedAppNames = NormalizeEntries(updated.OfType<string>());
    }

    public void RemoveGroupKey(string? groupKey)
    {
        var normalizedGroupKey = NormalizeEntry(groupKey);
        if (string.IsNullOrEmpty(normalizedGroupKey))
            return;

        AllowedGroupKeys = AllowedGroupKeys
            .Where(key => !string.Equals(NormalizeEntry(key), normalizedGroupKey, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    public static string[] NormalizeEntries(IEnumerable<string>? entries) =>
        (entries ?? [])
        .Select(NormalizeEntry)
        .Where(value => !string.IsNullOrEmpty(value))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public static string NormalizeEntry(string? value) => value?.Trim() ?? string.Empty;
}

public partial class AppSettings : ObservableObject
{
    [ObservableProperty] private double _buttonSize = 80;
    [ObservableProperty] private string _shape = "Hexagon";
    [ObservableProperty] private string _bgColor = "#2c3e50";
    [ObservableProperty] private string _fgColor = "#ecf0f1";
    [ObservableProperty] private string _iconStyle = "Hamburger";
    [ObservableProperty] private string _borderColor = "#3e4152";
    [ObservableProperty] private double _buttonOpacity = 1.0;
    [ObservableProperty] private bool _glowEnabled = false;
    [ObservableProperty] private double _menuWidth = 220;
    [ObservableProperty] private double _menuItemHeight = 50;
    [ObservableProperty] private bool _autoClose = true;
    [ObservableProperty] private double _posX = 100;
    [ObservableProperty] private double _posY = 100;
    [ObservableProperty] private bool _positionLocked = false;
    [ObservableProperty] private bool _kioskMode = false;

    // Security
    [ObservableProperty] private string _adminPasswordHash = "";
    [ObservableProperty] private string _operatorPasswordHash = "";
    [ObservableProperty] private OperatorScopeSettings _operatorScope = new();
    [ObservableProperty] private bool _configEncryption = false;

    // Session timeout: auto-lock after N minutes of inactivity (0 = disabled)
    [ObservableProperty] private int _sessionTimeoutMinutes = 0;

    [ObservableProperty] private bool _globalShortcutEnabled;

    // Theme
    [ObservableProperty] private string _theme = "Dark";

    // Language
    [ObservableProperty] private string _language = "de";

    // Config backup
    [ObservableProperty] private int _maxConfigBackups = 5;

    // Multi-monitor: 0 = auto (remember last position), 1+ = specific monitor index
    [ObservableProperty] private int _preferredMonitor = 0;
    [ObservableProperty] private string _preferredMonitorId = "";
}

public partial class AppItem : ObservableObject
{
    [ObservableProperty] private string _appId = Guid.NewGuid().ToString("N");
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWebsite))]
    [NotifyPropertyChangedFor(nameof(IsNative))]
    private AppTargetKind _targetKind;
    [ObservableProperty] private string _websiteUrl = "";
    [ObservableProperty] private BrowserKind _browser = BrowserKind.Edge;
    [ObservableProperty] private string _browserProfileId = "";
    [ObservableProperty] private string _browserProfileDirectory = "";
    [ObservableProperty] private string _browserExecutablePath = "";
    [JsonIgnore] public bool IsWebsite => TargetKind == AppTargetKind.Website;
    [JsonIgnore] public bool IsNative => !IsWebsite;
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _path = "";
    [ObservableProperty] private string _args = "";
    [ObservableProperty] private string _winPattern = "";
    [ObservableProperty] private string _bgColor = "#3498db";
    [ObservableProperty] private string _fgColor = "#ffffff";

    [ObservableProperty] private DisplayMode _mode = DisplayMode.Combined;
    [ObservableProperty] private string _customIconPath = "";

    // Group
    [ObservableProperty] private string _group = "";

    // Auto-launch
    [ObservableProperty] private bool _autoLaunch = false;

    // Watchdog
    [ObservableProperty] private bool _watchdogEnabled = false;
    [ObservableProperty] private int _maxRestarts = 3;

    // Working directory
    [ObservableProperty] private string _workingDirectory = "";

    // Lifecycle hooks
    [ObservableProperty] private string _preLaunchHookPath = "";
    [ObservableProperty] private string _preLaunchHookArgs = "";
    [ObservableProperty] private string _postLaunchHookPath = "";
    [ObservableProperty] private string _postLaunchHookArgs = "";
    [ObservableProperty] private string _preStopHookPath = "";
    [ObservableProperty] private string _preStopHookArgs = "";
    [ObservableProperty] private string _postStopHookPath = "";
    [ObservableProperty] private string _postStopHookArgs = "";

    // Dependencies: name of another app that must be running before this one launches
    [ObservableProperty] private string _dependsOn = "";

    // Delay in ms after dependency is satisfied before launching
    [ObservableProperty] private int _launchDelay = 0;

    [ObservableProperty] private ReadinessMode _readiness;
    [ObservableProperty] private int _readinessTimeoutSeconds = 30;

    // Scheduled daily launch time (HH:mm format), empty = disabled
    [ObservableProperty] private string _scheduleTime = "";

    // Scheduled restart time (HH:mm), empty = disabled
    [ObservableProperty] private string _scheduleRestartTime = "";

    // Scheduled shutdown time (HH:mm), empty = disabled
    [ObservableProperty] private string _scheduleShutdownTime = "";

    // HTTP health check URL (optional)
    [ObservableProperty] private string _healthCheckUrl = "";

    // Memory threshold in MB for alert (0 = disabled)
    [ObservableProperty] private int _memoryThresholdMB = 0;

    // Sort order within group
    [ObservableProperty] private int _sortOrder = 0;

    // Require confirmation before closing/killing
    [ObservableProperty] private bool _requireConfirmation = false;

    // Pin to top of menu (favorites)
    [ObservableProperty] private bool _isPinned = false;

    // Window policy
    [ObservableProperty] private WindowPlacementMode _placementMode = WindowPlacementMode.None;
    [ObservableProperty] private int _forcedMonitor = 0;
    [ObservableProperty] private string _forcedMonitorId = "";
    [ObservableProperty] private bool _alwaysOnTop = false;
    [ObservableProperty] private bool _reapplyPlacementContinuously = false;

    // === Runtime-only properties (not persisted) ===

    [property: JsonIgnore]
    [ObservableProperty, NotifyPropertyChangedFor(nameof(NeedsAttention))] private bool _isRunning;

    [property: JsonIgnore]
    [ObservableProperty, NotifyPropertyChangedFor(nameof(NeedsAttention))] private bool _isResponding = true;

    [property: JsonIgnore]
    [ObservableProperty, NotifyPropertyChangedFor(nameof(NeedsAttention))] private AppState _state = AppState.Stopped;

    [property: JsonIgnore]
    [ObservableProperty] private string _healthStatus = "";

    [property: JsonIgnore]
    [ObservableProperty] private double _cpuPercent;

    [property: JsonIgnore]
    [ObservableProperty] private double _memoryMB;

    [property: JsonIgnore]
    [ObservableProperty] private DateTime? _lastStartTime;

    [property: JsonIgnore]
    [ObservableProperty] private int _crashCountToday;

    [property: JsonIgnore]
    [ObservableProperty] private int _totalRestarts;

    [property: JsonIgnore]
    [ObservableProperty, NotifyPropertyChangedFor(nameof(NeedsAttention))] private bool _requiresWatchdogAcknowledgement;

    [property: JsonIgnore]
    [ObservableProperty] private DateTime? _watchdogNextRestartAt;

    [property: JsonIgnore]
    [ObservableProperty] private string _watchdogStatusText = "";

    [JsonIgnore]
    public bool NeedsAttention => RequiresWatchdogAcknowledgement || (IsRunning && !IsResponding)
        || State is AppState.Crashed or AppState.Restarting or AppState.Unhealthy or AppState.Recovering;

    [JsonIgnore]
    public AppProcessTarget ProcessTarget => IsWebsite
        ? new(WebsiteUrl, null, AppId, BrowserProfileId, Browser)
        : new(Path, WinPattern);
}

public class ScreenLayout
{
    public string Name { get; set; } = "";
    public List<WindowPosition> Positions { get; set; } = [];
    public List<string> AppIds { get; set; } = [];
    public List<string> GroupKeys { get; set; } = [];
}

public class WindowPosition
{
    public string AppName { get; set; } = "";
    public string AppId { get; set; } = "";
    public double Left { get; set; }
    public double Top { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public int MonitorIndex { get; set; }
    public string MonitorDeviceId { get; set; } = "";
    public double SavedMonitorLeft { get; set; }
    public double SavedMonitorTop { get; set; }
    public double SavedMonitorWidth { get; set; }
    public double SavedMonitorHeight { get; set; }
}

public class GroupDefinition
{
    public string Key { get; set; } = "";
    public Dictionary<string, string> Names { get; set; } = new();

    [JsonIgnore]
    public string CurrentDisplayName { get; set; } = "";

    /// <summary>
    /// Observable wrapper entries for two-way binding in the settings UI.
    /// Built dynamically from SupportedLanguages so any language is supported.
    /// </summary>
    [JsonIgnore]
    public ObservableCollection<GroupNameEntry> NameEntries { get; set; } = [];

    public string GetDisplayName(string language) =>
        Names.TryGetValue(language, out var name) && !string.IsNullOrEmpty(name) ? name : Key;

    public void UpdateDisplayName(string language)
    {
        CurrentDisplayName = string.IsNullOrEmpty(Key) ? "—" : GetDisplayName(language);
    }

    /// <summary>
    /// Builds NameEntries from the Names dictionary for all supported languages.
    /// Call this after deserialization to prepare for UI binding.
    /// </summary>
    public void BuildNameEntries(IEnumerable<LocalizationService.LanguageInfo> supportedLanguages)
    {
        NameEntries.Clear();
        foreach (var lang in supportedLanguages)
        {
            var entry = new GroupNameEntry
            {
                LanguageCode = lang.Code,
                LanguageLabel = lang.Code.ToUpperInvariant(),
                Name = Names.GetValueOrDefault(lang.Code, "")
            };
            // Sync changes back to the Names dictionary
            entry.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(GroupNameEntry.Name))
                    Names[entry.LanguageCode] = entry.Name;
            };
            NameEntries.Add(entry);
        }
    }
}

/// <summary>
/// Bindable wrapper for a single language entry in a group's name dictionary.
/// </summary>
public partial class GroupNameEntry : ObservableObject
{
    [ObservableProperty] private string _languageCode = "";
    [ObservableProperty] private string _languageLabel = "";
    [ObservableProperty] private string _name = "";
}

/// <summary>
/// Handles deserialization of Groups from both legacy format (string array)
/// and new format (GroupDefinition array) for backward compatibility.
/// </summary>
public class GroupListJsonConverter : JsonConverter<List<GroupDefinition>>
{
    public override List<GroupDefinition>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType is JsonTokenType.Null)
            return [];

        if (reader.TokenType != JsonTokenType.StartArray)
            return [];

        var result = new List<GroupDefinition>();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray)
                break;

            if (reader.TokenType == JsonTokenType.String)
            {
                // Legacy format: plain string → auto-convert
                var name = reader.GetString() ?? "";
                result.Add(new GroupDefinition
                {
                    Key = name,
                    Names = new Dictionary<string, string> { ["de"] = name, ["en"] = name }
                });
            }
            else if (reader.TokenType == JsonTokenType.StartObject)
            {
                // New format: GroupDefinition object
                var def = JsonSerializer.Deserialize<GroupDefinition>(ref reader, options);
                if (def != null) result.Add(def);
            }
        }

        return result;
    }

    public override void Write(Utf8JsonWriter writer, List<GroupDefinition> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var item in value)
            JsonSerializer.Serialize(writer, item, options);
        writer.WriteEndArray();
    }
}

public class ConfigRoot
{
    public int Version { get; set; } = 11;
    public AppSettings Settings { get; set; } = new();

    [JsonConverter(typeof(GroupListJsonConverter))]
    public List<GroupDefinition> Groups { get; set; } = [];

    public List<AppItem> Apps { get; set; } = [];
    public List<ScreenLayout> Layouts { get; set; } = [];
}

internal static class AppJsonOptions
{
    public static readonly JsonSerializerOptions Default = new()
    {
        WriteIndented = true
    };
}
