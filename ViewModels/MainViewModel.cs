namespace AppSwitcher.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    public IReadOnlyList<BrowserProfileChoice> BrowserProfiles => _processHelper.BrowserProfiles;
    private readonly ConfigService _configService;
    private readonly ProcessHelper _processHelper;
    private readonly IAppLifecycleService _appLifecycleService;
    private readonly KioskService _kioskService;
    private readonly WatchdogService _watchdogService;
    private readonly NotificationService _notificationService;
    private readonly SecurityService _securityService;
    private readonly ThemeService _themeService;
    private readonly LocalizationService _localizationService;
    private readonly SchedulerService _schedulerService;
    private readonly ConfigDiffService _configDiffService;
    private readonly AuditLogService _auditLogService;
    private readonly SessionTimeoutService _sessionTimeoutService;
    private readonly HealthCheckService _healthCheckService;
    private readonly ResourceMonitorService _resourceMonitorService;
    private readonly ScreenLayoutService _screenLayoutService;
    private readonly WindowPolicyEnforcementService _windowPolicyEnforcementService;
    private readonly ConfigValidationService _configValidationService;
    private readonly CrashHistoryService _crashHistoryService;
    private readonly AppVersionService _appVersionService;
    private readonly ILogger<MainViewModel> _logger;
    private readonly AppReadinessService? _readiness;
    public MaintenanceService? Maintenance { get; }

    private readonly HashSet<Window> _privilegedWindows = [];
    private readonly SemaphoreSlim _kioskStateLock = new(1, 1);
    private CancellationTokenSource? _monitorCts;
    private Task? _monitorTask;
    private ObservableCollection<AppItem>? _observedApps;
    private ObservableCollection<GroupDefinition>? _observedGroups;
    private OperatorScopeSettings? _observedOperatorScope;
    private ConfigRoot? _persistedConfigSnapshot;
    private string _persistedConfigSignature = string.Empty;
    private readonly HashSet<string> _pendingConfigAuditSuppressionKeys = new(StringComparer.OrdinalIgnoreCase);
    private AppItem[] _appsSnapshot = [];
    private bool _isChecking;
    private bool _suppressSettingsNotifications;
    private bool _isShuttingDown;
    private int _shutdownState;
    private AppSettings? _observedSettings;
    private SettingsWindow? _settingsWindow;
    private HealthDashboardWindow? _dashboardWindow;

    [ObservableProperty] private AppSettings _settings = new();
    [ObservableProperty] private ObservableCollection<AppItem> _apps = [];
    [ObservableProperty] private ObservableCollection<GroupDefinition> _groups = [];
    [ObservableProperty] private ObservableCollection<AppGroup> _groupedApps = [];
    [ObservableProperty] private ObservableCollection<AppItem> _pinnedApps = [];
    [ObservableProperty] private ObservableCollection<AppItem> _dashboardApps = [];
    [ObservableProperty] private ObservableCollection<AppItem> _monitoringApps = [];
    [ObservableProperty] private bool _showNeedsAttentionOnly;
    [ObservableProperty] private ObservableCollection<ScreenLayout> _layouts = [];
    [ObservableProperty] private bool _isMenuOpen;
    [ObservableProperty] private AccessLevel _currentAccessLevel = AccessLevel.None;

    internal AuditLogService AuditLogService => _auditLogService;
    internal KioskService KioskService => _kioskService;
    public bool IsSessionTimeoutActive => Settings.SessionTimeoutMinutes > 0;
    public bool CanManageApps => CurrentAccessLevel != AccessLevel.None;

    internal MainViewModel(
        ConfigService configService,
        ProcessHelper processHelper,
        IAppLifecycleService appLifecycleService,
        KioskService kioskService,
        WatchdogService watchdogService,
        NotificationService notificationService,
        SecurityService securityService,
        ThemeService themeService,
        LocalizationService localizationService,
        SchedulerService schedulerService,
        ConfigDiffService configDiffService,
        AuditLogService auditLogService,
        SessionTimeoutService sessionTimeoutService,
        HealthCheckService healthCheckService,
        ResourceMonitorService resourceMonitorService,
        ScreenLayoutService screenLayoutService,
        WindowPolicyEnforcementService windowPolicyEnforcementService,
        ConfigValidationService configValidationService,
        CrashHistoryService crashHistoryService,
        AppVersionService appVersionService,
        ILogger<MainViewModel> logger,
        MaintenanceService? maintenance = null, AppReadinessService? readiness = null)
    {
        _configService = configService;
        _processHelper = processHelper;
        _appLifecycleService = appLifecycleService;
        _kioskService = kioskService;
        _watchdogService = watchdogService;
        _notificationService = notificationService;
        _securityService = securityService;
        _themeService = themeService;
        _localizationService = localizationService;
        _schedulerService = schedulerService;
        _configDiffService = configDiffService;
        _auditLogService = auditLogService;
        _sessionTimeoutService = sessionTimeoutService;
        _healthCheckService = healthCheckService;
        _resourceMonitorService = resourceMonitorService;
        _screenLayoutService = screenLayoutService;
        _windowPolicyEnforcementService = windowPolicyEnforcementService;
        _configValidationService = configValidationService;
        _crashHistoryService = crashHistoryService;
        _appVersionService = appVersionService;
        _logger = logger;
        Maintenance = maintenance;
        _readiness = readiness;
        if (Maintenance != null) Maintenance.Changed += OnMaintenanceChanged;

        _kioskService.SetConfiguredAppTargetProvider(() =>
            Apps
                .Where(app => !string.IsNullOrWhiteSpace(app.Path))
                .Select(app => app.ProcessTarget)
                .ToArray());

        OnAppsChanged(Apps);
    }

    partial void OnCurrentAccessLevelChanged(AccessLevel value)
    {
        OnPropertyChanged(nameof(CanManageApps));
        OnPropertyChanged(nameof(CanAdminister));
        RebuildGroups();
    }

    partial void OnAppsChanged(ObservableCollection<AppItem> value)
    {
        if (_observedApps != null)
        {
            _observedApps.CollectionChanged -= OnAppsCollectionChanged;
            foreach (var app in _observedApps)
                app.PropertyChanged -= OnAppPropertyChanged;
        }

        _observedApps = value;
        _observedApps.CollectionChanged += OnAppsCollectionChanged;
        foreach (var app in _observedApps)
            app.PropertyChanged += OnAppPropertyChanged;
        RefreshAppsSnapshot();
    }

    partial void OnGroupsChanged(ObservableCollection<GroupDefinition> value)
    {
        if (_observedGroups != null)
            _observedGroups.CollectionChanged -= OnGroupsCollectionChanged;

        _observedGroups = value;
        _observedGroups.CollectionChanged += OnGroupsCollectionChanged;
    }

    public async Task InitializeAsync()
    {
        var config = await _configService.LoadAsync();
        await ApplyConfigAsync(config, showNotifications: false);
        MarkConfigAsPersisted(config);

        if (_securityService.NeedsRehash(Settings.AdminPasswordHash))
            _logger.LogInformation("Legacy password hash detected, will re-hash on next verification");

        RunStartupDiagnostics(config);
        StartProcessMonitor();

        _schedulerService.Start(GetAppsSnapshot);
        _healthCheckService.Start(GetAppsSnapshot);
        _resourceMonitorService.Start(GetAppsSnapshot, () => IsTelemetryVisible);
        _windowPolicyEnforcementService.Start(GetAppsSnapshot);

        StartSessionTimeout();

        _configService.ConfigChanged += OnConfigHotReloaded;
        _configService.StartWatching();

        _appVersionService.Start(() => [.. GetAppsSnapshot()]);

        // Startup actions begin after the launcher is visible; readiness waits must not hide the UI.
    }

    public void RegisterMainWindow(Window window)
    {
        PrepareWindow(window, privileged: false);
    }

    public void PrepareWindow(Window window, bool privileged)
    {
        AttachActivityTracking(window);
        _kioskService.RegisterOwnedWindow(window);

        if (privileged)
        {
            _privilegedWindows.Add(window);
            window.Closed += PrivilegedWindow_Closed;
        }
    }

    public void PrepareDialog(Window dialog, Window? owner = null, bool privileged = false)
    {
        PrepareWindow(dialog, privileged);
        if (owner != null)
            dialog.Owner = owner;
        else if (Application.Current?.MainWindow is Window mainWindow && dialog != mainWindow)
            dialog.Owner = mainWindow;
    }

    internal bool ConfirmOperation(string message, string details = "", bool destructive = false)
    {
        if (CurrentAccessLevel == AccessLevel.None) return false;
        if (Application.Current is null) return true;
        var dialog = new ConfirmationDialog(message, details, destructive: destructive);
        PrepareDialog(dialog, (Window?)_settingsWindow ?? _dashboardWindow, privileged: true);
        return dialog.ShowDialog() == true && CurrentAccessLevel != AccessLevel.None;
    }

    internal string DescribeConfigChanges(ConfigRoot incoming) =>
        _configDiffService.Compute(new ConfigRoot { Settings = Settings, Apps = [.. Apps], Groups = [.. Groups], Layouts = [.. Layouts] }, incoming)
            .BuildDetails(Environment.NewLine);

    private void PrivilegedWindow_Closed(object? sender, EventArgs e)
    {
        if (sender is not Window window)
            return;

        window.Closed -= PrivilegedWindow_Closed;
        _privilegedWindows.Remove(window);

        if (ReferenceEquals(window, _settingsWindow))
            _settingsWindow = null;
        if (ReferenceEquals(window, _dashboardWindow))
            _dashboardWindow = null;
    }

    private void AttachActivityTracking(Window window)
    {
        window.PreviewMouseDown += OnTrackedWindowActivity;
        window.PreviewMouseWheel += OnTrackedWindowActivity;
        window.PreviewKeyDown += OnTrackedWindowActivity;
        window.TouchDown += OnTrackedTouchActivity;
    }

    private void OnTrackedWindowActivity(object? sender, RoutedEventArgs e) => _sessionTimeoutService.ResetTimer();

    private void OnTrackedTouchActivity(object? sender, TouchEventArgs e) => _sessionTimeoutService.ResetTimer();

    partial void OnSettingsChanged(AppSettings value)
    {
        if (_observedSettings != null)
            _observedSettings.PropertyChanged -= OnSettingsPropertyChanged;

        if (_observedOperatorScope != null)
            _observedOperatorScope.PropertyChanged -= OnOperatorScopePropertyChanged;

        _observedSettings = value;
        _observedSettings.PropertyChanged += OnSettingsPropertyChanged;
        SetObservedOperatorScope(_observedSettings.OperatorScope);

        if (!_suppressSettingsNotifications)
            _ = RunApplySettingsStateAsync(showNotifications: false);
    }

    private void OnSettingsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_suppressSettingsNotifications)
            return;

        if (e.PropertyName == nameof(AppSettings.KioskMode))
        {
            _ = RunApplyKioskStateAsync(showNotifications: true);
        }
        else if (e.PropertyName == nameof(AppSettings.Theme))
        {
            _themeService.ApplyTheme(Settings.Theme);
        }
        else if (e.PropertyName == nameof(AppSettings.Language))
        {
            _localizationService.ApplyLanguage(Settings.Language);
            RefreshLocalizedRuntimeState();
            RebuildGroups();
        }
        else if (e.PropertyName == nameof(AppSettings.OperatorScope))
        {
            SetObservedOperatorScope(Settings.OperatorScope);
            RebuildGroups();
        }
        else if (e.PropertyName == nameof(AppSettings.SessionTimeoutMinutes))
        {
            StartSessionTimeout();
            OnPropertyChanged(nameof(IsSessionTimeoutActive));
        }
    }

    private void SetObservedOperatorScope(OperatorScopeSettings? scope)
    {
        if (_observedOperatorScope != null)
            _observedOperatorScope.PropertyChanged -= OnOperatorScopePropertyChanged;

        _observedOperatorScope = scope ?? new OperatorScopeSettings();
        _observedOperatorScope.PropertyChanged += OnOperatorScopePropertyChanged;
    }

    private void OnOperatorScopePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_suppressSettingsNotifications)
            return;

        if (e.PropertyName == nameof(OperatorScopeSettings.AllowedAppNames)
            || e.PropertyName == nameof(OperatorScopeSettings.AllowedGroupKeys))
        {
            RebuildGroups();
        }
    }

    private void StartSessionTimeout()
    {
        _sessionTimeoutService.UpdateTimeout(Settings.SessionTimeoutMinutes, OnSessionTimedOut);
        OnPropertyChanged(nameof(IsSessionTimeoutActive));
    }

    private void OnSessionTimedOut()
    {
        CurrentAccessLevel = AccessLevel.None;

        foreach (var window in _privilegedWindows.ToArray())
        {
            try
            {
                window.Close();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not close privileged window {WindowTitle} on session timeout", window.Title);
            }
        }

        _notificationService.Show(LocalizationService.T("Msg_SessionTimedOut"), NotificationType.Warning);
    }

    private void OnConfigHotReloaded(ConfigRoot config)
    {
        if (!UiDispatcher.TryBeginInvoke(() => _ = ApplyHotReloadedConfigAsync(config)))
        {
            _logger.LogDebug("Ignoring config hot-reload because the UI dispatcher is shutting down");
        }
    }

    private async Task ApplyHotReloadedConfigAsync(ConfigRoot config)
    {
        try
        {
            var validation = _configValidationService.Validate(config);
            if (validation.HasErrors)
            {
                _configValidationService.LogIssues(validation);
                _notificationService.Show(
                    LocalizationService.T(
                        "Msg_ConfigHotReloadRejected",
                        ConfigValidationService.BuildSummary(validation.Errors)),
                    NotificationType.Error);
                _logger.LogWarning("Rejected hot-reloaded configuration because of blocking validation errors");
                return;
            }

            if (HasUnsavedLocalConfigChanges())
            {
                _notificationService.Show(
                    LocalizationService.T("Msg_ConfigHotReloadSkippedDirty"),
                    NotificationType.Warning);
                _logger.LogWarning("Skipped hot-reloaded configuration because local unsaved changes are present");
                return;
            }

            var oldConfig = BuildCurrentConfigRoot();
            var diff = _configDiffService.Compute(oldConfig, config);
            _configDiffService.Log(diff);

            await ApplyConfigAsync(config, showNotifications: false);
            MarkConfigAsPersisted(config);

            if (diff.HasChanges)
            {
                RecordAudit(
                    "ConfigChanged",
                    CreateExternalAuditActor(),
                    "Success",
                    "Configuration",
                    diff.BuildDetails());
            }

            var diffSummary = diff.BuildSummary();
            var message = string.IsNullOrEmpty(diffSummary)
                ? LocalizationService.T("Msg_ConfigHotReloaded")
                : $"{LocalizationService.T("Msg_ConfigHotReloaded")}: {diffSummary}";

            _notificationService.Show(message, NotificationType.Info);
            _logger.LogInformation("Config hot-reloaded and applied");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply hot-reloaded configuration");
        }
    }

    private void RunStartupDiagnostics(ConfigRoot config)
    {
        var validation = _configValidationService.Validate(config);
        if (validation.Warnings.Count > 0)
        {
            _notificationService.Show(
                LocalizationService.T("Msg_DiagWarnings", validation.Warnings.Count),
                NotificationType.Warning);
        }

        _logger.LogInformation(
            "Startup diagnostics: {Warnings} warnings, {Errors} errors",
            validation.Warnings.Count,
            validation.Errors.Count);
    }

    private void StartProcessMonitor()
    {
        if (_monitorCts != null)
            return;

        _monitorCts = new CancellationTokenSource();
        var token = _monitorCts.Token;
        _monitorTask = Task.Run(() => ProcessMonitorLoop(token));
    }

    private async Task ProcessMonitorLoop(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var interval = (GetAppsSnapshot().Any(a => a.WatchdogEnabled) || IsMenuOpen)
                    ? TimeSpan.FromSeconds(1)
                    : TimeSpan.FromSeconds(5);

                await _processHelper.WaitForProcessChangeAsync(interval, ct);
                await CheckProcessesAsync(ct);
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task CheckProcessesAsync(CancellationToken ct)
    {
        if (_isChecking) return;
        _isChecking = true;

        try
        {
            var appsToCheck = GetAppsSnapshot();
            _responsiveness.Retain(appsToCheck.Select(a => a.ProcessTarget.Key).ToHashSet());
            _processHelper.RetainObservedTargets(appsToCheck.Select(a => a.ProcessTarget.Key).ToHashSet());

            await Parallel.ForEachAsync(appsToCheck, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct }, (app, token) =>
            {
                ct.ThrowIfCancellationRequested();

                var websitePresence = app.IsWebsite ? _processHelper.GetWebsitePresence(app.ProcessTarget) : WebsitePresence.Absent;
                if (app.IsWebsite && websitePresence is WebsitePresence.Unknown or WebsitePresence.Conflict)
                {
                    UiDispatcher.TryBeginInvoke(() =>
                    {
                        app.State = AppState.Recovering;
                        app.IsResponding = false;
                        app.HealthStatus = LocalizationService.T(websitePresence == WebsitePresence.Conflict ? "Browser_IdentityConflict" : "Browser_Recovering");
                    });
                    // Missing bridge telemetry is not a crash and must not trigger the watchdog.
                    return ValueTask.CompletedTask;
                }

                bool isRunning;
                using (_processHelper.ObserveProcesses()) isRunning = _processHelper.IsAppRunning(app.ProcessTarget);
                var wasRunning = app.IsRunning;
                var stopExpected = wasRunning && !isRunning && _watchdogService.IsStopExpected(app.ProcessTarget);
                var unexpectedExit = !stopExpected && _processHelper.GetLastExitKind(app.ProcessTarget) == ProcessExitKind.Unexpected;

                if (wasRunning != isRunning || app.State == AppState.Recovering)
                {
                    if (isRunning && !wasRunning)
                        _windowPolicyEnforcementService.RequestImmediateApply(app);

                    UiDispatcher.TryBeginInvoke(() =>
                    {
                        app.IsRunning = isRunning;

                        if (isRunning && !wasRunning)
                            app.LastStartTime = DateTime.Now;

                        if (isRunning)
                            app.State = AppState.Running;
                        else if (wasRunning)
                            app.State = unexpectedExit ? AppState.Crashed : AppState.Stopped;
                        else
                            app.State = AppState.Stopped;

                        if (string.IsNullOrWhiteSpace(app.HealthCheckUrl))
                        {
                            app.HealthStatus = isRunning
                                ? LocalizationService.T("Status_Ok")
                                : LocalizationService.T("Status_Stopped");
                        }
                    });
                }

                if (isRunning)
                {
                    var target = app.ProcessTarget;
                    _responsiveness.Queue(target.Key, TimeSpan.FromSeconds(IsTelemetryVisible ? 1 : 5),
                        () => { using var observation = _processHelper.ObserveProcesses(); return _processHelper.IsAppResponding(target); },
                        responding => UiDispatcher.TryBeginInvoke(() =>
                        {
                            if (app.IsRunning && app.ProcessTarget == target) app.IsResponding = responding;
                        }), ct);
                }
                else if (app.State == AppState.Stopped)
                {
                    UiDispatcher.TryBeginInvoke(() =>
                    {
                        app.HealthStatus = string.IsNullOrWhiteSpace(app.HealthCheckUrl)
                            ? LocalizationService.T("Status_Stopped")
                            : LocalizationService.T("Status_Unknown");
                    });
                }

                QueueWatchdogCheck(app, wasRunning, isRunning, ct);

                var (crashesToday, totalRestarts) = _watchdogService.GetCrashStats(app.ProcessTarget);
                var watchdogStatus = app.WatchdogEnabled
                    ? _watchdogService.GetStatus(app.ProcessTarget)
                    : WatchdogStatus.None;

                UiDispatcher.TryBeginInvoke(() =>
                {
                    if (app.CrashCountToday != crashesToday)
                        app.CrashCountToday = crashesToday;
                    if (app.TotalRestarts != totalRestarts)
                        app.TotalRestarts = totalRestarts;

                    ApplyWatchdogUiState(app, watchdogStatus);
                });
                return ValueTask.CompletedTask;
            });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in process monitoring");
        }
        finally
        {
            _isChecking = false;
        }
    }

    public void StartAutoLaunch()
    {
        var token = _autoLaunchLifetime.Token;
        _autoLaunchTask ??= Task.Run(() => AutoLaunchApps(token));
    }

    private async Task AutoLaunchApps(CancellationToken shutdown)
    {
        if (Maintenance?.IsPaused == true) return;
        using var automatic = CancellationTokenSource.CreateLinkedTokenSource(shutdown, Maintenance?.AutomaticToken ?? CancellationToken.None);
        var automaticToken = automatic.Token;
        var appsToLaunch = GetAppsSnapshot().Where(a => a.AutoLaunch).ToList();
        if (appsToLaunch.Count == 0) return;

        _logger.LogInformation("Auto-launching {Count} apps with dependency ordering", appsToLaunch.Count);

        var allApps = GetAppsSnapshot().ToList();
        var launchedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var app in appsToLaunch)
        {
            try
            {
                await StartAppWithDependenciesAsync(
                    app,
                    allApps,
                    launchedKeys,
                    operationName: "Auto-launch", ct: automaticToken);
            }
            catch (OperationCanceledException) when (automaticToken.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to auto-launch {Name}", app.Name);
                _notificationService.Show(GetOperationFailureMessage(ex, "Msg_AutostartFailed", app), NotificationType.Error);
            }
        }
    }

    private async Task StartAppWithDependenciesAsync(
        AppItem requestedApp,
        IReadOnlyList<AppItem> allApps,
        ISet<string> launchedKeys,
        string operationName,
        CancellationToken ct = default,
        bool activateIfAlreadyRunning = false,
        Func<AppItem, bool>? canLaunchApp = null)
    {
        foreach (var plannedApp in AppDependencyPlanner.BuildLaunchPlan([requestedApp], allApps))
        {
            ct.ThrowIfCancellationRequested();

            if (canLaunchApp != null && !canLaunchApp(plannedApp))
            {
                throw new InvalidOperationException(
                    $"App '{plannedApp.Name}' is outside the permitted operator scope for '{requestedApp.Name}'.");
            }

            var dependency = AppDependencyPlanner.FindByName(allApps, plannedApp.DependsOn);
            if (dependency != null && !_processHelper.IsAppRunning(dependency.ProcessTarget))
            {
                throw new InvalidOperationException(
                    $"Dependency '{dependency.Name}' is not running for '{plannedApp.Name}'.");
            }

            var key = plannedApp.ProcessTarget.Key;
            var isRunning = _processHelper.IsAppRunning(plannedApp.ProcessTarget);

            if (isRunning)
            {
                if (_readiness != null) await _readiness.WaitAsync(plannedApp, ct);
                launchedKeys.Add(key);

                if (activateIfAlreadyRunning && ReferenceEquals(plannedApp, requestedApp))
                    await _appLifecycleService.LaunchAsync(plannedApp, ct);

                continue;
            }

            if (launchedKeys.Contains(key))
                continue;

            if (plannedApp.LaunchDelay > 0)
                await Task.Delay(plannedApp.LaunchDelay, ct);

            await _appLifecycleService.LaunchAsync(plannedApp, ct);
            launchedKeys.Add(key);
            _logger.LogInformation("{Operation}: launched {Name}", operationName, plannedApp.Name);
        }
    }

    internal AuditActor CreateAuditActor(AccessLevel? accessLevelOverride = null)
    {
        var accessLevel = accessLevelOverride ?? CurrentAccessLevel;
        return new AuditActor(MapAccessLevelToAuditRole(accessLevel), GetCurrentWindowsUser());
    }

    internal static AuditActor CreateExternalAuditActor() => new("External", "Unknown");

    internal void RecordAudit(string action, AuditActor actor, string outcome, string target, string details = "") =>
        _auditLogService.Record(action, actor, outcome, target, details);

    private void RegisterConfigAuditSuppression(params string[] keys)
    {
        foreach (var key in keys.Where(key => !string.IsNullOrWhiteSpace(key)))
            _pendingConfigAuditSuppressionKeys.Add(key);
    }

    public void RebuildGroups()
    {
        var visibleApps = GetVisibleAppsForAccess(CurrentAccessLevel);
        DashboardApps = new ObservableCollection<AppItem>(visibleApps);
        RefreshMonitoringApps();
        visibleApps = FilterLauncherApps(visibleApps, QuickFilter, Groups, Settings.Language);
        var expanded = GroupedApps.ToDictionary(g => g.Key, g => g.IsExpanded);
        var groups = new ObservableCollection<AppGroup>();

        var pinned = visibleApps.Where(a => a.IsPinned).OrderBy(a => a.SortOrder).ThenBy(a => a.Name).ToList();
        PinnedApps = new ObservableCollection<AppItem>(pinned);

        var ungrouped = visibleApps.Where(a => string.IsNullOrEmpty(a.Group) && !a.IsPinned)
            .OrderBy(a => a.SortOrder).ThenBy(a => a.Name).ToList();
        if (ungrouped.Count > 0)
            groups.Add(new AppGroup("", "") { Apps = new(ungrouped) });

        foreach (var g in visibleApps.Where(a => !string.IsNullOrEmpty(a.Group) && !a.IsPinned)
                     .GroupBy(a => a.Group)
                     .OrderBy(g => g.Key))
        {
            var sorted = g.OrderBy(a => a.SortOrder).ThenBy(a => a.Name).ToList();
            var groupDef = Groups.FirstOrDefault(gd => string.Equals(gd.Key, g.Key, StringComparison.OrdinalIgnoreCase));
            var displayName = groupDef?.GetDisplayName(Settings.Language) ?? g.Key;
            groups.Add(new AppGroup(g.Key, displayName) { Apps = new(sorted) });
        }

        foreach (var group in groups)
            group.IsExpanded = !string.IsNullOrWhiteSpace(QuickFilter) || expanded.GetValueOrDefault(group.Key, true);
        GroupedApps = groups;
        OnPropertyChanged(nameof(HasLauncherMatches));
    }

    partial void OnShowNeedsAttentionOnlyChanged(bool value) => RefreshMonitoringApps();

    private void RefreshMonitoringApps()
    {
        var visible = DashboardApps.Where(app => !ShowNeedsAttentionOnly || app.NeedsAttention).ToArray();
        if (!MonitoringApps.SequenceEqual(visible)) MonitoringApps = new(visible);
    }

    internal IReadOnlyList<AppItem> GetVisibleAppsForAccess(AccessLevel accessLevel) =>
        ResolveVisibleApps(Apps, accessLevel, Settings.OperatorScope);

    internal IReadOnlyList<GroupDefinition> GetVisibleGroupsForAccess(AccessLevel accessLevel) =>
        ResolveVisibleGroups(Groups, GetVisibleAppsForAccess(accessLevel), accessLevel, Settings.OperatorScope);

    internal bool CanAccessAppForAccessLevel(AppItem? app, AccessLevel accessLevel) =>
        app != null
        && (accessLevel != AccessLevel.Operator
            || ResolveVisibleApps([app], accessLevel, Settings.OperatorScope).Count > 0);

    private bool CanAccessAppForCurrentAccess(AppItem? app) => CanAccessAppForAccessLevel(app, CurrentAccessLevel);

    private bool CanManageAppForCurrentAccess(AppItem? app) =>
        CurrentAccessLevel != AccessLevel.None && CanAccessAppForCurrentAccess(app);

    private bool CanManageGroupForCurrentAccess(AppGroup? group) =>
        group != null && CurrentAccessLevel != AccessLevel.None && group.Apps.Any(CanAccessAppForCurrentAccess);

    internal static IReadOnlyList<AppItem> ResolveVisibleApps(
        IEnumerable<AppItem> apps,
        AccessLevel accessLevel,
        OperatorScopeSettings? scope)
    {
        var appList = apps.ToList();
        if (accessLevel != AccessLevel.Operator || scope == null || !scope.HasRestrictions)
            return appList;

        var allowedAppNames = scope.AllowedAppNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var allowedGroupKeys = scope.AllowedGroupKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);

        return appList
            .Where(app =>
            {
                var appName = OperatorScopeSettings.NormalizeEntry(app.Name);
                var groupKey = OperatorScopeSettings.NormalizeEntry(app.Group);
                return (!string.IsNullOrEmpty(appName) && allowedAppNames.Contains(appName))
                       || (!string.IsNullOrEmpty(groupKey) && allowedGroupKeys.Contains(groupKey));
            })
            .ToList();
    }

    internal static IReadOnlyList<GroupDefinition> ResolveVisibleGroups(
        IEnumerable<GroupDefinition> groups,
        IEnumerable<AppItem> visibleApps,
        AccessLevel accessLevel,
        OperatorScopeSettings? scope)
    {
        var groupList = groups.ToList();
        if (accessLevel != AccessLevel.Operator || scope == null || !scope.HasRestrictions)
            return groupList;

        var visibleGroupKeys = scope.AllowedGroupKeys
            .Concat(visibleApps
                .Select(app => OperatorScopeSettings.NormalizeEntry(app.Group))
                .Where(groupKey => !string.IsNullOrEmpty(groupKey)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return groupList
            .Where(group => visibleGroupKeys.Contains(OperatorScopeSettings.NormalizeEntry(group.Key)))
            .ToList();
    }

    public sealed record ConfigSaveAuditOptions(
        AuditActor? ActorOverride = null,
        bool SuppressGenericConfigAudit = false,
        IReadOnlyCollection<string>? SuppressedChangeKeys = null);

    public async Task SaveConfigAsync(ConfigSaveAuditOptions? auditOptions = null)
    {
        try
        {
            for (int i = 0; i < Apps.Count; i++)
                Apps[i].SortOrder = i;

            // Freeze the saved version before awaiting I/O. Edits made while saving
            // must remain dirty instead of being mistaken for persisted changes.
            var root = CloneConfigRoot(BuildCurrentConfigRoot());
            EnsureConfigCanBeApplied(root, notifyFailure: true);
            var diff = BuildPersistedConfigDiff(root);
            await _configService.SaveAsync(root);
            RecordConfigSaveAudit(root, diff, auditOptions);
            MarkConfigAsPersisted(root);
            _pendingConfigAuditSuppressionKeys.Clear();
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Configuration save blocked by validation");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save configuration");
            _notificationService.Show(LocalizationService.T("Msg_SaveFailed"), NotificationType.Error);
            throw;
        }
    }

    public void SaveConfigInBackground(ConfigSaveAuditOptions? auditOptions = null)
    {
        _ = SaveConfigInBackgroundCore(auditOptions);
    }

    private async Task SaveConfigInBackgroundCore(ConfigSaveAuditOptions? auditOptions)
    {
        try
        {
            await SaveConfigAsync(auditOptions);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Background config save failure already surfaced");
        }
    }

    public async Task ApplyConfigAsync(ConfigRoot config, bool showNotifications = false)
    {
        ArgumentNullException.ThrowIfNull(config);
        EnsureConfigCanBeApplied(config, notifyFailure: false);

        _suppressSettingsNotifications = true;
        try
        {
            if (_observedSettings == null)
                Settings = config.Settings;
            else
                CopySettings(config.Settings, Settings);

            SyncCollection(Apps, config.Apps);
            SyncCollection(Groups, config.Groups);
            SyncCollection(Layouts, config.Layouts ?? []);
            RefreshAppsSnapshot();
            RebuildGroups();
        }
        finally
        {
            _suppressSettingsNotifications = false;
        }

        await ApplySettingsStateAsync(showNotifications);
    }

    [RelayCommand]
    private void ToggleMenu() => IsMenuOpen = !IsMenuOpen;

    [RelayCommand]
    private async Task LaunchApp(AppItem? app)
    {
        if (app is null || !CanAccessAppForCurrentAccess(app)) return;

        try
        {
            if (Settings.AutoClose || app.IsWebsite) IsMenuOpen = false;
            await StartAppWithDependenciesAsync(
                app,
                Apps.ToList(),
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                operationName: "Launch",
                activateIfAlreadyRunning: true,
                canLaunchApp: CanAccessAppForCurrentAccess);
            if (Settings.AutoClose)
                IsMenuOpen = false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to launch {Name}", app.Name);
            _notificationService.Show(GetOperationFailureMessage(ex, "Msg_LaunchFailed", app), NotificationType.Error);
        }
    }

    [RelayCommand]
    private async Task BringToFront(AppItem? app)
    {
        if (app is null || !CanAccessAppForCurrentAccess(app) || !app.IsRunning) return;
        if (app.IsWebsite)
        {
            IsMenuOpen = false;
            try { await _processHelper.ActivateWebsiteAsync(app); }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to activate website {Name}", app.Name);
                _notificationService.Show(GetOperationFailureMessage(ex, "Msg_LaunchFailed", app), NotificationType.Error);
            }
            return;
        }
        if (Settings.AutoClose) IsMenuOpen = false;
        _processHelper.BringToFront(app.ProcessTarget);
        _windowPolicyEnforcementService.RequestImmediateApply(app);
    }

    [RelayCommand]
    private void AcknowledgeWatchdog(AppItem? app)
    {
        if (app is null || !CanManageAppForCurrentAccess(app) || !app.RequiresWatchdogAcknowledgement)
            return;

        if (!ConfirmOperation(LocalizationService.T("UI_ConfirmAcknowledge", app.Name), app.WatchdogStatusText)) return;

        _watchdogService.AcknowledgeCircuit(app.ProcessTarget);
        ApplyWatchdogUiState(app, _watchdogService.GetStatus(app.ProcessTarget));
        _notificationService.Show(LocalizationService.T("Msg_WatchdogAcknowledged", app.Name), NotificationType.Info);
        _logger.LogInformation("Watchdog circuit acknowledged for {Name}", app.Name);
    }

    [RelayCommand]
    private void TogglePin(AppItem? app)
    {
        if (app is null || !CanManageAppForCurrentAccess(app)) return;
        app.IsPinned = !app.IsPinned;
        RebuildGroups();
        SaveConfigInBackground();
    }

    [RelayCommand]
    private async Task KillApp(AppItem? app)
    {
        if (app is null || !CanManageAppForCurrentAccess(app) || !app.IsRunning) return;

        if (app.RequireConfirmation)
        {
            var dialog = new ConfirmationDialog(app.Name);
            PrepareDialog(dialog, privileged: true);
            if (dialog.ShowDialog() != true || !CanManageAppForCurrentAccess(app)) return;
        }

        try
        {
            await _appLifecycleService.StopAsync(app, AppStopMode.ForceKill);
            _logger.LogInformation("Killed app: {Name}", app.Name);
            RecordAudit(
                "AppStopped",
                CreateAuditActor(),
                "Success",
                app.Name,
                string.IsNullOrWhiteSpace(app.Path) ? string.Empty : $"Path: {app.Path}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stop {Name}", app.Name);
            _notificationService.Show(GetOperationFailureMessage(ex, "Msg_StopFailed", app), NotificationType.Error);
        }
    }

    [RelayCommand]
    private async Task StartGroup(AppGroup? group)
    {
        if (group is null || !CanManageGroupForCurrentAccess(group)) return;
        if (!ConfirmOperation(LocalizationService.T("UI_ConfirmStartGroup", group.Name), string.Join(Environment.NewLine, group.Apps.Select(app => app.Name)))) return;

        var allApps = GetAppsSnapshot().ToList();
        var launchedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var app in group.Apps.Where(CanManageAppForCurrentAccess))
        {
            try
            {
                await StartAppWithDependenciesAsync(
                    app,
                    allApps,
                    launchedKeys,
                    operationName: "Group start",
                    canLaunchApp: CanAccessAppForCurrentAccess);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Group start: failed to launch {Name}", app.Name);
                _notificationService.Show(GetOperationFailureMessage(ex, "Msg_LaunchFailed", app), NotificationType.Error);
            }
        }
    }

    [RelayCommand]
    private async Task StopGroup(AppGroup? group)
    {
        if (group is null || !CanManageGroupForCurrentAccess(group)) return;
        if (!ConfirmOperation(LocalizationService.T("UI_ConfirmStopGroup", group.Name), string.Join(Environment.NewLine, group.Apps.Where(app => app.IsRunning).Select(app => app.Name)), destructive: true)) return;

        foreach (var app in group.Apps.Where(CanManageAppForCurrentAccess))
        {
            if (!app.IsRunning) continue;

            try
            {
                await _appLifecycleService.StopAsync(app, AppStopMode.ForceKill);
                _logger.LogInformation("Group stop: killed {Name}", app.Name);
                RecordAudit(
                    "AppStopped",
                    CreateAuditActor(),
                    "Success",
                    app.Name,
                    $"Group: {group.Name}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Group stop: failed to stop {Name}", app.Name);
                _notificationService.Show(GetOperationFailureMessage(ex, "Msg_StopFailed", app), NotificationType.Error);
            }
        }
    }

    private void ApplyWatchdogUiState(AppItem app, WatchdogStatus status)
    {
        app.RequiresWatchdogAcknowledgement = status.RequiresAcknowledgement;
        app.WatchdogNextRestartAt = status.NextRestartAt;
        app.WatchdogStatusText = BuildWatchdogStatusText(app, status);

        if (!app.IsRunning)
        {
            if (status.NextRestartAt != null)
                app.State = AppState.Restarting;
            else if (app.State == AppState.Restarting)
                app.State = AppState.Crashed;
        }
    }

    private string BuildWatchdogStatusText(AppItem app, WatchdogStatus status)
    {
        if (!app.WatchdogEnabled)
            return string.Empty;

        if (status.RequiresAcknowledgement)
            return LocalizationService.T("Watchdog_StatusAcknowledgementRequired", status.AttemptCount, app.MaxRestarts);

        if (status.NextRestartAt is DateTime nextRestartAt)
        {
            var secondsRemaining = Math.Max(0, (int)Math.Ceiling((nextRestartAt - DateTime.UtcNow).TotalSeconds));
            return LocalizationService.T("Watchdog_StatusPendingRestart", secondsRemaining, status.AttemptCount, app.MaxRestarts);
        }

        return string.Empty;
    }

    [RelayCommand]
    private void OpenDashboard()
    {
        IsMenuOpen = false;
        var accessLevel = EnsureAuthenticated(AccessLevel.Operator);
        if (accessLevel == AccessLevel.None)
            return;

        if (_dashboardWindow?.IsLoaded == true)
        {
            _dashboardWindow.Activate();
            _dashboardWindow.Focus();
            return;
        }

        var dashboard = new HealthDashboardWindow { DataContext = this };
        PrepareWindow(dashboard, privileged: true);
        _dashboardWindow = dashboard;
        dashboard.Show();
    }

    [RelayCommand]
    private async Task OpenSettings()
    {
        IsMenuOpen = false;
        var accessLevel = EnsureAuthenticated(AccessLevel.Operator);
        if (accessLevel == AccessLevel.None)
            return;

        if (_settingsWindow?.IsLoaded == true)
        {
            _settingsWindow.Activate();
            _settingsWindow.Focus();
            return;
        }

        var vm = new SettingsViewModel(
            this,
            _configService,
            _securityService,
            _notificationService,
            _screenLayoutService,
            _crashHistoryService,
            _sessionTimeoutService,
            accessLevel);

        var win = new SettingsWindow { DataContext = vm };
        PrepareWindow(win, privileged: true);
        _settingsWindow = win;
        win.ShowDialog();

        RebuildGroups();
        if (win.ConfigurationSavedOnClose) return;
        try
        {
            await SaveConfigAsync();
        }
        catch (InvalidOperationException)
        {
            _logger.LogInformation("Settings changes were kept in memory but not saved because validation failed");
        }
    }

    private AccessLevel EnsureAuthenticated(AccessLevel minimumLevel)
    {
        if (CurrentAccessLevel >= minimumLevel)
        {
            _sessionTimeoutService.ResetTimer();
            return CurrentAccessLevel;
        }

        AccessLevel accessLevel;
        if (!_securityService.IsPinConfigured(Settings.AdminPasswordHash))
        {
            var setupDialog = new PasswordDialog { IsSetMode = true };
            PrepareDialog(setupDialog);
            if (setupDialog.ShowDialog() != true)
                return AccessLevel.None;

            Settings.AdminPasswordHash = _securityService.HashPassword(setupDialog.Password);
            _notificationService.Show(LocalizationService.T("Msg_PinSet"), NotificationType.Info);
            SaveConfigInBackground(new ConfigSaveAuditOptions(
                ActorOverride: CreateAuditActor(AccessLevel.Admin)));
            accessLevel = AccessLevel.Admin;
        }
        else
        {
            if (_securityService.RetryAfter > TimeSpan.Zero)
            {
                _notificationService.Show(_securityService.AuthenticationFailureMessage, NotificationType.Error);
                return AccessLevel.None;
            }
            var dialog = new PasswordDialog();
            PrepareDialog(dialog);
            if (dialog.ShowDialog() != true)
                return AccessLevel.None;

            accessLevel = _securityService.GetAccessLevel(
                dialog.Password,
                Settings.AdminPasswordHash,
                Settings.OperatorPasswordHash);

            if (accessLevel == AccessLevel.None || accessLevel < minimumLevel)
            {
                RecordAudit("Authentication", CreateAuditActor(AccessLevel.None), "Denied", "Access");
                _notificationService.Show(_securityService.AuthenticationFailureMessage, NotificationType.Error);
                return AccessLevel.None;
            }

            if (accessLevel == AccessLevel.Admin && _securityService.NeedsRehash(Settings.AdminPasswordHash))
            {
                Settings.AdminPasswordHash = _securityService.HashPassword(dialog.Password);
                _logger.LogInformation("Password hash migrated to PBKDF2");
            }

            if (accessLevel == AccessLevel.Operator)
            {
                if (_securityService.NeedsRehash(Settings.OperatorPasswordHash))
                    Settings.OperatorPasswordHash = _securityService.HashPassword(dialog.Password);
                _notificationService.Show(LocalizationService.T("Msg_OperatorAccess"), NotificationType.Info);
            }
        }

        CurrentAccessLevel = accessLevel;
        _sessionTimeoutService.ResetTimer();
        return accessLevel;
    }

    public async Task ShutdownAsync(CancellationToken ct = default, bool stopManagedApps = true)
    {
        if (Interlocked.Exchange(ref _shutdownState, 1) != 0)
            return;

        _isShuttingDown = true;
        _configService.ConfigChanged -= OnConfigHotReloaded;
        _configService.StopWatching();

        await StopBackgroundServicesAsync(ct);
        if (stopManagedApps) await ShutdownManagedAppsAsync(ct);
    }

    public async Task ShutdownManagedAppsAsync(CancellationToken ct = default)
    {
        _isShuttingDown = true;

        var managedApps = GetAppsSnapshot().Where(a => a.AutoLaunch || a.WatchdogEnabled).ToList();
        if (managedApps.Count == 0) return;

        _logger.LogInformation("Graceful shutdown: stopping {Count} managed apps in reverse-dependency order", managedApps.Count);
        var shutdownOrder = BuildReverseShutdownOrder(managedApps);

        foreach (var app in shutdownOrder)
        {
            app.WatchdogEnabled = false;

            try
            {
                ct.ThrowIfCancellationRequested();

                if (!_processHelper.IsAppRunning(app.ProcessTarget)) continue;

                _logger.LogInformation("Shutting down: {Name}", app.Name);
                await _appLifecycleService.StopAsync(app, AppStopMode.Shutdown, ct);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Managed app shutdown cancelled while stopping {Name}", app.Name);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error shutting down {Name}", app.Name);
            }
        }

        _logger.LogInformation("Graceful shutdown complete");
    }

    public bool ShouldPreventLauncherClose() => Settings.KioskMode && !_isShuttingDown && !Application.Current.Dispatcher.HasShutdownStarted;

    private List<AppItem> BuildReverseShutdownOrder(List<AppItem> apps)
        => AppDependencyPlanner.BuildReverseShutdownOrder(apps);

    private async Task StopBackgroundServicesAsync(CancellationToken ct)
    {
        await StopServiceAsync("scheduler", () => _schedulerService.StopAsync());
        await StopProcessMonitorAsync();
        await StopServiceAsync("health checks", () => _healthCheckService.StopAsync());
        await StopServiceAsync("resource monitor", () => _resourceMonitorService.StopAsync());
        await StopServiceAsync("window policy enforcement", () => _windowPolicyEnforcementService.StopAsync());
        await StopServiceAsync("version monitor", () => _appVersionService.StopAsync());
        await StopServiceAsync("session timeout", () => _sessionTimeoutService.StopAsync());

        ct.ThrowIfCancellationRequested();
    }

    private async Task StopProcessMonitorAsync()
    {
        var cts = _monitorCts;
        var monitorTask = _monitorTask;

        _monitorCts = null;
        _monitorTask = null;

        if (cts == null)
            return;

        cts.Cancel();
        _autoLaunchLifetime.Cancel();
        try
        {
            if (_autoLaunchTask != null) await _autoLaunchTask;
            if (monitorTask != null)
                await monitorTask;
            await Task.WhenAll(_watchdogChecks.Values);
            await _responsiveness.DrainAsync();
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            cts.Dispose();
        }
    }

    private async Task StopServiceAsync(string serviceName, Func<Task> stopAsync)
    {
        try
        {
            await stopAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stop {ServiceName}", serviceName);
        }
    }

    private ConfigRoot BuildCurrentConfigRoot() =>
        new()
        {
            Settings = Settings,
            Groups = [.. Groups],
            Apps = [.. Apps],
            Layouts = [.. Layouts]
        };

    public void MarkConfigAsPersisted(ConfigRoot? config = null)
    {
        var snapshot = CloneConfigRoot(config ?? BuildCurrentConfigRoot());
        _persistedConfigSnapshot = snapshot;
        _persistedConfigSignature = CreateConfigSignature(snapshot);
        OnPropertyChanged(nameof(HasUnsavedConfiguration));
    }

    private void EnsureConfigCanBeApplied(ConfigRoot config, bool notifyFailure)
    {
        var validation = _configValidationService.Validate(config);
        if (!validation.HasErrors)
            return;

        _configValidationService.LogIssues(validation);
        var summary = ConfigValidationService.BuildSummary(validation.Errors);

        if (notifyFailure)
        {
            _notificationService.Show(
                LocalizationService.T("Msg_ConfigValidationFailed", summary),
                NotificationType.Error);
        }

        throw new InvalidOperationException(summary);
    }

    private bool HasUnsavedLocalConfigChanges() =>
        !string.Equals(_persistedConfigSignature, CreateConfigSignature(BuildCurrentConfigRoot()), StringComparison.Ordinal);

    internal bool HasUnsavedConfiguration => HasUnsavedLocalConfigChanges();

    private IReadOnlyList<AppItem> GetAppsSnapshot() => _appsSnapshot;

    private void RefreshAppsSnapshot()
    {
        _appsSnapshot = [.. Apps];
    }

    private void OnAppsCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (var app in e.OldItems.OfType<AppItem>())
                app.PropertyChanged -= OnAppPropertyChanged;
        }

        if (e.NewItems != null)
        {
            foreach (var app in e.NewItems.OfType<AppItem>())
                app.PropertyChanged += OnAppPropertyChanged;
        }

        RefreshAppsSnapshot();

        if (!_suppressSettingsNotifications)
            RebuildGroups();
    }

    private void OnGroupsCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (!_suppressSettingsNotifications)
            RebuildGroups();
    }

    private void OnAppPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_suppressSettingsNotifications)
            return;

        if (e.PropertyName == nameof(AppItem.NeedsAttention)) RefreshMonitoringApps();

        if (e.PropertyName == nameof(AppItem.Group)
            || e.PropertyName == nameof(AppItem.IsPinned)
            || e.PropertyName == nameof(AppItem.SortOrder)
            || e.PropertyName == nameof(AppItem.Name))
        {
            RebuildGroups();
        }
    }

    private static string CreateConfigSignature(ConfigRoot config) =>
        JsonSerializer.Serialize(config, AppJsonOptions.Default);

    private ConfigDiffResult BuildPersistedConfigDiff(ConfigRoot currentConfig)
    {
        var baseline = _persistedConfigSnapshot ?? CloneConfigRoot(currentConfig);
        return _configDiffService.Compute(baseline, currentConfig);
    }

    private void RecordConfigSaveAudit(ConfigRoot savedConfig, ConfigDiffResult diff, ConfigSaveAuditOptions? auditOptions)
    {
        ArgumentNullException.ThrowIfNull(savedConfig);
        ArgumentNullException.ThrowIfNull(diff);

        if (auditOptions?.SuppressGenericConfigAudit == true)
            return;

        var actor = auditOptions?.ActorOverride;
        if (actor == null && CurrentAccessLevel != AccessLevel.None)
            actor = CreateAuditActor();

        if (actor == null)
            return;

        var suppressedKeys = _pendingConfigAuditSuppressionKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (auditOptions?.SuppressedChangeKeys != null)
            suppressedKeys.UnionWith(auditOptions.SuppressedChangeKeys.Where(key => !string.IsNullOrWhiteSpace(key)));

        var filteredDiff = diff.WithoutKeys(suppressedKeys);
        if (!filteredDiff.HasChanges)
            return;

        RecordAudit(
            "ConfigChanged",
            actor,
            "Success",
            "Configuration",
            filteredDiff.BuildDetails());
    }

    private static ConfigRoot CloneConfigRoot(ConfigRoot config)
    {
        var json = JsonSerializer.Serialize(config, AppJsonOptions.Default);
        return JsonSerializer.Deserialize<ConfigRoot>(json, AppJsonOptions.Default)
               ?? throw new InvalidOperationException("Could not clone configuration snapshot.");
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _autoLaunchLifetime.Cancel();
        _autoLaunchLifetime.Dispose();
        _monitorCts?.Cancel();
        if (Maintenance != null) Maintenance.Changed -= OnMaintenanceChanged;
        if (_observedSettings != null)
            _observedSettings.PropertyChanged -= OnSettingsPropertyChanged;
        if (_observedOperatorScope != null)
            _observedOperatorScope.PropertyChanged -= OnOperatorScopePropertyChanged;
        if (_observedApps != null)
        {
            _observedApps.CollectionChanged -= OnAppsCollectionChanged;
            foreach (var app in _observedApps)
                app.PropertyChanged -= OnAppPropertyChanged;
        }
        if (_observedGroups != null)
            _observedGroups.CollectionChanged -= OnGroupsCollectionChanged;
        _monitorCts?.Dispose();
        _schedulerService.Dispose();
        _sessionTimeoutService.Dispose();
        _healthCheckService.Dispose();
        _resourceMonitorService.Dispose();
        _appVersionService.Dispose();
    }

    private async Task RunApplySettingsStateAsync(bool showNotifications)
    {
        try
        {
            await ApplySettingsStateAsync(showNotifications);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply settings state");
            _notificationService.Show(LocalizationService.T("Msg_KioskWarnKeyboardFilterFailed"), NotificationType.Warning);
        }
    }

    private async Task RunApplyKioskStateAsync(bool showNotifications)
    {
        try
        {
            await ApplyKioskStateAsync(showNotifications);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply kiosk state");
            _notificationService.Show(LocalizationService.T("Msg_KioskWarnKeyboardFilterFailed"), NotificationType.Warning);
        }
    }

    private async Task ApplySettingsStateAsync(bool showNotifications)
    {
        _themeService.ApplyTheme(Settings.Theme);
        _localizationService.ApplyLanguage(Settings.Language);
        _kioskService.Status.DeploymentNotes = LocalizationService.T("Settings_KioskDeploymentNotes");
        await ApplyKioskStateAsync(showNotifications);
        _kioskService.RefreshStatus();
    }

    private async Task ApplyKioskStateAsync(bool showNotifications)
    {
        await _kioskStateLock.WaitAsync();
        try
        {
            var requestedState = Settings.KioskMode;
            if (Settings.KioskMode)
            {
                var result = await _kioskService.ActivateAsync();
                if (showNotifications)
                {
                    _notificationService.Show(LocalizationService.T("Msg_KioskActivated"), NotificationType.Info);
                    ShowKioskLimitationsIfNeeded();
                    RecordAudit(
                        "KioskToggled",
                        CreateAuditActor(),
                        "Success",
                        "Enabled",
                        BuildKioskAuditDetails(requestedState, result.Message));
                    RegisterConfigAuditSuppression("settings.kioskmode");
                }
            }
            else
            {
                var result = await _kioskService.DeactivateAsync();
                if (_kioskService.IsActive)
                {
                    RevertKioskModeSetting();
                    if (showNotifications)
                    {
                        ShowKioskDeactivationWarning();
                        RecordAudit(
                            "KioskToggled",
                            CreateAuditActor(),
                            "Blocked",
                            "Disabled",
                            BuildKioskAuditDetails(requestedState, result.Message));
                    }
                    return;
                }

                if (showNotifications)
                {
                    _notificationService.Show(LocalizationService.T("Msg_KioskDeactivated"), NotificationType.Info);
                    RecordAudit(
                        "KioskToggled",
                        CreateAuditActor(),
                        "Success",
                        "Disabled",
                        BuildKioskAuditDetails(requestedState, result.Message));
                    RegisterConfigAuditSuppression("settings.kioskmode");
                }
            }
        }
        catch (Exception)
        {
            if (showNotifications)
            {
                RecordAudit(
                    "KioskToggled",
                    CreateAuditActor(),
                    "Failed",
                    Settings.KioskMode ? "Enabled" : "Disabled",
                    "The kiosk operation threw an exception.");
            }

            throw;
        }
        finally
        {
            _kioskStateLock.Release();
        }
    }

    private string BuildKioskAuditDetails(bool requestedState, string serviceMessage)
    {
        var result = _kioskService.LastMachineLockdownResult;
        return string.Join("; ", new[]
        {
            $"Requested: {(requestedState ? "Enabled" : "Disabled")}",
            $"MachineState: {result.State}",
            string.IsNullOrWhiteSpace(serviceMessage) ? string.Empty : serviceMessage,
            string.IsNullOrWhiteSpace(result.Message) || string.Equals(result.Message, serviceMessage, StringComparison.Ordinal)
                ? string.Empty
                : result.Message
        }.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    private void ShowKioskLimitationsIfNeeded()
    {
        if (!string.IsNullOrWhiteSpace(_kioskService.Status.LimitedProtectionMessage))
        {
            _notificationService.Show(_kioskService.Status.LimitedProtectionMessage, NotificationType.Warning);
        }
    }

    private void ShowKioskDeactivationWarning()
    {
        var message = _kioskService.Status.KeyboardFilterStatusMessage;
        if (string.IsNullOrWhiteSpace(message))
            message = LocalizationService.T("Msg_KioskWarnKeyboardFilterFailed");

        _notificationService.Show(message, NotificationType.Warning);
    }

    private void RevertKioskModeSetting()
    {
        _suppressSettingsNotifications = true;
        try
        {
            Settings.KioskMode = true;
        }
        finally
        {
            _suppressSettingsNotifications = false;
        }
    }

    private void RefreshLocalizedRuntimeState()
    {
        foreach (var app in Apps)
        {
            if (app.State == AppState.Recovering)
            {
                app.HealthStatus = LocalizationService.T("Browser_Recovering");
                continue;
            }
            if (!app.IsRunning)
            {
                app.HealthStatus = string.IsNullOrWhiteSpace(app.HealthCheckUrl)
                    ? LocalizationService.T("Status_Stopped")
                    : LocalizationService.T("Status_Unknown");
                continue;
            }

            if (string.IsNullOrWhiteSpace(app.HealthCheckUrl))
            {
                app.HealthStatus = LocalizationService.T("Status_Ok");
                continue;
            }

            app.HealthStatus = IsLocalizedOrLegacyStatus(app.HealthStatus, "Status_Unhealthy", "Unhealthy")
                               || app.State == AppState.Unhealthy
                ? LocalizationService.T("Status_Unhealthy")
                : LocalizationService.T("Status_Healthy");
        }

        _kioskService.Status.DeploymentNotes =
            LocalizationService.T("Settings_KioskDeploymentNotes");
        _kioskService.RefreshStatus();
    }

    private static bool IsLocalizedOrLegacyStatus(string value, string resourceKey, string legacyValue) =>
        string.Equals(value, LocalizationService.T(resourceKey), StringComparison.CurrentCulture)
        || string.Equals(value, legacyValue, StringComparison.Ordinal);

    private static string MapAccessLevelToAuditRole(AccessLevel accessLevel) =>
        accessLevel switch
        {
            AccessLevel.Admin => "Admin",
            AccessLevel.Operator => "Operator",
            _ => "System"
        };

    private static string GetCurrentWindowsUser()
    {
        var user = Environment.UserName;
        var domain = Environment.UserDomainName;

        if (string.IsNullOrWhiteSpace(user))
            return "Unknown";

        return string.IsNullOrWhiteSpace(domain) ? user : $@"{domain}\{user}";
    }

    private static void SyncCollection<T>(ObservableCollection<T> target, IEnumerable<T> source)
    {
        target.Clear();
        foreach (var item in source)
            target.Add(item);
    }

    private static string GetOperationFailureMessage(Exception ex, string fallbackResourceKey, AppItem app) =>
        ex is AppLifecycleException lifecycleException
            ? lifecycleException.Message
            : LocalizationService.T(fallbackResourceKey, app.Name, ex.Message);

    private static void CopySettings(AppSettings source, AppSettings target)
    {
        foreach (var property in typeof(AppSettings).GetProperties()
                     .Where(property => property.CanRead && property.CanWrite))
        {
            property.SetValue(target, property.GetValue(source));
        }
    }
}
