namespace AppSwitcher.ViewModels;

public sealed partial class SetupStep(string key, string section, int tab = 0) : ObservableObject
{
    public string Key { get; } = key;
    public string Section { get; } = section;
    public int Tab { get; } = tab;
    public string Title => LocalizationService.T("Setup_" + Key);
    public string Hint => LocalizationService.T("Setup_" + Key + "Hint");
    [ObservableProperty] private string _status = "";

    internal void Refresh(string statusKey)
    {
        Status = LocalizationService.T(statusKey);
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Hint));
    }
}

public partial class SettingsViewModel
{
    public IReadOnlyList<SetupStep> SetupSteps { get; } =
    [new("Apps", "Applications"), new("Access", "Access"), new("Browsers", "Browsers"),
        new("Workspace", "Layouts"), new("Backups", "System", 3), new("Test", "Monitoring")];

    private void RefreshSetupSteps()
    {
        var websites = Apps.Where(app => app.IsWebsite).ToArray();
        SetupSteps[0].Refresh(Apps.Count > 0 ? "Setup_Configured" : "Setup_Review");
        SetupSteps[1].Refresh(!string.IsNullOrEmpty(Settings.AdminPasswordHash) && Settings.SessionTimeoutMinutes > 0
            ? "Setup_Configured" : "Setup_Review");
        SetupSteps[2].Refresh(websites.Length == 0 ? "Setup_Optional" : websites.All(app => !string.IsNullOrWhiteSpace(app.BrowserProfileId))
            ? "Setup_Configured" : "Setup_Review");
        SetupSteps[3].Refresh(Layouts.Count > 0 ? "Setup_Configured" : "Setup_Optional");
        SetupSteps[4].Refresh(ConfigBackups.Count > 0 ? "Setup_Available" : "Setup_Review");
        SetupSteps[5].Refresh("Setup_TestRequired");
    }

    [RelayCommand]
    private void OpenSetupStep(SetupStep? step)
    {
        if (!IsAdmin || !_mainVm.CanAdminister || step == null || !SetupSteps.Contains(step)) return;
        OpenSearchResult(new("Setup_" + step.Key, step.Section, step.Tab, "", "", AdminOnly: true));
    }
}
