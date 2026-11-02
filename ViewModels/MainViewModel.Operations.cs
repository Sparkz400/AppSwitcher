using System.Collections.Concurrent;

namespace AppSwitcher.ViewModels;

public partial class MainViewModel
{
    private readonly ConcurrentDictionary<string, Task> _watchdogChecks = new();
    private readonly object _watchdogCheckGate = new();
    private readonly ResponsivenessMonitor _responsiveness = new();
    private readonly CancellationTokenSource _autoLaunchLifetime = new();
    private Task? _autoLaunchTask;
    private int _disposed;
    private Dictionary<string, bool>? _unfilteredExpansion;
    [ObservableProperty] private string _quickFilter = "";
    [ObservableProperty] private string _shortcutStatus = "";
    public bool HasLauncherMatches => PinnedApps.Count > 0 || GroupedApps.Any(g => g.Apps.Count > 0);
    public bool IsTelemetryVisible => IsMenuOpen || _dashboardWindow != null || _settingsWindow != null;
    public bool IsMaintenancePaused => Maintenance?.IsPaused == true;
    public string MaintenanceStatus => Maintenance?.Status ?? LocalizationService.T("Maintenance_Inactive");
    public bool CanAdminister => CurrentAccessLevel == AccessLevel.Admin;

    [RelayCommand]
    private void PauseMaintenance(string? minutes)
    {
        if (!CanAdminister || Maintenance == null || !int.TryParse(minutes, out var duration)) return;
        try
        {
            Maintenance.Pause(TimeSpan.FromMinutes(duration));
            RecordAudit("MaintenancePause", CreateAuditActor(), "Success", "Automation", $"{duration} minutes");
        }
        catch (Exception ex) { _notificationService.Show(ex.Message, NotificationType.Error); }
    }
    [RelayCommand]
    private void ResumeMaintenance()
    {
        if (!CanAdminister || Maintenance == null) return;
        try { Maintenance.Resume(); RecordAudit("MaintenanceResume", CreateAuditActor(), "Success", "Automation"); }
        catch (Exception ex) { _notificationService.Show(ex.Message, NotificationType.Error); }
    }
    internal Task<string> CheckBrowserAsync(AppItem app, CancellationToken ct) => _processHelper.InspectWebsiteAsync(app, ct);
    internal Task<AppLaunchResult> TestBrowserSwitchAsync(AppItem app, CancellationToken ct) => _processHelper.ActivateWebsiteAsync(app, ct);
    internal string CreateSupportReport()
    {
        var validation = _configValidationService.Validate(BuildCurrentConfigRoot());
        return SupportReportService.Build(Apps.ToArray(), BrowserProfiles, _auditLogService.GetRecentEntries(50),
            IsMaintenancePaused, validation.Errors.Count, validation.Warnings.Count);
    }
    partial void OnQuickFilterChanged(string? oldValue, string newValue)
    {
        if (string.IsNullOrWhiteSpace(oldValue)) _unfilteredExpansion = GroupedApps.ToDictionary(g => g.Key, g => g.IsExpanded);
        RebuildGroups();
        if (string.IsNullOrWhiteSpace(newValue) && _unfilteredExpansion != null)
        {
            foreach (var group in GroupedApps) group.IsExpanded = _unfilteredExpansion.GetValueOrDefault(group.Key, true);
            _unfilteredExpansion = null;
        }
    }
    private void OnMaintenanceChanged() => UiDispatcher.TryBeginInvoke(() =>
    {
        OnPropertyChanged(nameof(IsMaintenancePaused));
        OnPropertyChanged(nameof(MaintenanceStatus));
    });

    internal static IReadOnlyList<AppItem> FilterLauncherApps(IReadOnlyList<AppItem> apps, string query,
        IEnumerable<GroupDefinition> groups, string language)
    {
        query = query.Trim();
        if (query.Length == 0) return apps;
        var matchingGroups = groups.Where(g => g.GetDisplayName(language).Contains(query, StringComparison.CurrentCultureIgnoreCase))
            .Select(g => g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return apps.Where(a => a.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || a.Group.Contains(query, StringComparison.CurrentCultureIgnoreCase) || matchingGroups.Contains(a.Group)).ToArray();
    }

    private void QueueWatchdogCheck(AppItem app, bool wasRunning, bool running, CancellationToken ct)
    {
        // Responsiveness/telemetry passes do not wait for a hook, launch or browser round trip.
        var key = app.ProcessTarget.Key;
        lock (_watchdogCheckGate)
        {
            if (_watchdogChecks.TryGetValue(key, out var existing) && !existing.IsCompleted) return;
            _watchdogChecks[key] = Task.Run(() => RunWatchdogCheckAsync(app, wasRunning, running, ct));
            foreach (var old in _watchdogChecks.Where(kv => kv.Value.IsCompleted && kv.Key != key).Select(kv => kv.Key))
                _watchdogChecks.TryRemove(old, out _);
        }
    }
    private async Task RunWatchdogCheckAsync(AppItem app, bool wasRunning, bool running, CancellationToken ct)
    {
        try { await _watchdogService.CheckAndRestartAsync(app, wasRunning, running, ct).ConfigureAwait(false); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex) { _logger.LogError(ex, "Watchdog check failed for {Name}", app.Name); }
    }

    public async Task StartWorkspaceAsync(ScreenLayout layout, CancellationToken ct = default)
    {
        if (!CanManageApps) return;
        var actor = CreateAuditActor();
        try
        {
            var apps = Apps.ToArray();
            var plan = WorkspacePlanner.Resolve(layout, apps);
            if (plan.Any(a => !CanManageAppForCurrentAccess(a)))
                throw new InvalidOperationException(LocalizationService.T("Workspace_Scope"));
            var launched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var app in plan)
            {
                await StartAppWithDependenciesAsync(app, apps, launched, "Workspace", ct,
                    canLaunchApp: CanManageAppForCurrentAccess);
            }
            foreach (var position in layout.Positions)
            {
                var app = plan.First(a => !string.IsNullOrEmpty(position.AppId) ? a.AppId == position.AppId : string.Equals(a.Name, position.AppName, StringComparison.OrdinalIgnoreCase));
                if (!CanManageAppForCurrentAccess(app)) throw new InvalidOperationException(LocalizationService.T("Workspace_Scope"));
                if (app.IsWebsite) continue;
                var wait = new AppItem { Name = app.Name, Readiness = ReadinessMode.Window, ReadinessTimeoutSeconds = 30 };
                await AppReadinessService.WaitUntilAsync(wait,
                    _ => Task.FromResult(_processHelper.GetMainWindowHandle(app.ProcessTarget) != IntPtr.Zero), ct);
            }
            if (plan.Any(a => !CanManageAppForCurrentAccess(a))) throw new InvalidOperationException(LocalizationService.T("Workspace_Scope"));
            _screenLayoutService.RestoreLayout(layout, plan);
            RecordAudit("StartWorkspace", actor, "Success", layout.Name);
            _notificationService.Show(LocalizationService.T("Workspace_Started", layout.Name), NotificationType.Info);
        }
        catch (Exception ex)
        {
            RecordAudit("StartWorkspace", actor, "Failed", layout.Name, ex.Message);
            _notificationService.Show(ex.Message, NotificationType.Error);
        }
    }
}
