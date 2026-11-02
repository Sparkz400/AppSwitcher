namespace AppSwitcher.ViewModels;

internal sealed partial class BrowserSetupViewModel : ObservableObject, IDisposable
{
    private readonly IBrowserSetupService _service;
    private readonly Func<IReadOnlyList<BrowserProfileChoice>> _profiles;
    private readonly Func<bool> _authorized;
    private readonly string _savedProfileId;
    private string _preferredProfileId = "";
    private readonly CancellationTokenSource _lifetime = new();
    private bool _disposed;
    private BrowserSetupPackage? _package;
    public BrowserKind Browser { get; }
    public string Title => LocalizationService.T("Setup_Title", Browser);
    public string RestoreHint => LocalizationService.T("Setup_RestoreHint");
    public string RestoreChoice => LocalizationService.T("Setup_RestoreChoice" + Browser);
    public event Action<BrowserProfileChoice>? Completed;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _hostReady;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private string _error = "";
    [ObservableProperty] private string _pathStatus = "";
    [ObservableProperty] private BrowserProfileChoice? _selectedProfile;
    public ObservableCollection<BrowserProfileChoice> Profiles { get; } = [];
    public bool NeedsPrepare => !HostReady;
    public bool NeedsConnection => !ConnectionReady && Profiles.Count == 0;
    public bool PackageBlocked => HostReady && NeedsConnection && _package?.InstallKind == BrowserExtensionInstall.Unavailable;
    public bool ShowInstallSteps => NeedsConnection && !PackageBlocked;
    public bool ShowInstallActions => ShowInstallSteps && CanInstall;
    public bool ShowHostReady => HostReady && !PackageBlocked;
    public bool ShowFinish => !PackageBlocked;
    public bool ShowProfiles => Profiles.Count > 1 || (_savedProfileId.Length == 0 && _preferredProfileId.Length > 0 && Profiles.Count > 0 && SelectedProfile == null);
    public bool ShowSingleProfile => ConnectionReady && !ShowProfiles;
    public bool IsUnpacked => _package?.InstallKind == BrowserExtensionInstall.Unpacked && NeedsConnection;
    public bool ShowFolder => NeedsConnection && _package?.InstallKind is BrowserExtensionInstall.Unpacked or BrowserExtensionInstall.FirefoxFile;
    public string ExtensionDirectory => _package?.InstallKind == BrowserExtensionInstall.FirefoxFile ? _package.InstallTarget : _package?.ExtensionDirectory ?? "";
    public bool CanInstall => Authorized && HostReady && _package?.InstallKind != BrowserExtensionInstall.Unavailable;
    public bool ConnectionReady => HostReady && SelectedProfile != null && Profiles.Contains(SelectedProfile);
    public bool CanFinish => Authorized && ConnectionReady;
    public bool Authorized => !_disposed && _authorized();
    public string InstallHint => LocalizationService.T(_package?.InstallKind switch
    {
        BrowserExtensionInstall.Store => "Setup_StoreHint",
        BrowserExtensionInstall.FirefoxFile => "Setup_FileHint",
        BrowserExtensionInstall.Unpacked => "Setup_UnpackedHint",
        _ => "Setup_FirefoxUnavailable"
    });
    public string InstallAction => LocalizationService.T(ShowFolder ? "Setup_CopyAndOpen" : "Setup_Install", Browser);
    public IReadOnlyList<string> InstallSteps => (_package?.InstallKind switch
    {
        BrowserExtensionInstall.Unpacked => new[] { "Setup_UnpackedOne", "Setup_UnpackedTwo", "Setup_UnpackedThree" },
        BrowserExtensionInstall.FirefoxFile => new[] { "Setup_FirefoxOne", "Setup_FirefoxTwo", "Setup_FirefoxThree" },
        _ => new[] { "Setup_StoreHint" }
    }).Select((key, index) => $"{index + 1}.  {LocalizationService.T(key)}").ToArray();
    public BrowserSetupViewModel(BrowserKind browser, string savedProfileId, IBrowserSetupService service,
        Func<IReadOnlyList<BrowserProfileChoice>> profiles, Func<bool> authorized)
    { Browser = browser; _savedProfileId = savedProfileId; _service = service; _profiles = profiles; _authorized = authorized; }

    [RelayCommand]
    internal async Task PrepareAsync()
    {
        if (!Authorized || IsBusy) return;
        IsBusy = true; Error = ""; Status = LocalizationService.T("Setup_Preparing");
        try
        {
            _package = await _service.PrepareAsync(Browser, _lifetime.Token);
            if (!Authorized) return;
            HostReady = true;
            NotifyState();
            RefreshConnection();
            if (NeedsConnection && CanInstall) OpenInstall();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex) { if (Authorized) { Error = LocalizationService.T(ex.Message); Status = LocalizationService.T("Setup_Failed"); } }
        finally { IsBusy = false; NotifyState(); }
    }

