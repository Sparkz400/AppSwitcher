namespace AppSwitcher.Services;

/// <summary>
/// Compares two ConfigRoot snapshots and produces a human-readable list of changes.
/// Used during hot-reload to log what specifically changed.
/// </summary>
public sealed record ConfigDiffChange(string Key, string Description);

public sealed class ConfigDiffResult
{
    public static ConfigDiffResult Empty { get; } = new([]);

    public IReadOnlyList<ConfigDiffChange> Changes { get; }
    public bool HasChanges => Changes.Count > 0;

    public ConfigDiffResult(IReadOnlyList<ConfigDiffChange> changes)
    {
        Changes = changes;
    }

    public string BuildSummary(int maxItems = 3)
    {
        if (Changes.Count == 0)
            return string.Empty;

        var take = Math.Max(1, maxItems);
        var summary = string.Join(", ", Changes.Take(take).Select(change => change.Description));
        return Changes.Count > take ? $"{summary} (+{Changes.Count - take})" : summary;
    }

    public string BuildDetails(string separator = "; ") =>
        string.Join(separator, Changes.Select(change => change.Description));

    public ConfigDiffResult WithoutKeys(IEnumerable<string> suppressedKeys)
    {
        ArgumentNullException.ThrowIfNull(suppressedKeys);

        var suppressed = suppressedKeys
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (suppressed.Count == 0)
            return this;

        return new ConfigDiffResult(
            Changes
                .Where(change => !suppressed.Contains(change.Key))
                .ToList());
    }
}

public class ConfigDiffService
{
    private readonly ILogger<ConfigDiffService> _logger;

