namespace AppSwitcher.ViewModels;

public partial class SettingsViewModel
{
    public ObservableCollection<LocalizedOption<ReadinessMode>> ReadinessChoices { get; } = [];
    [ObservableProperty] private string _browserDiagnostic = "";
    [ObservableProperty] private string _workspaceGroupKey = "";
    private readonly CancellationTokenSource _operationLifetime = new();
    private int _operationsDisposed;

    public bool CanRestartLauncher => CanEditApps && !HasInputErrors && !SaveCommand.IsRunning;
    [RelayCommand(CanExecute = nameof(CanRestartLauncher))]
    private async Task RestartLauncher()
    {
        if (!_mainVm.CanAdminister || HasInputErrors || Application.Current is not App app) return;
        if (HasUnsavedChanges) await SaveCommand.ExecuteAsync(null);
        if (HasUnsavedChanges || HasInputErrors) return;
        try { await app.RestartAsync(); }
        catch (Exception ex) { _notificationService.Show(LocalizationService.T("Msg_RestartFailed", ex.Message), NotificationType.Error); }
    }

    [RelayCommand(CanExecute = nameof(CanEditApps))]
    private async Task CheckBrowser()
    {
        if (!_mainVm.CanAdminister || SelectedApp is not { IsWebsite: true } app) return;
        var host = LocalizationService.T("Browser_" + BrowserSetupDiagnostics.HostStatus(app.Browser));
        BrowserDiagnostic = host;
        try
        {
            var code = await _mainVm.CheckBrowserAsync(app, _operationLifetime.Token);
            if (SelectedApp == app) BrowserDiagnostic = host + Environment.NewLine + LocalizationService.T("Browser_" + code);
            RefreshBrowserProfiles();
            _mainVm.RecordAudit("BrowserCheck", _mainVm.CreateAuditActor(), "Success", app.Name, code);
        }
        catch (OperationCanceledException) when (_operationLifetime.IsCancellationRequested) { }
        catch (Exception ex) { if (SelectedApp == app) BrowserDiagnostic = host + Environment.NewLine + ex.Message; }
    }

    [RelayCommand(CanExecute = nameof(CanEditApps))]
    private async Task TestBrowserSwitch()
    {
        if (!_mainVm.CanAdminister || SelectedApp is not { IsWebsite: true } app) return;
        try
        {
            await _mainVm.TestBrowserSwitchAsync(app, _operationLifetime.Token);
            if (SelectedApp == app) BrowserDiagnostic = LocalizationService.T("Browser_SwitchPassed");
            _mainVm.RecordAudit("BrowserSwitchTest", _mainVm.CreateAuditActor(), "Success", app.Name);
        }
        catch (OperationCanceledException) when (_operationLifetime.IsCancellationRequested) { }
        catch (Exception ex) { if (SelectedApp == app) BrowserDiagnostic = ex.Message; }
    }

    [RelayCommand(CanExecute = nameof(CanManageApps))]
    private Task StartWorkspace(ScreenLayout? layout) => layout == null
        ? Task.CompletedTask : _mainVm.StartWorkspaceAsync(layout, _operationLifetime.Token);

    [RelayCommand(CanExecute = nameof(CanViewDiagnostics))]
    private void PreviewSupportReport()
    {
        if (!_mainVm.CanAdminister) return;
        var dialog = new SupportReportWindow(_mainVm.CreateSupportReport(), () => _mainVm.CanAdminister);
        _mainVm.PrepareDialog(dialog, privileged: true);
        dialog.ShowDialog();
    }
}
