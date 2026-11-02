using System.ComponentModel;

namespace AppSwitcher.ViewModels;

public partial class SettingsViewModel
{
    private bool _browserSetupOpen;
    internal Func<IBrowserSetupService> BrowserSetupServiceFactory { get; set; } = () => new BrowserSetupService();
    [RelayCommand(CanExecute = nameof(CanEditApps))]
    private void SetupSelectedBrowser()
    {
        if (SelectedApp is { IsWebsite: true } app) ShowBrowserSetup(app.Browser, app);
    }
    private void ShowBrowserSetup(BrowserKind browser, AppItem? app)
    {
        if (!CanEditApps || !_mainVm.CanAdminister || _browserSetupOpen || _operationLifetime.IsCancellationRequested) return;
        var owner = Application.Current?.Windows.OfType<SettingsWindow>().FirstOrDefault(window => ReferenceEquals(window.DataContext, this));
        if (owner == null) return;
        _browserSetupOpen = true;
        var savedId = app?.BrowserProfileId ?? "";
        var actor = _mainVm.CreateAuditActor();
        using var model = new BrowserSetupViewModel(browser, savedId, BrowserSetupServiceFactory(), () => _mainVm.BrowserProfiles,
            () => CanEditApps && _mainVm.CanAdminister && !_operationLifetime.IsCancellationRequested);
        var dialog = new BrowserSetupWindow(model);
        _mainVm.PrepareDialog(dialog, owner, privileged: true);
        PropertyChangedEventHandler accessChanged = (_, change) =>
        {
            if (change.PropertyName is nameof(MainViewModel.CanAdminister) or nameof(MainViewModel.CurrentAccessLevel)
                && !_mainVm.CanAdminister && dialog.IsVisible) dialog.Close();
        };
        CancelEventHandler ownerClosing = (_, close) => { if (!close.Cancel && dialog.IsVisible) dialog.Close(); };
        _mainVm.PropertyChanged += accessChanged; owner.Closing += ownerClosing;
        try
        {
            dialog.ShowDialog();
            if (model.HostReady) _mainVm.RecordAudit("BrowserHostSetup", actor, "Success", browser.ToString());
            if (!_mainVm.CanAdminister || _operationLifetime.IsCancellationRequested) return;
            if (dialog.Result is { } result && app != null && Apps.Contains(app) && app.IsWebsite && app.Browser == browser
                && app.BrowserProfileId == savedId && (savedId.Length == 0 || savedId == result.ProfileId)
                && _mainVm.BrowserProfiles.Count(p => p.Browser == browser && p.ProfileId == result.ProfileId) == 1)
            {
                app.BrowserProfileId = result.ProfileId;
                _mainVm.RecordAudit("BrowserSetup", _mainVm.CreateAuditActor(), "Success", app.Name, browser.ToString());
            }
            RefreshBrowserProfiles();
            if (ActiveSection == "Browsers") _ = RefreshBrowserConnectionsCommand.ExecuteAsync(null);
        }
        finally { _mainVm.PropertyChanged -= accessChanged; owner.Closing -= ownerClosing; _browserSetupOpen = false; }
    }
}
