namespace AppSwitcher.ViewModels;

public sealed partial class BrowserConnectionRow : ObservableObject
{
    public BrowserKind Browser { get; init; }
    public string ProfileName { get; init; } = "";
    public string HostStatus { get; init; } = "";
    public string ExtensionVersion { get; init; } = "";
    public int WebsiteCount { get; init; }
    public string? AppId { get; set; }
    public bool HostAvailable { get; init; }
    public string Heading => $"{Browser} — {ProfileName}";
    public string Detail => LocalizationService.T("Browser_OverviewDetail", WebsiteCount, ExtensionVersion);
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Summary))] private string _status = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Summary), nameof(ActionLabel), nameof(ActionHint))] private string _stateCode = "";
    public string ActionHint => LocalizationService.T(RecoveryKey(StateCode, HostAvailable));
    internal static string RecoveryKey(string code, bool hostAvailable) => !hostAvailable ? "Browser_RepairHost" : code switch
    {
        "Ready" or "Connected" => "Browser_RepairReady",
        "ProfileConflict" => "Browser_RepairProfileConflict",
        "IdentityConflict" or "Recovering" => "Browser_RepairIdentity",
        "InvalidIdentity" => "Browser_RepairInvalidIdentity",
        "JournalUnavailable" => "Browser_RepairJournal",
        "InvalidUrl" => "Browser_RepairUrl",
        "Absent" => "Browser_RepairAbsent",
        "ProfileChanged" => "Browser_RepairProfileChanged",
        "NotConfigured" or "ProfileRequired" => "Browser_RepairPairing",
        "ConnectionUnavailable" or "CheckTimeout" or "CheckFailed" => "Browser_RepairConnection",
        _ => "Browser_RepairWebsite"
    };
    public string Summary => StateCode switch
    {
        "Ready" => LocalizationService.T("Browser_SummaryReady"),
        "Connected" => LocalizationService.T("Browser_SummaryConnected"),
        "NotConfigured" => LocalizationService.T("Browser_SummaryNotConfigured"),
        "ConnectionUnavailable" => LocalizationService.T(!HostAvailable ? "Browser_SummaryHost" : "Browser_SummaryDisconnected"),
        _ => Status // A concrete fault and its recovery instruction stay visible.
    };
    public string ActionLabel => LocalizationService.T(AppId == null || !HostAvailable ? "Browser_ActionSetup"
        : StateCode is "ConnectionUnavailable" or "ProfileRequired" ? "Browser_ActionProfile" : "Browser_ActionReview");
}

public partial class SettingsViewModel
{
    public ObservableCollection<BrowserConnectionRow> BrowserConnections { get; } = [];
    [ObservableProperty] private bool _browserSetupExpanded;
    [ObservableProperty] private string _browserOverviewStatus = "";

    partial void OnActiveSectionChanged(string value)
    {
        if (value is "Applications" or "System") RefreshMonitorChoices();
        if (value == "Layouts") RefreshWorkspaceCards();
        if (value == "Setup" && IsAdmin) { RefreshBackups(); RefreshSetupSteps(); }
        if (value == "Browsers" && IsAdmin && !RefreshBrowserConnectionsCommand.IsRunning)
            _ = RefreshBrowserConnectionsCommand.ExecuteAsync(null);
    }

