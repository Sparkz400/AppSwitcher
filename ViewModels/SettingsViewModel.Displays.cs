namespace AppSwitcher.ViewModels;

public partial class SettingsViewModel
{
    private IReadOnlyList<DisplayInfo> _displayChoices = [];
    private bool _syncingMonitorSelection;

    public int LauncherMonitorSelection
    {
        get => DisplaySelection(Settings.PreferredMonitor, Settings.PreferredMonitorId);
        set
        {
            if (!CanEditApps || !_mainVm.CanAdminister || _syncingMonitorSelection || value < 0 || (value > 0 && !_displayChoices.Any(display => display.Index == value))) return;
            _syncingMonitorSelection = true;
            try
            {
                Settings.PreferredMonitor = value;
                Settings.PreferredMonitorId = _displayChoices.FirstOrDefault(display => display.Index == value)?.DeviceId ?? "";
            }
            finally { _syncingMonitorSelection = false; }
            NotifyMonitorSelections();
        }
    }

    public int AppMonitorSelection
    {
        get => SelectedApp is { } app ? DisplaySelection(app.ForcedMonitor, app.ForcedMonitorId) : 0;
        set
        {
            if (!CanEditApps || !_mainVm.CanAdminister || _syncingMonitorSelection || SelectedApp is not { } app || value < 0 || (value > 0 && !_displayChoices.Any(display => display.Index == value))) return;
            _syncingMonitorSelection = true;
            try
            {
                app.ForcedMonitor = value;
                app.ForcedMonitorId = _displayChoices.FirstOrDefault(display => display.Index == value)?.DeviceId ?? "";
            }
            finally { _syncingMonitorSelection = false; }
            NotifyMonitorSelections();
        }
    }

    private int DisplaySelection(int index, string identity)
    {
        if (index == 0 && string.IsNullOrEmpty(identity)) return 0;
        var found = MonitorHelper.FindConfiguredDisplay(index, identity, _displayChoices);
        return found > 0 ? found : -1;
    }

    private void RefreshMonitorChoices() => UpdateMonitorChoices(MonitorHelper.GetDisplays());

    internal void UpdateMonitorChoices(IReadOnlyList<DisplayInfo> displays)
    {
        _syncingMonitorSelection = true;
        try
        {
            _displayChoices = displays;
            var options = displays.Select(display => new LocalizedOption<int>(display.Index,
                $"{display.Index}: {display.Name}{(display.IsPrimary ? " — " + LocalizationService.T("Display_Primary") : "")} ({(int)display.Bounds.Width}×{(int)display.Bounds.Height})")).ToArray();
            UpdateChoices(MonitorChoices, [new(0, LocalizationService.T("Settings_MonitorAuto")), .. options,
                .. LauncherMonitorSelection == -1 ? new[] { new LocalizedOption<int>(-1, LocalizationService.T("Display_Unavailable")) } : []]);
            UpdateChoices(WindowPolicyMonitorChoices, [new(0, LocalizationService.T("Settings_WindowPolicyMonitorNone")),
                .. options.Select(option => new LocalizedOption<int>(option.Value, option.Label)),
                .. AppMonitorSelection == -1 ? new[] { new LocalizedOption<int>(-1, LocalizationService.T("Display_Unavailable")) } : []]);
        }
        finally { _syncingMonitorSelection = false; }
        NotifyMonitorSelections();
    }

    private void NotifyMonitorSelections()
    {
        if (_syncingMonitorSelection) return;
        OnPropertyChanged(nameof(LauncherMonitorSelection));
        OnPropertyChanged(nameof(AppMonitorSelection));
    }
}