    public ConfigDiffService(ILogger<ConfigDiffService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Computes and logs differences between old and new config.
    /// Returns a summary string for notification display.
    /// </summary>
    public string ComputeAndLog(ConfigRoot oldConfig, ConfigRoot newConfig)
    {
        var result = Compute(oldConfig, newConfig);
        Log(result);
        return result.BuildSummary();
    }

    public ConfigDiffResult Compute(ConfigRoot oldConfig, ConfigRoot newConfig)
    {
        ArgumentNullException.ThrowIfNull(oldConfig);
        ArgumentNullException.ThrowIfNull(newConfig);

        var changes = new List<ConfigDiffChange>();

        DiffSettings(oldConfig.Settings, newConfig.Settings, changes);
        DiffGroups(oldConfig.Groups, newConfig.Groups, changes);
        DiffApps(oldConfig.Apps, newConfig.Apps, changes);
        if (JsonSerializer.Serialize(oldConfig.Layouts) != JsonSerializer.Serialize(newConfig.Layouts))
            changes.Add(new("layouts", "Saved workspaces or window positions changed"));

        return changes.Count == 0 ? ConfigDiffResult.Empty : new ConfigDiffResult(changes);
    }

    public void Log(ConfigDiffResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (!result.HasChanges)
        {
            _logger.LogInformation("Config hot-reload: no meaningful changes detected");
            return;
        }

        foreach (var change in result.Changes)
            _logger.LogInformation("Config change: {Change}", change.Description);
    }

    private static void DiffSettings(AppSettings oldS, AppSettings newS, List<ConfigDiffChange> changes)
    {
        if (oldS.GlobalShortcutEnabled != newS.GlobalShortcutEnabled)
            changes.Add(new("settings.globalshortcutenabled", "Launcher shortcut changed"));
        if (oldS.KioskMode != newS.KioskMode)
            changes.Add(new("settings.kioskmode", $"KioskMode: {oldS.KioskMode} → {newS.KioskMode}"));
        if (oldS.Theme != newS.Theme)
            changes.Add(new("settings.theme", $"Theme: {oldS.Theme} → {newS.Theme}"));
        if (oldS.Language != newS.Language)
            changes.Add(new("settings.language", $"Language: {oldS.Language} → {newS.Language}"));
        if (oldS.ConfigEncryption != newS.ConfigEncryption)
            changes.Add(new("settings.configencryption", $"ConfigEncryption: {oldS.ConfigEncryption} → {newS.ConfigEncryption}"));
        if (oldS.PositionLocked != newS.PositionLocked)
            changes.Add(new("settings.positionlocked", $"PositionLocked: {oldS.PositionLocked} → {newS.PositionLocked}"));
        if (oldS.Shape != newS.Shape)
            changes.Add(new("settings.shape", $"Shape: {oldS.Shape} → {newS.Shape}"));
        if (Math.Abs(oldS.ButtonSize - newS.ButtonSize) > 0.5)
            changes.Add(new("settings.buttonsize", $"ButtonSize: {oldS.ButtonSize:0} → {newS.ButtonSize:0}"));
        if (oldS.BgColor != newS.BgColor)
            changes.Add(new("settings.bgcolor", $"BgColor: {oldS.BgColor} → {newS.BgColor}"));
        if (oldS.FgColor != newS.FgColor) changes.Add(new("settings.fgcolor", "Launcher foreground color changed"));
        if (oldS.IconStyle != newS.IconStyle) changes.Add(new("settings.iconstyle", "Launcher icon changed"));
        if (oldS.BorderColor != newS.BorderColor) changes.Add(new("settings.bordercolor", "Launcher border color changed"));
        if (oldS.ButtonOpacity != newS.ButtonOpacity) changes.Add(new("settings.buttonopacity", "Launcher opacity changed"));
        if (oldS.GlowEnabled != newS.GlowEnabled) changes.Add(new("settings.glowenabled", "Launcher glow changed"));
        if (oldS.MenuWidth != newS.MenuWidth) changes.Add(new("settings.menuwidth", "Launcher menu width changed"));
        if (oldS.MenuItemHeight != newS.MenuItemHeight) changes.Add(new("settings.menuitemheight", "Launcher row height changed"));
        if (oldS.AutoClose != newS.AutoClose) changes.Add(new("settings.autoclose", "Launcher auto-close changed"));
        if (oldS.PosX != newS.PosX || oldS.PosY != newS.PosY) changes.Add(new("settings.position", "Launcher position changed"));
        if (oldS.MaxConfigBackups != newS.MaxConfigBackups) changes.Add(new("settings.maxconfigbackups", "Backup retention changed"));
        if (oldS.PreferredMonitor != newS.PreferredMonitor || oldS.PreferredMonitorId != newS.PreferredMonitorId)
            changes.Add(new("settings.preferredmonitor", "Preferred monitor selection changed"));
        if (oldS.SessionTimeoutMinutes != newS.SessionTimeoutMinutes)
            changes.Add(new("settings.sessiontimeoutminutes", $"SessionTimeout: {oldS.SessionTimeoutMinutes} → {newS.SessionTimeoutMinutes}"));
        if (oldS.AdminPasswordHash != newS.AdminPasswordHash)
            changes.Add(new("settings.adminpasswordhash", "AdminPIN changed"));
        if (oldS.OperatorPasswordHash != newS.OperatorPasswordHash)
            changes.Add(new("settings.operatorpasswordhash", "OperatorPIN changed"));
        if (oldS.OperatorScope.Restricted != newS.OperatorScope.Restricted
            || !oldS.OperatorScope.AllowedAppNames.OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .SequenceEqual(newS.OperatorScope.AllowedAppNames.OrderBy(name => name, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase)
            || !oldS.OperatorScope.AllowedGroupKeys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
                .SequenceEqual(newS.OperatorScope.AllowedGroupKeys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase))
        {
            changes.Add(new("settings.operatorscope", "OperatorScope changed"));
        }
    }

    private static void DiffGroups(List<GroupDefinition> oldGroups, List<GroupDefinition> newGroups, List<ConfigDiffChange> changes)
    {
        var oldKeys = oldGroups.Select(g => g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var newKeys = newGroups.Select(g => g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var key in newKeys.Except(oldKeys))
            changes.Add(new($"group.added:{key}", $"Group added: {key}"));
        foreach (var key in oldKeys.Except(newKeys))
            changes.Add(new($"group.removed:{key}", $"Group removed: {key}"));
        foreach (var key in oldKeys.Intersect(newKeys))
            if (JsonSerializer.Serialize(oldGroups.First(group => group.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Names)
                != JsonSerializer.Serialize(newGroups.First(group => group.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Names))
                changes.Add(new($"group.names:{key}", $"Group labels changed: {key}"));
    }

    private static void DiffApps(List<AppItem> oldApps, List<AppItem> newApps, List<ConfigDiffChange> changes)
    {
        foreach (var duplicate in FindDuplicateNames(oldApps))
            changes.Add(new($"app.duplicate.old:{duplicate}", $"Duplicate app name (old config): {duplicate}"));

        foreach (var duplicate in FindDuplicateNames(newApps))
            changes.Add(new($"app.duplicate.new:{duplicate}", $"Duplicate app name (new config): {duplicate}"));

        var oldByName = oldApps
            .Where(app => !string.IsNullOrWhiteSpace(app.Name))
            .GroupBy(app => app.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var newByName = newApps
            .Where(app => !string.IsNullOrWhiteSpace(app.Name))
            .GroupBy(app => app.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var name in newByName.Keys.Except(oldByName.Keys, StringComparer.OrdinalIgnoreCase))
            changes.Add(new($"app.added:{name}", $"App added: {name}"));

        foreach (var name in oldByName.Keys.Except(newByName.Keys, StringComparer.OrdinalIgnoreCase))
            changes.Add(new($"app.removed:{name}", $"App removed: {name}"));

        foreach (var name in oldByName.Keys.Intersect(newByName.Keys, StringComparer.OrdinalIgnoreCase))
        {
            var oldApp = oldByName[name];
            var newApp = newByName[name];
            AddAppPropertyChange(changes, name, "Path", oldApp.Path != newApp.Path);
            AddAppPropertyChange(changes, name, "Arguments", oldApp.Args != newApp.Args);
            AddAppPropertyChange(changes, name, "WindowTitle", oldApp.WinPattern != newApp.WinPattern);
            AddAppPropertyChange(changes, name, "WorkingDirectory", oldApp.WorkingDirectory != newApp.WorkingDirectory);
            AddAppPropertyChange(changes, name, "LaunchDelay", oldApp.LaunchDelay != newApp.LaunchDelay);
            AddAppPropertyChange(changes, name, "MaxRestarts", oldApp.MaxRestarts != newApp.MaxRestarts);
            AddAppPropertyChange(changes, name, "AutoLaunch", oldApp.AutoLaunch != newApp.AutoLaunch);
            AddAppPropertyChange(changes, name, "Watchdog", oldApp.WatchdogEnabled != newApp.WatchdogEnabled);
            AddAppPropertyChange(changes, name, "Website", oldApp.TargetKind != newApp.TargetKind || oldApp.WebsiteUrl != newApp.WebsiteUrl
                || oldApp.AppId != newApp.AppId || oldApp.Browser != newApp.Browser || oldApp.BrowserProfileId != newApp.BrowserProfileId
                || oldApp.BrowserProfileDirectory != newApp.BrowserProfileDirectory || oldApp.BrowserExecutablePath != newApp.BrowserExecutablePath);
            AddAppPropertyChange(changes, name, "Group", oldApp.Group != newApp.Group);
            AddAppPropertyChange(changes, name, "Schedule", oldApp.ScheduleTime != newApp.ScheduleTime);
            AddAppPropertyChange(changes, name, "ScheduleRestart", oldApp.ScheduleRestartTime != newApp.ScheduleRestartTime);
            AddAppPropertyChange(changes, name, "ScheduleShutdown", oldApp.ScheduleShutdownTime != newApp.ScheduleShutdownTime);
            AddAppPropertyChange(changes, name, "DependsOn", oldApp.DependsOn != newApp.DependsOn);
            AddAppPropertyChange(changes, name, "Readiness", oldApp.Readiness != newApp.Readiness || oldApp.ReadinessTimeoutSeconds != newApp.ReadinessTimeoutSeconds);
            AddAppPropertyChange(changes, name, "HealthCheck", oldApp.HealthCheckUrl != newApp.HealthCheckUrl);
            AddAppPropertyChange(changes, name, "MemoryThreshold", oldApp.MemoryThresholdMB != newApp.MemoryThresholdMB);
            AddAppPropertyChange(changes, name, "RequireConfirmation", oldApp.RequireConfirmation != newApp.RequireConfirmation);
            AddAppPropertyChange(changes, name, "IsPinned", oldApp.IsPinned != newApp.IsPinned);
            AddAppPropertyChange(changes, name, "PlacementMode", oldApp.PlacementMode != newApp.PlacementMode);
            AddAppPropertyChange(changes, name, "ForcedMonitor", oldApp.ForcedMonitor != newApp.ForcedMonitor || oldApp.ForcedMonitorId != newApp.ForcedMonitorId);
            AddAppPropertyChange(changes, name, "AlwaysOnTop", oldApp.AlwaysOnTop != newApp.AlwaysOnTop);
            AddAppPropertyChange(changes, name, "ReapplyPlacementContinuously", oldApp.ReapplyPlacementContinuously != newApp.ReapplyPlacementContinuously);
            AddAppPropertyChange(
                changes,
                name,
                "PreLaunchHook",
                oldApp.PreLaunchHookPath != newApp.PreLaunchHookPath || oldApp.PreLaunchHookArgs != newApp.PreLaunchHookArgs);
            AddAppPropertyChange(
                changes,
                name,
                "PostLaunchHook",
                oldApp.PostLaunchHookPath != newApp.PostLaunchHookPath || oldApp.PostLaunchHookArgs != newApp.PostLaunchHookArgs);
            AddAppPropertyChange(
                changes,
                name,
                "PreStopHook",
                oldApp.PreStopHookPath != newApp.PreStopHookPath || oldApp.PreStopHookArgs != newApp.PreStopHookArgs);
            AddAppPropertyChange(
                changes,
                name,
                "PostStopHook",
                oldApp.PostStopHookPath != newApp.PostStopHookPath || oldApp.PostStopHookArgs != newApp.PostStopHookArgs);
            AddAppPropertyChange(changes, name, "SortOrder", oldApp.SortOrder != newApp.SortOrder);
        }
    }

    private static void AddAppPropertyChange(List<ConfigDiffChange> changes, string appName, string property, bool hasChanged)
    {
        if (!hasChanged)
            return;

        changes.Add(new(
            $"app.modified:{appName}:{property.ToLowerInvariant()}",
            $"App '{appName}' modified: {property}"));
    }

    private static IEnumerable<string> FindDuplicateNames(IEnumerable<AppItem> apps) =>
        apps.Where(app => !string.IsNullOrWhiteSpace(app.Name))
            .GroupBy(app => app.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key);
}