    // Called by the dialog only. No startup service or background polling after it closes.
    internal async Task WatchAsync()
    {
        try
        {
            for (var tick = 0; tick < 600 && Authorized; tick++)
            {
                await Task.Delay(1000, _lifetime.Token);
                RefreshConnection();
            }
            if (Authorized && !ConnectionReady && !PackageBlocked) Status = LocalizationService.T("Setup_WaitExpired");
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
    }

    [RelayCommand]
    internal void RefreshConnection()
    {
        if (!Authorized || !HostReady) return;
        var connections = _profiles().Where(p => p.Browser == Browser).ToArray();
        // Duplicate identities (e.g. a copied browser profile) are never eligible.
        var eligible = connections.GroupBy(p => p.ProfileId).Where(g => g.Count() == 1 && Guid.TryParse(g.Key, out _))
            .Select(g => g.Single()).Where(p => _savedProfileId.Length == 0 || p.ProfileId == _savedProfileId).ToArray();
        var selectedId = _savedProfileId.Length > 0 ? _savedProfileId : _preferredProfileId;
        if (!Profiles.SequenceEqual(eligible))
        {
            Profiles.Clear(); foreach (var profile in eligible) Profiles.Add(profile);
        }
        SelectedProfile = selectedId.Length > 0 ? eligible.FirstOrDefault(p => p.ProfileId == selectedId) : eligible.Length == 1 ? eligible[0] : null;
        Status = LocalizationService.T(ConnectionReady ? "Setup_Connected"
            : PackageBlocked ? "Setup_FirefoxBlocked"
            : connections.Any(p => (_savedProfileId.Length == 0 || p.ProfileId == _savedProfileId) && connections.Count(c => c.ProfileId == p.ProfileId) > 1) ? "Setup_ProfileConflict"
            : eligible.Length > 0 ? "Setup_ChooseProfile" : _savedProfileId.Length > 0 ? "Setup_OpenSavedProfile" : "Setup_Waiting");
        NotifyState();
    }
    partial void OnSelectedProfileChanged(BrowserProfileChoice? value)
    {
        if (value != null) _preferredProfileId = value.ProfileId;
        if (HostReady && value != null) Status = LocalizationService.T("Setup_Connected");
        NotifyState();
    }
    private void NotifyState()
    {
        foreach (var property in new[] { nameof(NeedsPrepare), nameof(NeedsConnection), nameof(PackageBlocked), nameof(ShowInstallSteps), nameof(ShowInstallActions), nameof(ShowHostReady), nameof(ShowFinish), nameof(InstallAction), nameof(ShowFolder), nameof(ShowProfiles), nameof(ShowSingleProfile), nameof(IsUnpacked), nameof(ExtensionDirectory), nameof(CanInstall), nameof(ConnectionReady), nameof(CanFinish), nameof(InstallHint) }) OnPropertyChanged(property);
        OnPropertyChanged(nameof(InstallSteps));
        OpenInstallCommand.NotifyCanExecuteChanged(); FinishCommand.NotifyCanExecuteChanged(); OpenStartupCommand.NotifyCanExecuteChanged();
    }
    [RelayCommand(CanExecute = nameof(CanInstall))]
    private void OpenInstall()
    {
        if (!CanInstall) return;
        if (ShowFolder) CopyFolder();
        TryOpen(_package?.InstallKind == BrowserExtensionInstall.FirefoxFile ? BrowserSetupService.ExtensionsPage(Browser) : _package?.InstallTarget);
    }
    [RelayCommand(CanExecute = nameof(CanFinish))]
    private void OpenStartup() { if (CanFinish) TryOpen(BrowserSetupService.StartupPage(Browser)); }
    private void TryOpen(string? target)
    {
        if (!Authorized || target == null) return;
        try { _service.Open(Browser, target); Error = ""; }
        catch (Exception ex) { Error = LocalizationService.T(ex.Message); }
    }
    [RelayCommand]
    private void CopyFolder()
    {
        if (!Authorized || !ShowFolder) return;
        try { _service.CopyPath(ExtensionDirectory); PathStatus = LocalizationService.T("Setup_PathCopied"); }
        catch (Exception) { PathStatus = LocalizationService.T("Setup_CopyFailed"); }
    }
    [RelayCommand(CanExecute = nameof(CanFinish))]
    private void Finish()
    {
        // Validate again at acceptance; a disconnect or a second connection can arrive between ticks.
        var chosen = SelectedProfile?.ProfileId;
        RefreshConnection();
        if (CanFinish && SelectedProfile?.ProfileId == chosen) Completed?.Invoke(SelectedProfile!);
    }
    public void Dispose() { if (_disposed) return; _disposed = true; _lifetime.Cancel(); _lifetime.Dispose(); NotifyState(); }
}