    [RelayCommand(CanExecute = nameof(CanViewDiagnostics))]
    private async Task RefreshBrowserConnections()
    {
        if (!_mainVm.CanAdminister) return;
        BrowserOverviewStatus = LocalizationService.T("Browser_Checking");
        BrowserConnections.Clear();
        var profiles = _mainVm.BrowserProfiles;
        var websites = Apps.Where(app => app.IsWebsite).ToArray();
        try
        {
            var hostStates = await Task.Run(() => Enum.GetValues<BrowserKind>().ToDictionary(browser => browser, BrowserSetupDiagnostics.HostStatus), _operationLifetime.Token);
            foreach (var browser in Enum.GetValues<BrowserKind>())
            {
                var configured = websites.Where(app => app.Browser == browser).ToArray();
                var browserProfiles = profiles.Where(profile => profile.Browser == browser).ToArray();
                var ids = configured.Select(app => app.BrowserProfileId).Concat(browserProfiles.Select(profile => profile.ProfileId)).Distinct().ToArray();
                if (ids.Length == 0) ids = [""];
                foreach (var id in ids)
                {
                    var connections = browserProfiles.Where(profile => profile.ProfileId == id).ToArray();
                    var apps = configured.Where(app => app.BrowserProfileId == id).ToArray();
                    var code = apps.Length == 0 && connections.Length == 0 ? "NotConfigured"
                        : id.Length == 0 ? "ProfileRequired"
                        : connections.Length > 1 ? "ProfileConflict"
                        : connections.Length == 0 ? "ConnectionUnavailable" : "Connected";
                    BrowserConnections.Add(new BrowserConnectionRow
                    {
                        Browser = browser,
                        ProfileName = connections.FirstOrDefault()?.Name ?? (id.Length == 0 ? LocalizationService.T("Browser_NoProfile") : id[..Math.Min(id.Length, 8)]),
                        HostStatus = LocalizationService.T("Browser_" + hostStates[browser]),
                        ExtensionVersion = connections.FirstOrDefault()?.ExtensionVersion ?? "—",
                        WebsiteCount = apps.Length, AppId = apps.FirstOrDefault()?.AppId,
                        HostAvailable = hostStates[browser] == "HostInstalled", StateCode = code,
                        Status = LocalizationService.T("Browser_" + code)
                    });
                }
            }

            // A refresh is explicit/on-open only; no new timer or background poller.
            using var slots = new SemaphoreSlim(4);
            await Task.WhenAll(BrowserConnections.Where(row => row.WebsiteCount > 0).Select(async row =>
            {
                await slots.WaitAsync(_operationLifetime.Token);
                try
                {
                    var first = websites.First(app => app.AppId == row.AppId);
                    var apps = websites.Where(app => app.Browser == row.Browser && app.BrowserProfileId == first.BrowserProfileId);
                    foreach (var website in apps)
                    {
                        if (!_mainVm.CanAdminister) return;
                        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_operationLifetime.Token);
                        deadline.CancelAfter(TimeSpan.FromSeconds(5));
                        try
                        {
                            var code = await _mainVm.CheckBrowserAsync(website, deadline.Token);
                            if (code is not ("Ready" or "Connected"))
                            {
                                row.AppId = website.AppId;
                                row.Status = website.Name + ": " + LocalizationService.T("Browser_" + code);
                                row.StateCode = code;
                                return;
                            }
                        }
                        catch (OperationCanceledException) when (!_operationLifetime.IsCancellationRequested)
                        { row.Status = LocalizationService.T("Browser_CheckTimeout"); row.StateCode = "CheckTimeout"; return; }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        { row.AppId = website.AppId; row.Status = website.Name + ": " + ex.Message; row.StateCode = "CheckFailed"; return; }
                    }
                    row.Status = LocalizationService.T("Browser_Ready");
                    row.StateCode = "Ready";
                }
                finally { slots.Release(); }
            }));
            BrowserOverviewStatus = LocalizationService.T("Browser_CheckedAt", DateTime.Now.ToShortTimeString());
        }
        catch (OperationCanceledException) when (_operationLifetime.IsCancellationRequested) { }
        catch (Exception ex) { BrowserOverviewStatus = ex.Message; }
    }

    [RelayCommand(CanExecute = nameof(CanViewDiagnostics))]
    private void ConfigureBrowserConnection(BrowserConnectionRow? row)
    {
        if (!_mainVm.CanAdminister || row == null) return;
        var configuredApp = Apps.FirstOrDefault(app => app.AppId == row.AppId);
        if (row.AppId == null || !row.HostAvailable || row.StateCode is "ConnectionUnavailable" or "ProfileRequired")
        {
            ShowBrowserSetup(row.Browser, configuredApp);
            return;
        }
        if (Apps.FirstOrDefault(app => app.AppId == row.AppId) is { } app)
        {
            SelectedApp = app;
            ActiveSection = "Applications";
            BrowserSetupExpanded = true;
            NavigateToSetting?.Invoke(new("Browser_Profile", "Applications", 0, "SelectedBrowserProfile", ""));
        }
        else
        {
            BrowserSetupExpanded = true;
            NavigateToSetting?.Invoke(new("Browser_SetupInstructions", "Browsers", 0, "", ""));
        }
    }
}
