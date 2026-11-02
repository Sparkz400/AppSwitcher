using System.ComponentModel;

namespace AppSwitcher.ViewModels;

public partial class SettingsViewModel
{
    private bool _windowPickerOpen;
    private bool CanAddFromOpenWindow => CanEditApps && _mainVm.CanAdminister && !_windowPickerOpen;

    [RelayCommand(CanExecute = nameof(CanAddFromOpenWindow))]
    private void AddFromOpenWindow()
    {
        if (!CanAddFromOpenWindow) return;
        var owner = Application.Current?.Windows.OfType<SettingsWindow>().FirstOrDefault(window => ReferenceEquals(window.DataContext, this));
        if (owner == null) return;
        _windowPickerOpen = true;
        AddFromOpenWindowCommand.NotifyCanExecuteChanged();
        using var model = new OpenWindowPickerViewModel(new OpenWindowCatalog(),
            () => CanEditApps && _mainVm.CanAdminister && !_operationLifetime.IsCancellationRequested, () => Apps.ToArray());
        var picker = new OpenWindowPicker(model);
        _mainVm.PrepareDialog(picker, owner, privileged: true);
        PropertyChangedEventHandler accessChanged = (_, change) =>
        {
            if (change.PropertyName is nameof(MainViewModel.CanAdminister) or nameof(MainViewModel.CurrentAccessLevel))
            {
                model.RefreshAuthorization();
                if (!_mainVm.CanAdminister && picker.IsVisible) picker.Close();
            }
        };
        // End the modal dispatcher frame before WPF tears down its owner.
        CancelEventHandler ownerClosing = (_, close) => { if (!close.Cancel && picker.IsVisible) picker.Close(); };
        _mainVm.PropertyChanged += accessChanged;
        owner.Closing += ownerClosing;
        try
        {
            picker.ShowDialog();
            if (picker.Result is { } result) ApplyWindowPick(result);
        }
        finally
        {
            _mainVm.PropertyChanged -= accessChanged;
            owner.Closing -= ownerClosing;
            _windowPickerOpen = false;
            AddFromOpenWindowCommand.NotifyCanExecuteChanged();
        }
    }

    internal bool ApplyWindowPick(OpenWindowPick result)
    {
        if (!CanEditApps || !_mainVm.CanAdminister || _operationLifetime.IsCancellationRequested) return false;
        if (result.Action == WindowPickAction.Manual) { AddApp(); return true; }
        if (result.Candidate is not { } candidate || !OpenWindowCatalog.SameIdentity(candidate, candidate)) return false;
        var classification = OpenWindowCatalog.Classify(candidate.ExecutablePath);
        if (classification.Kind != candidate.Kind || classification.Browser != candidate.Browser) return false;
        var existing = OpenWindowPickerViewModel.FindExisting(candidate, Apps.ToArray());
        if (existing.Count > 1) return false;
        if (existing.Count == 1)
        {
            if (result.Action == WindowPickAction.Existing && existing[0].AppId != result.ExistingAppId) return false;
            SelectedApp = existing[0];
            ActiveSection = "Applications";
            return true;
        }
        if (result.Action == WindowPickAction.Existing) return false;

        AppItem app;
        if (result.Action == WindowPickAction.Website && candidate.Kind == OpenWindowKind.Website && candidate.Browser is { } browser)
        {
            app = new() { Name = BuildUniqueAppName(LocalizationService.T("Msg_NewApp")), TargetKind = AppTargetKind.Website,
                Browser = browser, RequireConfirmation = true };
        }
        else if (result.Action == WindowPickAction.Native && candidate.Kind == OpenWindowKind.Native && !string.IsNullOrWhiteSpace(result.Name))
        {
            app = new() { Name = BuildUniqueAppName(result.Name.Trim()), Path = candidate.ExecutablePath, RequireConfirmation = true };
            // Titles identify a choice in the picker, not a durable matching rule.
            // Keep the normal executable-scoped behavior and working-folder fallback.
            // Do not import command lines, document paths, recovery or autostart settings.
        }
        else return false;
        Apps.Add(app);
        SelectedApp = app;
        ActiveSection = "Applications";
        if (app.IsWebsite) NavigateToSetting?.Invoke(new("Browser_Url", "Applications", 0, "SelectedApp.WebsiteUrl", ""));
        return true;
    }
}
