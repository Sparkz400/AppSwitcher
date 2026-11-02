namespace AppSwitcher.Services;

public enum ValidationSeverity
{
    Error,
    Warning
}

public sealed record ValidationIssue(string Subject, string Message, ValidationSeverity Severity)
{
    public bool IsWarning => Severity == ValidationSeverity.Warning;
}

public sealed class ConfigValidationResult
{
    public List<ValidationIssue> Errors { get; } = [];
    public List<ValidationIssue> Warnings { get; } = [];

    public IEnumerable<ValidationIssue> AllIssues => Errors.Concat(Warnings);
    public bool HasErrors => Errors.Count > 0;

    public void AddError(string subject, string message) =>
        Errors.Add(new ValidationIssue(subject, message, ValidationSeverity.Error));

    public void AddWarning(string subject, string message) =>
        Warnings.Add(new ValidationIssue(subject, message, ValidationSeverity.Warning));
}

public class ConfigValidationService
{
    private readonly ILogger<ConfigValidationService> _logger;

    public ConfigValidationService(ILogger<ConfigValidationService> logger)
    {
        _logger = logger;
    }

    public ConfigValidationResult Validate(ConfigRoot config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var result = new ConfigValidationResult();
        var settings = config.Settings ?? new AppSettings();
        var apps = config.Apps ?? [];
        var groups = config.Groups ?? [];
        var layouts = config.Layouts ?? [];

        AddDuplicateAppNameIssues(apps, result);
        foreach (var duplicate in apps.GroupBy(a => a.AppId, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            result.AddError(duplicate.First().Name, LocalizationService.T("Browser_InvalidIdentity"));
        AddDuplicateGroupIssues(groups, result);
        AddDuplicateLayoutIssues(layouts, result);
        AddGlobalSettingIssues(settings, result);
        ValidateDisplayIdentity(settings.PreferredMonitorId, LocalizationService.T("Settings_PreferredMonitor"), result);
        foreach (var layout in layouts)
            foreach (var position in layout.Positions ?? [])
                ValidateDisplayIdentity(position.MonitorDeviceId, layout.Name, result);

        foreach (var group in groups)
        {
            if (string.IsNullOrWhiteSpace(group.Key))
                result.AddError(LocalizationService.T("Settings_Groups"), LocalizationService.T("Diag_EmptyGroupKey"));
        }

        var groupKeys = groups
            .Where(group => !string.IsNullOrWhiteSpace(group.Key))
            .Select(group => group.Key.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var appsByName = apps
            .Where(app => !string.IsNullOrWhiteSpace(app.Name))
            .GroupBy(app => app.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        ValidateOperatorScope(settings.OperatorScope, appsByName.Keys, groupKeys, result);

        foreach (var app in apps)
        {
            var appName = GetAppSubject(app);
            ValidateDisplayIdentity(app.ForcedMonitorId, appName, result);
            foreach (var issue in WebsiteConfiguration.Validate(app, settings)) result.AddError(appName, issue);
            if (WebsiteConfiguration.IsLegacyWebsite(app)) result.AddWarning(appName, LocalizationService.T("Browser_ConvertRequired"));

            if (string.IsNullOrWhiteSpace(app.Name))
                result.AddError(appName, LocalizationService.T("Diag_EmptyAppName"));

            if (!app.IsWebsite && !string.IsNullOrWhiteSpace(app.Path))
            {
                var resolved = ResolveExecutablePath(app.Path);
                if (resolved == null)
                    result.AddWarning(appName, LocalizationService.T("Diag_ExeNotFound", app.Path));
            }
            else if (!app.IsWebsite)
            {
                result.AddError(appName, LocalizationService.T("Diag_NoExePath"));
            }

            ValidateTimeFormat(app.ScheduleTime, nameof(AppItem.ScheduleTime), appName, result);
            ValidateTimeFormat(app.ScheduleRestartTime, nameof(AppItem.ScheduleRestartTime), appName, result);
            ValidateTimeFormat(app.ScheduleShutdownTime, nameof(AppItem.ScheduleShutdownTime), appName, result);
            if (!Enum.IsDefined(app.Readiness) || app.ReadinessTimeoutSeconds is < 1 or > 600
                || (app.Readiness == ReadinessMode.Window && app.IsWebsite)
                || (app.Readiness == ReadinessMode.Http && !AppReadinessService.IsValidHealthUrl(app.HealthCheckUrl)))
                result.AddError(appName, LocalizationService.T("Readiness_Invalid"));
            ValidateNonNegative(app.MaxRestarts, nameof(AppItem.MaxRestarts), appName, result);
            ValidateNonNegative(app.LaunchDelay, nameof(AppItem.LaunchDelay), appName, result);
            ValidateNonNegative(app.MemoryThresholdMB, nameof(AppItem.MemoryThresholdMB), appName, result);

            if (app.ForcedMonitor < 0)
            {
                result.AddError(
                    appName,
                    LocalizationService.T("Diag_InvalidForcedMonitor", app.ForcedMonitor));
            }

            if (!Enum.IsDefined(typeof(WindowPlacementMode), app.PlacementMode))
            {
                result.AddError(
                    appName,
                    LocalizationService.T("Diag_InvalidWindowPlacementMode", app.PlacementMode));
            }

            if (!string.IsNullOrWhiteSpace(app.DependsOn))
            {
                var dependencyName = app.DependsOn.Trim();
                var normalizedName = app.Name?.Trim() ?? string.Empty;

                if (!string.IsNullOrEmpty(normalizedName)
                    && dependencyName.Equals(normalizedName, StringComparison.OrdinalIgnoreCase))
                {
                    result.AddError(appName, LocalizationService.T("Diag_SelfDependency", dependencyName));
                }
                else if (!appsByName.ContainsKey(dependencyName))
                {
                    result.AddError(appName, LocalizationService.T("Diag_DependencyNotFound", dependencyName));
                }
            }

            if (!string.IsNullOrWhiteSpace(app.HealthCheckUrl))
            {
                if (!Uri.TryCreate(app.HealthCheckUrl, UriKind.Absolute, out var uri)
                    || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                {
                    result.AddWarning(appName, LocalizationService.T("Diag_InvalidHealthUrl", app.HealthCheckUrl));
                }
            }

            if (!string.IsNullOrWhiteSpace(app.CustomIconPath) && !File.Exists(app.CustomIconPath))
                result.AddWarning(appName, LocalizationService.T("Diag_IconNotFound", app.CustomIconPath));

            if (!string.IsNullOrWhiteSpace(app.WorkingDirectory) && !Directory.Exists(app.WorkingDirectory))
                result.AddWarning(appName, LocalizationService.T("Diag_WorkDirNotFound", app.WorkingDirectory));

            ValidateHook(app.PreLaunchHookPath, app.PreLaunchHookArgs, "Settings_PreLaunchHook", appName, result);
            ValidateHook(app.PostLaunchHookPath, app.PostLaunchHookArgs, "Settings_PostLaunchHook", appName, result);
            ValidateHook(app.PreStopHookPath, app.PreStopHookArgs, "Settings_PreStopHook", appName, result);
            ValidateHook(app.PostStopHookPath, app.PostStopHookArgs, "Settings_PostStopHook", appName, result);
        }

        DetectCircularDependencies(appsByName, result);
        return result;
    }

    public void LogIssues(ConfigValidationResult result)
    {
        foreach (var issue in result.AllIssues)
        {
            if (issue.IsWarning)
                _logger.LogWarning("Config validation: [{Subject}] {Message}", issue.Subject, issue.Message);
            else
                _logger.LogError("Config validation: [{Subject}] {Message}", issue.Subject, issue.Message);
        }
    }

    public static string BuildSummary(IEnumerable<ValidationIssue> issues, int maxIssues = 3)
    {
        var selected = issues.Take(maxIssues).Select(issue => issue.Message).ToList();
        if (selected.Count == 0)
            return string.Empty;

        var remaining = issues.Skip(maxIssues).Count();
        return remaining > 0
            ? $"{string.Join("; ", selected)} (+{remaining})"
            : string.Join("; ", selected);
    }

    private static void AddDuplicateAppNameIssues(IEnumerable<AppItem> apps, ConfigValidationResult result)
    {
        foreach (var duplicate in apps
                     .Where(app => !string.IsNullOrWhiteSpace(app.Name))
                     .GroupBy(app => app.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Count() > 1))
        {
            result.AddError(
                duplicate.Key,
                LocalizationService.T("Diag_DuplicateAppName", duplicate.Key));
        }
    }

    private static void AddDuplicateGroupIssues(IEnumerable<GroupDefinition> groups, ConfigValidationResult result)
    {
        foreach (var duplicate in groups
                     .Where(group => !string.IsNullOrWhiteSpace(group.Key))
                     .GroupBy(group => group.Key.Trim(), StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Count() > 1))
        {
            result.AddError(
                duplicate.Key,
                LocalizationService.T("Diag_DuplicateGroupKey", duplicate.Key));
        }
    }

    private static void AddDuplicateLayoutIssues(IEnumerable<ScreenLayout> layouts, ConfigValidationResult result)
    {
        foreach (var duplicate in layouts
                     .Where(layout => !string.IsNullOrWhiteSpace(layout.Name))
                     .GroupBy(layout => layout.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Count() > 1))
        {
            result.AddError(
                duplicate.Key,
                LocalizationService.T("Diag_DuplicateLayoutName", duplicate.Key));
        }
    }

    private static void ValidateTimeFormat(
        string? time,
        string fieldName,
        string subject,
        ConfigValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(time))
            return;

        if (!SchedulerService.TryParseScheduledTime(time, out _))
            result.AddError(subject, LocalizationService.T("Diag_InvalidTimeFormat", fieldName, time));
    }

    private static void ValidateNonNegative(int value, string fieldName, string subject, ConfigValidationResult result)
    {
        if (value < 0)
            result.AddError(subject, LocalizationService.T("Diag_NegativeValue", fieldName, value));
    }

    private static void ValidateDisplayIdentity(string? identity, string subject, ConfigValidationResult result)
    {
        if (identity != null && (identity.Length > 512 || identity.Any(char.IsControl)))
            result.AddError(subject, LocalizationService.T("Display_InvalidIdentity"));
    }

    private static void AddGlobalSettingIssues(AppSettings settings, ConfigValidationResult result)
    {
        if (!SecurityService.IsSupportedHash(settings.AdminPasswordHash)
            || !SecurityService.IsSupportedHash(settings.OperatorPasswordHash))
            result.AddError(LocalizationService.T("Settings_OperatorAccess"), LocalizationService.T("Diag_InvalidPinHash"));
        ValidateNonNegative(settings.SessionTimeoutMinutes, nameof(AppSettings.SessionTimeoutMinutes), LocalizationService.T("Settings_SessionTimeout"), result);
        ValidateNonNegative(settings.MaxConfigBackups, nameof(AppSettings.MaxConfigBackups), LocalizationService.T("Settings_ConfigBackups"), result);

        if (settings.PreferredMonitor < 0)
        {
            result.AddError(
                LocalizationService.T("Settings_PreferredMonitor"),
                LocalizationService.T("Diag_InvalidPreferredMonitor", settings.PreferredMonitor));
        }
    }

    private static void ValidateOperatorScope(
        OperatorScopeSettings? scope,
        IEnumerable<string> appNames,
        IEnumerable<string> groupKeys,
        ConfigValidationResult result)
    {
        if (scope == null)
            return;

        var knownApps = appNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var knownGroups = groupKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var subject = LocalizationService.T("Settings_OperatorAccess");

        foreach (var appName in scope.AllowedAppNames)
        {
            if (!knownApps.Contains(appName))
            {
                result.AddError(
                    subject,
                    LocalizationService.T("Diag_OperatorScopeAppNotFound", appName));
            }
        }

        foreach (var groupKey in scope.AllowedGroupKeys)
        {
            if (!knownGroups.Contains(groupKey))
            {
                result.AddError(
                    subject,
                    LocalizationService.T("Diag_OperatorScopeGroupNotFound", groupKey));
            }
        }
    }

    private static void DetectCircularDependencies(
        IReadOnlyDictionary<string, AppItem> appsByName,
        ConfigValidationResult result)
    {
        var visited = new Dictionary<string, VisitState>(StringComparer.OrdinalIgnoreCase);
        var stack = new Stack<string>();
        var reportedCycles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var appName in appsByName.Keys)
            Visit(appName);

        void Visit(string appName)
        {
            if (visited.TryGetValue(appName, out var state))
            {
                if (state == VisitState.Visiting)
                {
                    var cycle = BuildCyclePath(stack, appName);
                    if (reportedCycles.Add(cycle))
                        result.AddError(appName, LocalizationService.T("Diag_CircularDependency", cycle));
                }
                return;
            }

            visited[appName] = VisitState.Visiting;
            stack.Push(appName);

            if (appsByName.TryGetValue(appName, out var app)
                && !string.IsNullOrWhiteSpace(app.DependsOn)
                && appsByName.ContainsKey(app.DependsOn.Trim()))
            {
                Visit(app.DependsOn.Trim());
            }

            stack.Pop();
            visited[appName] = VisitState.Visited;
        }
    }

    private static string BuildCyclePath(IEnumerable<string> stack, string repeatedNode)
    {
        var path = stack.Reverse().ToList();
        var startIndex = path.FindIndex(name => name.Equals(repeatedNode, StringComparison.OrdinalIgnoreCase));
        if (startIndex < 0)
            return repeatedNode;

        var cycle = path.Skip(startIndex).ToList();
        cycle.Add(repeatedNode);
        return string.Join(" -> ", cycle);
    }

    private static string GetAppSubject(AppItem app)
    {
        if (!string.IsNullOrWhiteSpace(app.Name))
            return app.Name.Trim();

        if (!string.IsNullOrWhiteSpace(app.Path))
            return app.Path.Trim();

        return LocalizationService.T("Msg_NewApp");
    }

    private static string? ResolveExecutablePath(string configuredPath)
    {
        if (Path.IsPathRooted(configuredPath) && File.Exists(configuredPath))
            return configuredPath;
        if (File.Exists(configuredPath))
            return Path.GetFullPath(configuredPath);

        var fileName = Path.HasExtension(configuredPath) ? configuredPath : $"{configuredPath}.exe";
        var pathEntries = Environment.GetEnvironmentVariable("PATH")?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (pathEntries == null)
            return null;

        foreach (var dir in pathEntries)
        {
            try
            {
                var candidate = Path.Combine(dir, fileName);
                if (File.Exists(candidate))
                    return candidate;
            }
            catch
            {
                // Ignore malformed PATH entries while validating.
            }
        }

        return null;
    }

    private static void ValidateHook(
        string? hookPath,
        string? hookArgs,
        string hookResourceKey,
        string subject,
        ConfigValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(hookPath))
        {
            if (!string.IsNullOrWhiteSpace(hookArgs))
            {
                result.AddError(
                    subject,
                    LocalizationService.T("Diag_HookArgsWithoutPath", LocalizationService.T(hookResourceKey)));
            }

            return;
        }

        if (LooksLikeLocalHookPath(hookPath) && !HookFileExists(hookPath))
        {
            result.AddWarning(
                subject,
                LocalizationService.T("Diag_HookNotFound", LocalizationService.T(hookResourceKey), hookPath));
        }
    }

    private static bool LooksLikeLocalHookPath(string hookPath)
    {
        if (Path.IsPathRooted(hookPath))
            return true;

        if (hookPath.StartsWith(".", StringComparison.Ordinal))
            return true;

        if (hookPath.Contains(Path.DirectorySeparatorChar) || hookPath.Contains(Path.AltDirectorySeparatorChar))
            return true;

        var extension = Path.GetExtension(hookPath);
        return extension.Equals(".ps1", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".bat", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HookFileExists(string hookPath)
    {
        try
        {
            return File.Exists(Path.GetFullPath(hookPath));
        }
        catch
        {
            return false;
        }
    }

    private enum VisitState
    {
        Visiting,
        Visited
    }
}
