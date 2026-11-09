namespace AppSwitcher.ViewModels;

public partial class SettingsViewModel : ObservableObject, IDisposable
{
    private readonly MainViewModel _mainVm;
    private readonly ConfigService _configService;
    private readonly SecurityService _securityService;
    private readonly NotificationService _notificationService;
    private readonly ScreenLayoutService _screenLayoutService;
    private readonly CrashHistoryService _crashHistoryService;
    private readonly SessionTimeoutService _sessionTimeoutService;
    private readonly AuditLogService _auditLogService;
    private readonly Dictionary<AppItem, string> _trackedAppNames = [];
    private OperatorScopeSettings? _observedOperatorScope;
    private bool _refreshingOperatorScopeEntries;

    [ObservableProperty] private AppSettings _settings;
    [ObservableProperty] private ObservableCollection<AppItem> _apps;
    [ObservableProperty] private ObservableCollection<GroupDefinition> _groups;
    [ObservableProperty] private ObservableCollection<AppItem> _visibleApps = [];
    [ObservableProperty] private ObservableCollection<GroupDefinition> _visibleGroups = [];
    [ObservableProperty] private ObservableCollection<ScreenLayout> _layouts;
    [ObservableProperty] private ObservableCollection<ConfigService.ConfigBackupInfo> _configBackups = [];
    [ObservableProperty] private ObservableCollection<AuditLogEntry> _auditEntries = [];
    [ObservableProperty] private AppItem? _selectedApp;
    [ObservableProperty] private bool _schedulesExpanded;
    [ObservableProperty] private bool _startupOptionsExpanded;
    [ObservableProperty] private bool _healthChecksExpanded;
    [ObservableProperty] private bool _workingFolderExpanded;
    [ObservableProperty] private bool _connectionOptionsExpanded;
    [ObservableProperty] private bool _lifecycleScriptsExpanded;
    [ObservableProperty] private string _newGroupName = "";
    [ObservableProperty] private string _newLayoutName = "";
    [ObservableProperty] private string _activeSection = "Applications";
    [ObservableProperty] private string _saveStatus = LocalizationService.T("UI_SaveHint");
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(RestartLauncherCommand))]
    [NotifyPropertyChangedFor(nameof(ShowSaveAction), nameof(CanSave))]
    private bool _hasInputErrors;

    public MainViewModel Runtime => _mainVm;
    public ObservableCollection<LocalizedOption<AppTargetKind>> TargetKindChoices { get; } = [];
    public Array BrowserChoices { get; } = Enum.GetValues<BrowserKind>();
    [ObservableProperty] private ObservableCollection<BrowserProfileChoice> _browserProfiles = [];
    [ObservableProperty] private BrowserProfileChoice? _selectedBrowserProfile;
    private bool _syncingBrowserProfile;

    partial void OnSelectedBrowserProfileChanged(BrowserProfileChoice? value)
    {
        if (_syncingBrowserProfile || !CanEditApps || SelectedApp is not { } app || value == null) return;
        app.Browser = value.Browser;
        app.BrowserProfileId = value.ProfileId;
    }

    [RelayCommand(CanExecute = nameof(CanEditApps))]
    private void RefreshBrowserProfiles()
    {
        _syncingBrowserProfile = true;
        try
        {
            var app = SelectedApp;
            BrowserProfiles = new(_mainVm.BrowserProfiles.Where(profile => app == null || profile.Browser == app.Browser));
            var matches = BrowserProfiles.Where(profile => profile.ProfileId == app?.BrowserProfileId).ToArray();
            // Keep the configured identity visible offline; refreshing never changes ownership.
            if (matches.Length == 0 && app != null && !string.IsNullOrWhiteSpace(app.BrowserProfileId))
            {
                var offline = new BrowserProfileChoice(app.BrowserProfileId, app.Browser, LocalizationService.T("Browser_SavedOffline"));
                BrowserProfiles.Add(offline);
                SelectedBrowserProfile = offline;
            }
            else SelectedBrowserProfile = matches.Length == 1 ? matches[0] : null;
        }
        finally { _syncingBrowserProfile = false; }
    }

    [RelayCommand(CanExecute = nameof(CanEditApps))]
    private void ConvertToWebsite()
    {
        if (SelectedApp is not { } app) return;
        var url = WebsiteConfiguration.ExtractUrl(app);
        if (url == null) { _notificationService.Show(LocalizationService.T("Browser_InvalidUrl"), NotificationType.Error); return; }
        app.WebsiteUrl = url;
        app.TargetKind = AppTargetKind.Website;
        app.PlacementMode = WindowPlacementMode.None;
        app.ForcedMonitor = 0;
        app.ForcedMonitorId = "";
        app.AlwaysOnTop = false;
        app.ReapplyPlacementContinuously = false;
        RefreshBrowserProfiles();
    }
    public bool CanSave => CanEditApps && HasUnsavedChanges && !HasInputErrors;
    public bool LastSaveSucceeded { get; private set; }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task Save()
    {
        LastSaveSucceeded = false;
        if (!CanSave) return;
        try
        {
            await _mainVm.SaveConfigAsync();
            LastSaveSucceeded = true;
            RefreshSaveState();
            if (!HasUnsavedChanges && !HasInputErrors)
                SaveStatus = LocalizationService.T("UI_Saved", DateTime.Now.ToShortTimeString());
        }
        catch (Exception)
        {
            // SaveConfigAsync reports the cause; keep the editor open for correction.
            SaveStatus = LocalizationService.T("UI_SaveFailed");
        }
    }

    [RelayCommand(CanExecute = nameof(CanEditApps))]
    private void MoveApp(string direction)
    {
        if (!CanEditApps || SelectedApp is not { } app || !int.TryParse(direction, out var offset) || offset is not (-1 or 1))
            return;
        var index = Apps.IndexOf(app);
        if (index < 0 || index + offset < 0 || index + offset >= Apps.Count)
            return;
        Apps.Move(index, index + offset);
        SelectedApp = app;
    }

    public AccessLevel AccessLevel { get; }
    public bool IsAdmin => AccessLevel == AccessLevel.Admin;
    public bool CanManageApps => AccessLevel != AccessLevel.None;
    public bool CanEditApps => AccessLevel == AccessLevel.Admin;
    public bool CanEditSecurity => AccessLevel == AccessLevel.Admin;
    public bool CanEditKiosk => AccessLevel == AccessLevel.Admin;
    public bool CanEditDiagnostics => AccessLevel == AccessLevel.Admin;
    public bool CanViewDiagnostics => AccessLevel == AccessLevel.Admin;
    public bool IsOperatorPinConfigured => !string.IsNullOrEmpty(Settings.OperatorPasswordHash);
    public int MonitorCount { get; } = MonitorHelper.GetMonitorWorkAreas().Count;
    public ObservableCollection<LocalizedOption<int>> MonitorChoices { get; } = [];
    public ObservableCollection<LocalizedOption<DisplayMode>> DisplayModeChoices { get; } = [];
    public ObservableCollection<LocalizedOption<int>> WindowPolicyMonitorChoices { get; } = [];
    public ObservableCollection<LocalizedOption<WindowPlacementMode>> WindowPlacementChoices { get; } = [];
    public ObservableCollection<OperatorScopeOption> OperatorScopeAppEntries { get; } = [];
    public ObservableCollection<OperatorScopeOption> OperatorScopeGroupEntries { get; } = [];
    public List<LocalizationService.LanguageInfo> LanguageChoices { get; } = LocalizationService.SupportedLanguages;
    public KioskStatus KioskStatus => _mainVm.KioskService.Status;
    public bool IsSessionTimeoutActive => _mainVm.IsSessionTimeoutActive;
    public string AccessLevelLabel => LocalizationService.T(AccessLevel switch
    {
        AccessLevel.Admin => "Diag_AccessAdmin",
        AccessLevel.Operator => "Diag_AccessOperator",
        _ => "Diag_AccessNone"
    });
    public string SessionTimeoutLabel => LocalizationService.T("UI_SessionStatus", IsSessionTimeoutActive
        ? LocalizationService.T("Diag_SessionTimeoutActive", Settings.SessionTimeoutMinutes)
        : LocalizationService.T("Diag_SessionTimeoutDisabled"));

    public SettingsViewModel(
        MainViewModel mainVm,
        ConfigService configService,
        SecurityService securityService,
        NotificationService notificationService,
        ScreenLayoutService screenLayoutService,
        CrashHistoryService crashHistoryService,
        SessionTimeoutService sessionTimeoutService,
        AccessLevel accessLevel = AccessLevel.Admin)
    {
        _mainVm = mainVm;
        _configService = configService;
        _securityService = securityService;
        _notificationService = notificationService;
        _screenLayoutService = screenLayoutService;
        _crashHistoryService = crashHistoryService;
        _sessionTimeoutService = sessionTimeoutService;
        _auditLogService = mainVm.AuditLogService;
        AccessLevel = accessLevel;

        _settings = mainVm.Settings;
        _apps = mainVm.Apps;
        _groups = mainVm.Groups;
        _layouts = mainVm.Layouts;

        RefreshLocalizedChoices();
        Settings.PropertyChanged += OnSettingsPropertyChanged;
        _mainVm.PropertyChanged += OnMainVmPropertyChanged;
        KioskStatus.PropertyChanged += OnKioskStatusChanged;
        _auditLogService.EntryRecorded += OnAuditEntryRecorded;
        Apps.CollectionChanged += OnAppsCollectionChanged;
        Groups.CollectionChanged += OnGroupsCollectionChanged;
        Layouts.CollectionChanged += OnLayoutsCollectionChanged;
        RefreshWorkspaceCards();
        SetObservedOperatorScope(Settings.OperatorScope);
        TrackApps(Apps);

        UpdateGroupDisplayNames();
        RefreshVisibleCollections();
        RefreshOperatorScopeEntries();
        RefreshBackups();
        RefreshAuditEntries();
        SelectedApp = VisibleApps.FirstOrDefault();
        if (IsAdmin && _configService.IsFirstRun) ActiveSection = "Setup";
        RefreshSaveState();
        SaveCommand.PropertyChanged += OnSaveCommandChanged;
        _mainVm.BeginEditing();
    }

    public List<GroupDefinition> GroupChoices
    {
        get
        {
            var none = new GroupDefinition { Key = "" };
            none.UpdateDisplayName(Settings.Language);
            var list = new List<GroupDefinition> { none };
            list.AddRange(VisibleGroups);
            return list;
        }
    }

    public List<string> PresetColors { get; } =
    [
        "#e74c3c", "#e67e22", "#f1c40f", "#2ecc71", "#1abc9c", "#3498db", "#9b59b6", "#34495e",
        "#c0392b", "#d35400", "#f39c12", "#27ae60", "#16a085", "#2980b9", "#8e44ad", "#2c3e50",
        "#ecf0f1", "#95a5a6", "#7f8c8d", "#000000", "#1e2028", "#252836", "#4ecdc4", "#ff6b6b"
    ];

    partial void OnSelectedAppChanged(AppItem? value)
    {
        RefreshMonitorChoices();
        SelectedBrowserProfile = null;
        BrowserDiagnostic = "";
        RefreshBrowserProfiles();
        SchedulesExpanded = value != null && new[] { value.ScheduleTime, value.ScheduleRestartTime, value.ScheduleShutdownTime }.Any(time => !string.IsNullOrWhiteSpace(time));
        StartupOptionsExpanded = value != null && (value.Readiness != ReadinessMode.None || !string.IsNullOrWhiteSpace(value.DependsOn) || value.LaunchDelay != 0);
        HealthChecksExpanded = value != null && (!string.IsNullOrWhiteSpace(value.HealthCheckUrl) || value.MemoryThresholdMB != 0);
        WorkingFolderExpanded = !string.IsNullOrWhiteSpace(value?.WorkingDirectory);
        ConnectionOptionsExpanded = value != null && (!string.IsNullOrWhiteSpace(value.BrowserExecutablePath) || !string.IsNullOrWhiteSpace(value.BrowserProfileDirectory));
        LifecycleScriptsExpanded = value != null && new[] { value.PreLaunchHookPath, value.PostLaunchHookPath, value.PreStopHookPath, value.PostStopHookPath }.Any(path => !string.IsNullOrWhiteSpace(path));
        if (value != null)
        {
            ActiveSection = "Applications";
        }
    }

    private void TrackApps(IEnumerable<AppItem> apps)
    {
        foreach (var app in apps)
            TrackApp(app);
    }

    private void TrackApp(AppItem app)
    {
        if (_trackedAppNames.ContainsKey(app)) return;
        _trackedAppNames[app] = app.Name;
        app.PropertyChanged += OnAppPropertyChanged;
    }

    private void UntrackApp(AppItem app)
    {
        app.PropertyChanged -= OnAppPropertyChanged;
        _trackedAppNames.Remove(app);
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
        OnPropertyChanged(nameof(AllowAllOperatorApps));
        RefreshSaveState();
        if (e.PropertyName == nameof(OperatorScopeSettings.AllowedAppNames)
            || e.PropertyName == nameof(OperatorScopeSettings.AllowedGroupKeys))
        {
            RefreshVisibleCollections();
            RefreshOperatorScopeEntries();
        }
    }

    private void OnAppsCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
        {
            foreach (var app in _trackedAppNames.Keys.ToList()) UntrackApp(app);
            TrackApps(Apps);
        }
        if (e.OldItems != null)
        {
            foreach (var app in e.OldItems.OfType<AppItem>())
                UntrackApp(app);
        }

        if (e.NewItems != null)
        {
            foreach (var app in e.NewItems.OfType<AppItem>())
                TrackApp(app);
        }

        RefreshVisibleCollections();
        RefreshOperatorScopeEntries();
        RefreshSaveState();
    }

    private void OnGroupsCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        UpdateGroupDisplayNames();
        RefreshVisibleCollections();
        RefreshOperatorScopeEntries();
        RefreshSaveState();
    }

    private void OnAppPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (!_syncingMonitorSelection && e.PropertyName is nameof(AppItem.ForcedMonitor) or nameof(AppItem.ForcedMonitorId)) RefreshMonitorChoices();
        if (!_syncingBrowserProfile && ReferenceEquals(sender, SelectedApp)
            && e.PropertyName is nameof(AppItem.Browser) or nameof(AppItem.BrowserProfileId))
            RefreshBrowserProfiles();
        if (sender is not AppItem app)
            return;

        if (e.PropertyName == nameof(AppItem.TargetKind) && app.IsWebsite)
        {
            app.Args = "";
            app.MemoryThresholdMB = 0;
            app.PlacementMode = WindowPlacementMode.None;
            app.ForcedMonitor = 0;
            app.ForcedMonitorId = "";
            app.AlwaysOnTop = false;
            app.ReapplyPlacementContinuously = false;
            if (app.Readiness == ReadinessMode.Window) app.Readiness = ReadinessMode.Process;
        }

        if (e.PropertyName != nameof(AppItem.Name) && (e.PropertyName == null || PersistedAppProperties.Contains(e.PropertyName)))
            RefreshSaveState();

        if (e.PropertyName == nameof(AppItem.Name))
        {
            var previousName = _trackedAppNames.GetValueOrDefault(app, "");
            if (!string.IsNullOrWhiteSpace(app.Name) && !string.Equals(previousName, app.Name, StringComparison.Ordinal)
                && !Apps.Any(other => !ReferenceEquals(other, app) && string.Equals(other.Name.Trim(), app.Name.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                _trackedAppNames[app] = app.Name;
                Settings.OperatorScope.RenameAppName(previousName, app.Name);
                foreach (var dependent in Apps.Where(item => !string.IsNullOrWhiteSpace(previousName)
                    && string.Equals(item.DependsOn.Trim(), previousName.Trim(), StringComparison.OrdinalIgnoreCase)))
                    dependent.DependsOn = app.Name.Trim();
                foreach (var position in Layouts.SelectMany(layout => layout.Positions).Where(position =>
                    position.AppId == app.AppId || (string.IsNullOrEmpty(position.AppId) && position.AppName == previousName)))
                { position.AppId = app.AppId; position.AppName = app.Name; }
                RefreshOperatorScopeEntries();
            }
            RefreshSaveState();
        }

        if (e.PropertyName == nameof(AppItem.Name)
            || e.PropertyName == nameof(AppItem.Group))
        {
            RefreshVisibleCollections();
        }
    }

    private void OnMainVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.HasUnsavedConfiguration))
        {
            ResetEditorHistory();
            RefreshSaveState();
        }
        if (e.PropertyName == nameof(MainViewModel.CurrentAccessLevel))
        {
            AddFromOpenWindowCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(AccessLevelLabel));
        }
        else if (e.PropertyName == nameof(MainViewModel.IsSessionTimeoutActive))
            OnPropertyChanged(nameof(SessionTimeoutLabel));
    }

    private void OnKioskStatusChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(KioskStatus.LastSelfHealAction) ||
            e.PropertyName == nameof(KioskStatus.LastSelfHealResult) ||
            e.PropertyName == nameof(KioskStatus.LastBlockedSurface) ||
            e.PropertyName == nameof(KioskStatus.LastAllowedForeground) ||
            e.PropertyName == nameof(KioskStatus.ShellInterventionCount) ||
            e.PropertyName == nameof(KioskStatus.LimitedProtectionMessage) ||
            e.PropertyName == nameof(KioskStatus.KeyboardFilterSupported) ||
            e.PropertyName == nameof(KioskStatus.KeyboardFilterConfigured) ||
            e.PropertyName == nameof(KioskStatus.KeyboardFilterRestartRequired) ||
            e.PropertyName == nameof(KioskStatus.KeyboardFilterStatusMessage))
        {
            OnPropertyChanged(nameof(KioskStatus));
        }
    }

    private void UpdateGroupDisplayNames()
    {
        foreach (var g in Groups)
        {
            g.UpdateDisplayName(Settings.Language);
            if (g.NameEntries.Count == 0)
                g.BuildNameEntries(LocalizationService.SupportedLanguages);
        }
        TrackGroupNames();
        OnPropertyChanged(nameof(GroupChoices));
    }

    private void RefreshVisibleCollections()
    {
        SyncCollection(VisibleApps, _mainVm.GetVisibleAppsForAccess(AccessLevel));
        SyncCollection(VisibleGroups, _mainVm.GetVisibleGroupsForAccess(AccessLevel));

        if (SelectedApp != null && !VisibleApps.Contains(SelectedApp))
        {
            SelectedApp = null;
        }

        OnPropertyChanged(nameof(GroupChoices));
    }

    private void RefreshOperatorScopeEntries()
    {
        _refreshingOperatorScopeEntries = true;
        try
        {
            var selectedAppNames = Settings.OperatorScope.AllowedAppNames
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var selectedGroupKeys = Settings.OperatorScope.AllowedGroupKeys
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            ReplaceScopeEntries(
                OperatorScopeAppEntries,
                Apps.Where(app => !string.IsNullOrWhiteSpace(app.Name))
                    .OrderBy(app => app.Name)
                    .Select(app => new OperatorScopeOption
                    {
                        Key = app.Name.Trim(),
                        Label = app.Name.Trim(),
                        IsSelected = selectedAppNames.Contains(app.Name.Trim())
                    }),
                OnOperatorScopeAppEntryPropertyChanged);

            ReplaceScopeEntries(
                OperatorScopeGroupEntries,
                Groups.Where(group => !string.IsNullOrWhiteSpace(group.Key))
                    .OrderBy(group => group.GetDisplayName(Settings.Language))
                    .Select(group => new OperatorScopeOption
                    {
                        Key = group.Key.Trim(),
                        Label = group.GetDisplayName(Settings.Language),
                        IsSelected = selectedGroupKeys.Contains(group.Key.Trim())
                    }),
                OnOperatorScopeGroupEntryPropertyChanged);
        }
        finally
        {
            _refreshingOperatorScopeEntries = false;
        }
    }

    public bool AllowAllOperatorApps
    {
        get => !Settings.OperatorScope.HasRestrictions;
        set
        {
            if (!CanEditSecurity || !_mainVm.CanAdminister) return;
            if (value)
            {
                Settings.OperatorScope.AllowedAppNames = [];
                Settings.OperatorScope.AllowedGroupKeys = [];
            }
            Settings.OperatorScope.Restricted = !value;
            OnPropertyChanged(nameof(AllowAllOperatorApps));
        }
    }

    private static void ReplaceScopeEntries(
        ObservableCollection<OperatorScopeOption> target,
        IEnumerable<OperatorScopeOption> source,
        System.ComponentModel.PropertyChangedEventHandler propertyChangedHandler)
    {
        var previous = target.GroupBy(entry => entry.Key, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var desired = source.GroupBy(entry => entry.Key, StringComparer.Ordinal).Select(group => group.First()).Select(entry =>
        {
            if (previous.TryGetValue(entry.Key, out var existing) && existing.Label == entry.Label)
            { existing.IsSelected = entry.IsSelected; return existing; }
            entry.PropertyChanged += propertyChangedHandler;
            return entry;
        }).ToArray();
        var retained = desired.ToHashSet();
        foreach (var entry in target.Where(entry => !retained.Contains(entry)).ToArray())
        { entry.PropertyChanged -= propertyChangedHandler; target.Remove(entry); }
        for (var index = 0; index < desired.Length; index++)
        {
            if (index < target.Count && ReferenceEquals(target[index], desired[index])) continue;
            var oldIndex = target.IndexOf(desired[index]);
            if (oldIndex >= 0) target.Move(oldIndex, index);
            else target.Insert(index, desired[index]);
        }
    }

    private void OnOperatorScopeAppEntryPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_refreshingOperatorScopeEntries || e.PropertyName != nameof(OperatorScopeOption.IsSelected))
            return;

        Settings.OperatorScope.SetAllowedAppNames(
            OperatorScopeAppEntries
                .Where(entry => entry.IsSelected)
                .Select(entry => entry.Key));
    }

    private void OnOperatorScopeGroupEntryPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_refreshingOperatorScopeEntries || e.PropertyName != nameof(OperatorScopeOption.IsSelected))
            return;

        Settings.OperatorScope.SetAllowedGroupKeys(
            OperatorScopeGroupEntries
                .Where(entry => entry.IsSelected)
                .Select(entry => entry.Key));
    }

    private static void SyncCollection<T>(ObservableCollection<T> target, IEnumerable<T> source)
    {
        var desired = source.ToArray();
        if (target.SequenceEqual(desired)) return;
        target.Clear();
        foreach (var item in desired)
            target.Add(item);
    }

    private void OnSettingsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (!_syncingMonitorSelection && e.PropertyName is nameof(AppSettings.PreferredMonitor) or nameof(AppSettings.PreferredMonitorId)) RefreshMonitorChoices();
        RefreshSaveState();
        if (e.PropertyName == nameof(AppSettings.Language))
        {
            RefreshAuditEntries();
            RefreshBrowserProfiles();
            OnSettingsSearchChanged(SettingsSearch);
            RefreshLocalizedChoices();
            UpdateGroupDisplayNames();
            RefreshOperatorScopeEntries();
            OnPropertyChanged(nameof(AccessLevelLabel));
            OnPropertyChanged(nameof(SessionTimeoutLabel));
        }
        else if (e.PropertyName == nameof(AppSettings.OperatorScope))
        {
            SetObservedOperatorScope(Settings.OperatorScope);
            RefreshVisibleCollections();
            RefreshOperatorScopeEntries();
        }
        else if (e.PropertyName == nameof(AppSettings.SessionTimeoutMinutes))
        {
            OnPropertyChanged(nameof(SessionTimeoutLabel));
        }
    }

    private void RefreshLocalizedChoices()
    {
        // Relabel existing choices: clearing a bound list can write -1/null back
        // through SelectedIndex/SelectedValue and silently change configuration.
        UpdateChoices(ReadinessChoices, Enum.GetValues<ReadinessMode>().Select(mode => new LocalizedOption<ReadinessMode>(mode, LocalizationService.T("Readiness_" + mode))));
        UpdateChoices(TargetKindChoices, [new(AppTargetKind.Native, LocalizationService.T("Browser_Native")), new(AppTargetKind.Website, LocalizationService.T("Browser_Website"))]);
        RefreshMonitorChoices();
        UpdateChoices(DisplayModeChoices, [new(DisplayMode.Combined, LocalizationService.T("DisplayMode_Combined")),
            new(DisplayMode.IconOnly, LocalizationService.T("DisplayMode_IconOnly")), new(DisplayMode.TextOnly, LocalizationService.T("DisplayMode_TextOnly"))]);
        UpdateChoices(WindowPlacementChoices, [new(WindowPlacementMode.None, LocalizationService.T("PlacementMode_None")),
            new(WindowPlacementMode.Maximize, LocalizationService.T("PlacementMode_Maximize")), new(WindowPlacementMode.Fullscreen, LocalizationService.T("PlacementMode_Fullscreen"))]);
    }

    private static void UpdateChoices<T>(ObservableCollection<LocalizedOption<T>> target, IEnumerable<LocalizedOption<T>> choices)
    {
        var index = 0;
        foreach (var choice in choices)
        {
            if (index < target.Count && EqualityComparer<T>.Default.Equals(target[index].Value, choice.Value))
                target[index].Label = choice.Label;
            else if (index < target.Count) target[index] = choice;
            else target.Add(choice);
            index++;
        }
        while (target.Count > index) target.RemoveAt(target.Count - 1);
    }

    [RelayCommand(CanExecute = nameof(CanEditApps))]
    private void AddApp()
    {
        var newApp = new AppItem { Name = BuildUniqueAppName(LocalizationService.T("Msg_NewApp")), RequireConfirmation = true };
        Apps.Add(newApp);
        SelectedApp = newApp;
    }

    [RelayCommand(CanExecute = nameof(CanEditApps))]
    private void DeleteApp()
    {
        if (!CanEditApps || SelectedApp is null) return;
        var deleted = SelectedApp;
        var dependents = Apps.Where(app => app.DependsOn.Trim().Equals(deleted.Name.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        var affectedLayouts = Layouts.Where(layout => layout.AppIds.Contains(deleted.AppId)
            || layout.Positions.Any(position => position.AppId == deleted.AppId || position.AppName == deleted.Name)).ToList();
        var details = LocalizationService.T("UI_DeleteReferences", string.Join(", ", dependents.Select(app => app.Name)), string.Join(", ", affectedLayouts.Select(layout => layout.Name)));
        if (!_mainVm.ConfirmOperation(LocalizationService.T("UI_DeleteAppConfirm", deleted.Name), details, destructive: true) || !_mainVm.CanAdminister) return;
        foreach (var dependent in dependents) dependent.DependsOn = "";
        foreach (var layout in affectedLayouts)
        {
            layout.AppIds.RemoveAll(id => id == deleted.AppId);
            layout.Positions.RemoveAll(position => position.AppId == deleted.AppId || position.AppName == deleted.Name);
        }
        Settings.OperatorScope.RemoveAppName(deleted.Name);
        Apps.Remove(deleted);
        SelectedApp = null;
        RefreshOperatorScopeEntries();
    }

    [RelayCommand]
    private void BrowseExe()
    {
        if (!CanEditApps || SelectedApp is null) return;

        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = LocalizationService.T("Filter_Executables") };
        if (dlg.ShowDialog() == true)
        {
            SelectedApp.Path = dlg.FileName;
            if (string.IsNullOrEmpty(SelectedApp.Name) || SelectedApp.Name == LocalizationService.T("Msg_NewApp"))
                SelectedApp.Name = Path.GetFileNameWithoutExtension(dlg.FileName);
        }
    }

    [RelayCommand]
    private void BrowseIcon()
    {
        if (!CanEditApps || SelectedApp is null) return;

        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = LocalizationService.T("Filter_ImagesIcons")
        };
        if (dlg.ShowDialog() == true)
            SelectedApp.CustomIconPath = dlg.FileName;
    }

    [RelayCommand]
    private void BrowseWorkingDirectory()
    {
        if (!CanEditApps || SelectedApp is null) return;

        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = LocalizationService.T("Msg_SelectWorkDir")
        };
        if (dlg.ShowDialog() == true)
            SelectedApp.WorkingDirectory = dlg.FolderName;
    }

    [RelayCommand]
    private void BrowseHookPath(string? propertyName)
    {
        if (!CanEditApps || SelectedApp is null || string.IsNullOrWhiteSpace(propertyName))
            return;

        var property = typeof(AppItem).GetProperty(propertyName);
        if (property == null || property.PropertyType != typeof(string) || !property.CanWrite)
            return;

        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = LocalizationService.T("Filter_HookScripts")
        };

        if (dlg.ShowDialog() == true)
            property.SetValue(SelectedApp, dlg.FileName);
    }

    [RelayCommand]
    private void SetBackgroundColor(string hex)
    {
        if (ActiveSection == "Applications") SetAppColor(hex);
        else SetGlobalColor(hex);
    }

    [RelayCommand]
    private void SetGlobalColor(string hex)
    {
        if (CanEditApps && !string.IsNullOrEmpty(hex))
            Settings.BgColor = hex;
    }

    [RelayCommand]
    private void SetFgColor(string hex)
    {
        if (CanEditApps && !string.IsNullOrEmpty(hex))
            Settings.FgColor = hex;
    }

    [RelayCommand]
    private void SetBorderColor(string hex)
    {
        if (CanEditApps && !string.IsNullOrEmpty(hex))
            Settings.BorderColor = hex;
    }

    [RelayCommand]
    private void SetAppColor(string hex)
    {
        if (CanEditApps && !string.IsNullOrEmpty(hex) && SelectedApp != null)
            SelectedApp.BgColor = hex;
    }

    [RelayCommand]
    private void AddGroup()
    {
        if (!CanEditApps) return;

        var name = NewGroupName?.Trim();
        if (string.IsNullOrEmpty(name)) return;
        if (Groups.Any(g => g.Key.Equals(name, StringComparison.OrdinalIgnoreCase))) return;

        var names = new Dictionary<string, string>();
        foreach (var lang in LocalizationService.SupportedLanguages)
            names[lang.Code] = name;

        var def = new GroupDefinition { Key = name, Names = names };
        def.UpdateDisplayName(Settings.Language);
        def.BuildNameEntries(LocalizationService.SupportedLanguages);
        Groups.Add(def);
        OnPropertyChanged(nameof(GroupChoices));
        NewGroupName = "";
        RefreshOperatorScopeEntries();
    }

    [RelayCommand]
    private void DeleteGroup(GroupDefinition? group)
    {
        if (!CanEditApps || group == null || string.IsNullOrEmpty(group.Key)) return;
        var members = Apps.Where(app => app.Group == group.Key).Select(app => app.Name).ToArray();
        var workspaces = Layouts.Where(layout => layout.GroupKeys.Contains(group.Key)).Select(layout => layout.Name).ToArray();
        if (!_mainVm.ConfirmOperation(LocalizationService.T("UI_DeleteGroupConfirm", group.GetDisplayName(Settings.Language)),
            LocalizationService.T("UI_DeleteGroupReferences", string.Join(", ", members), string.Join(", ", workspaces)), destructive: true)) return;

        Settings.OperatorScope.RemoveGroupKey(group.Key);
        Groups.Remove(group);
        OnPropertyChanged(nameof(GroupChoices));
        foreach (var app in Apps.Where(a => a.Group == group.Key))
            app.Group = "";
        foreach (var layout in Layouts) layout.GroupKeys.RemoveAll(key => key == group.Key);
        RefreshOperatorScopeEntries();
    }

    [RelayCommand]
    private void SetAdminPin()
    {
        if (!CanEditSecurity || !_mainVm.CanAdminister) return;

        var dialog = new PasswordDialog { IsSetMode = true };
        _mainVm.PrepareDialog(dialog, privileged: true);
        if (dialog.ShowDialog() == true && _mainVm.CanAdminister)
        {
            Settings.AdminPasswordHash = _securityService.HashPassword(dialog.Password);
            _notificationService.Show(LocalizationService.T("Msg_PinSet"), NotificationType.Info);
        }
    }

    [RelayCommand]
    private void RemoveAdminPin()
    {
        if (!CanEditSecurity || !_mainVm.CanAdminister || string.IsNullOrEmpty(Settings.AdminPasswordHash)) return;

        var dialog = new PasswordDialog();
        _mainVm.PrepareDialog(dialog, privileged: true);
        if (dialog.ShowDialog() == true && _mainVm.CanAdminister)
        {
            if (_securityService.GetAccessLevel(dialog.Password, Settings.AdminPasswordHash, "") == AccessLevel.Admin)
            {
                Settings.AdminPasswordHash = "";
                _notificationService.Show(LocalizationService.T("Msg_PinRemoved"), NotificationType.Info);
            }
            else
            {
                _notificationService.Show(_securityService.AuthenticationFailureMessage, NotificationType.Error);
            }
        }
    }

    [RelayCommand]
    private void SetOperatorPin()
    {
        if (!CanEditSecurity || !_mainVm.CanAdminister) return;

        var dialog = new PasswordDialog { IsSetMode = true };
        _mainVm.PrepareDialog(dialog, privileged: true);
        if (dialog.ShowDialog() == true && _mainVm.CanAdminister)
        {
            Settings.OperatorPasswordHash = _securityService.HashPassword(dialog.Password);
            OnPropertyChanged(nameof(IsOperatorPinConfigured));
            _notificationService.Show(LocalizationService.T("Msg_OperatorPinSet"), NotificationType.Info);
        }
    }

    [RelayCommand]
    private void RemoveOperatorPin()
    {
        if (!CanEditSecurity || !_mainVm.CanAdminister) return;

        Settings.OperatorPasswordHash = "";
        OnPropertyChanged(nameof(IsOperatorPinConfigured));
        _notificationService.Show(LocalizationService.T("Msg_OperatorPinRemoved"), NotificationType.Info);
    }

    [RelayCommand]
    private void SaveLayout()
    {
        if (!CanEditApps) return;

        var name = NewLayoutName?.Trim();
        if (string.IsNullOrEmpty(name)) return;

        var layout = _screenLayoutService.CaptureLayout(name, Apps);
        if (!string.IsNullOrWhiteSpace(WorkspaceGroupKey)) layout.GroupKeys.Add(WorkspaceGroupKey);
        if (layout.AppIds.Count == 0 && layout.GroupKeys.Count == 0)
        {
            _notificationService.Show(LocalizationService.T("Msg_NoRunningApps"), NotificationType.Warning);
            return;
        }

        var existing = Layouts.FirstOrDefault(l => l.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            var choices = WorkspaceChoices;
            if (!_mainVm.ConfirmOperation(LocalizationService.T("Workspace_ReplaceConfirm", existing.Name),
                LocalizationService.T("Workspace_ReplaceDetails", WorkspaceEditor.Summary(existing, Apps, choices), WorkspaceEditor.Summary(layout, Apps, choices)))) return;
            Layouts.Remove(existing);
        }

        Layouts.Add(layout);
        NewLayoutName = "";
        _notificationService.Show(LocalizationService.T("Msg_LayoutSaved", name), NotificationType.Info);
    }

    [RelayCommand]
    private void RestoreLayout(ScreenLayout? layout)
    {
        if (!CanEditApps || layout == null) return;
        if (!_mainVm.ConfirmOperation(LocalizationService.T("UI_RestoreLayoutConfirm", layout.Name))) return;

        _screenLayoutService.RestoreLayout(layout, Apps);
        _notificationService.Show(LocalizationService.T("Msg_LayoutRestored", layout.Name), NotificationType.Info);
    }

    [RelayCommand]
    private void DeleteLayout(ScreenLayout? layout)
    {
        if (!CanEditApps || layout == null) return;
        if (!_mainVm.ConfirmOperation(LocalizationService.T("UI_DeleteLayoutConfirm", layout.Name),
            LocalizationService.T("Workspace_DeleteDetails", WorkspaceEditor.Summary(layout, Apps, WorkspaceChoices)), destructive: true)) return;

        Layouts.Remove(layout);
        _notificationService.Show(LocalizationService.T("Msg_LayoutDeleted", layout.Name), NotificationType.Info);
    }

    [RelayCommand(CanExecute = nameof(CanEditSecurity))]
    private async Task ExportConfig()
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = LocalizationService.T("Filter_JsonConfig"),
            FileName = "appswitcher_config.json"
        };
        if (dlg.ShowDialog() == true)
        {
            var root = new ConfigRoot
            {
                Settings = Settings,
                Groups = [.. Groups],
                Apps = [.. Apps],
                Layouts = [.. Layouts]
            };
            await _configService.ExportAsync(dlg.FileName, root);
            _notificationService.Show(LocalizationService.T("Msg_ConfigExported"), NotificationType.Info);
        }
    }

    [RelayCommand(CanExecute = nameof(CanEditSecurity))]
    private async Task ImportConfig()
    {
        if (!CanEditSecurity || !_mainVm.CanAdminister) return;
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = LocalizationService.T("Filter_JsonConfig")
        };
        if (dlg.ShowDialog() == true)
        {
            var config = await _configService.ImportAsync(dlg.FileName);
            if (!_mainVm.CanAdminister) return;
            if (config != null)
            {
                config.Settings.AdminPasswordHash = Settings.AdminPasswordHash;
                config.Settings.OperatorPasswordHash = Settings.OperatorPasswordHash;
                if (!_mainVm.ConfirmOperation(LocalizationService.T("UI_ImportConfirm", config.Apps.Count, config.Groups.Count, config.Layouts.Count), _mainVm.DescribeConfigChanges(config))
                    || !_mainVm.CanAdminister) return;

                try
                {
                    await _mainVm.ApplyConfigAsync(config);
                    _notificationService.Show(LocalizationService.T("Msg_ConfigImported"), NotificationType.Info);
                }
                catch (InvalidOperationException)
                {
                    _notificationService.Show(LocalizationService.T("Msg_ImportFailed"), NotificationType.Error);
                }
            }
            else
            {
                _notificationService.Show(LocalizationService.T("Msg_ImportFailed"), NotificationType.Error);
            }
        }
    }

    private void RefreshBackups()
    {
        var backups = _configService.GetBackups();
        ConfigBackups = new ObservableCollection<ConfigService.ConfigBackupInfo>(backups);
    }

    private void RefreshAuditEntries()
    {
        AuditEntries = new ObservableCollection<AuditLogEntry>(_auditLogService.GetRecentEntries());
    }

    private void OnAuditEntryRecorded(AuditLogEntry entry)
    {
        UiDispatcher.TryBeginInvoke(() =>
        {
            AuditEntries.Insert(0, entry);
            while (AuditEntries.Count > 200)
                AuditEntries.RemoveAt(AuditEntries.Count - 1);
        });
    }

    [RelayCommand]
    private async Task RestoreBackup(ConfigService.ConfigBackupInfo? backup)
    {
        if (!CanEditApps || !_mainVm.CanAdminister || backup == null) return;
        var preview = await _configService.ReadBackupPreviewAsync(backup.FilePath);
        if (!_mainVm.CanAdminister) return;
        if (preview == null)
        {
            _notificationService.Show(LocalizationService.T("Msg_BackupRestoreFailed"), NotificationType.Error);
            return;
        }
        preview.Config.Settings.AdminPasswordHash = Settings.AdminPasswordHash;
        preview.Config.Settings.OperatorPasswordHash = Settings.OperatorPasswordHash;
        var details = backup.FileName + Environment.NewLine + _mainVm.DescribeConfigChanges(preview.Config);
        if (!_mainVm.ConfirmOperation(LocalizationService.T("UI_RestoreBackupConfirm", backup.Timestamp.ToString("g")), details)
            || !_mainVm.CanAdminister) return;

        var actor = _mainVm.CreateAuditActor(AccessLevel);
        var config = await _configService.RestoreBackupAsync(backup.FilePath, restoredConfig =>
        {
            if (!_mainVm.CanAdminister) throw new UnauthorizedAccessException("The administrator session expired.");
            restoredConfig.Settings.AdminPasswordHash = Settings.AdminPasswordHash;
            restoredConfig.Settings.OperatorPasswordHash = Settings.OperatorPasswordHash;
        }, preview.Fingerprint);
        if (!_mainVm.CanAdminister) return;
        if (config != null)
        {
            try
            {
                await _mainVm.ApplyConfigAsync(config);
                _mainVm.MarkConfigAsPersisted(config);
                _mainVm.RecordAudit(
                    "BackupRestored",
                    actor,
                    "Success",
                    backup.FileName,
                    $"Backup timestamp: {backup.Timestamp:yyyy-MM-dd HH:mm:ss}");
                _notificationService.Show(LocalizationService.T("Msg_BackupRestored", backup.Timestamp.ToString("g")), NotificationType.Info);
                RefreshBackups();
            }
            catch (InvalidOperationException)
            {
                _mainVm.RecordAudit(
                    "BackupRestored",
                    actor,
                    "Failed",
                    backup.FileName,
                    "Backup restore could not be applied to the current session.");
                _notificationService.Show(LocalizationService.T("Msg_BackupRestoreFailed"), NotificationType.Error);
            }
        }
        else
        {
            _mainVm.RecordAudit(
                "BackupRestored",
                actor,
                "Failed",
                backup.FileName,
                "Backup restore failed before the configuration could be applied.");
            _notificationService.Show(LocalizationService.T("Msg_BackupRestoreFailed"), NotificationType.Error);
        }
    }

    [RelayCommand]
    private void DeleteBackup(ConfigService.ConfigBackupInfo? backup)
    {
        if (!CanEditApps || backup == null) return;
        if (!_mainVm.ConfirmOperation(LocalizationService.T("UI_DeleteBackupConfirm", backup.FileName), destructive: true)) return;

        try
        {
            File.Delete(backup.FilePath);
            RefreshBackups();
            _notificationService.Show(LocalizationService.T("Msg_BackupDeleted"), NotificationType.Info);
        }
        catch
        {
            _notificationService.Show(LocalizationService.T("Msg_BackupDeleteFailed"), NotificationType.Error);
        }
    }

    public List<CrashHistoryService.CrashEntry> CrashHistory => _crashHistoryService.GetHistory();
    public Dictionary<string, int> CrashCountsByApp => _crashHistoryService.GetCrashCountsByApp();

    private string BuildUniqueAppName(string baseName)
    {
        var candidate = baseName;
        var suffix = 2;

        while (Apps.Any(app => app.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase)))
        {
            candidate = $"{baseName} {suffix}";
            suffix++;
        }

        return candidate;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _operationsDisposed, 1) != 0) return;
        _mainVm.EndEditing();
        _operationLifetime.Cancel();
        _operationLifetime.Dispose();
        Settings.PropertyChanged -= OnSettingsPropertyChanged;
        SaveCommand.PropertyChanged -= OnSaveCommandChanged;
        _mainVm.PropertyChanged -= OnMainVmPropertyChanged;
        KioskStatus.PropertyChanged -= OnKioskStatusChanged;
        _auditLogService.EntryRecorded -= OnAuditEntryRecorded;
        Apps.CollectionChanged -= OnAppsCollectionChanged;
        Groups.CollectionChanged -= OnGroupsCollectionChanged;
        Layouts.CollectionChanged -= OnLayoutsCollectionChanged;
        foreach (var entry in _trackedGroupNames)
            entry.PropertyChanged -= OnGroupNameChanged;
        _trackedGroupNames.Clear();
        if (_observedOperatorScope != null)
            _observedOperatorScope.PropertyChanged -= OnOperatorScopePropertyChanged;
        foreach (var app in _trackedAppNames.Keys.ToList())
            app.PropertyChanged -= OnAppPropertyChanged;
    }

    public sealed class LocalizedOption<T>(T value, string label) : ObservableObject
    {
        public T Value { get; } = value;
        private string _label = label;
        public string Label { get => _label; set => SetProperty(ref _label, value); }
    }
}

public partial class OperatorScopeOption : ObservableObject
{
    public string Key { get; init; } = "";
    public string Label { get; init; } = "";

    [ObservableProperty] private bool _isSelected;
}
