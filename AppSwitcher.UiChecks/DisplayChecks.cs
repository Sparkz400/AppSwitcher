using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using AppSwitcher;
using AppSwitcher.Services;
using AppSwitcher.ViewModels;

internal static partial class Program
{
    private static async Task CheckDisplaySelections(Window window, SettingsViewModel settings, string language, string theme)
    {
        var native = MonitorHelper.GetDisplays();
        Check(native.Count > 0 && native.Any(display => display.IsPrimary), "Windows reports an active primary display");
        Check(native.All(display => display.WorkArea.Width > 0 && !string.IsNullOrWhiteSpace(display.Name)), "Native display discovery provides usable bounds and names");
        File.WriteAllText(Path.Combine(Output, "displays.json"), JsonSerializer.Serialize(new
        {
            count = native.Count, identitiesAvailable = native.Count(display => display.DeviceId.Length > 0),
            displays = native.Select(display => new { display.Index, display.Name, display.Bounds, display.WorkArea, display.DpiScale })
        }));
        var originalIndex = settings.Settings.PreferredMonitor;
        var originalId = settings.Settings.PreferredMonitorId;
        try
        {
            settings.OpenSearchResultCommand.Execute(new SettingsSearchResult("Settings_PreferredMonitor", "System", 0, "LauncherMonitorSelection", ""));
            await Render();
            settings.Settings.PreferredMonitor = 1;
            settings.Settings.PreferredMonitorId = "ui-check-disconnected-panel";
            settings.UpdateMonitorChoices(native);
            await Render();
            var choice = Descendants<ComboBox>(window).Single(combo => combo.IsVisible && combo.GetBindingExpression(ComboBox.SelectedValueProperty)?.ParentBinding.Path.Path == "LauncherMonitorSelection");
            choice.BringIntoView(); await Render();
            Check((int)choice.SelectedValue == -1, "A disconnected display remains a visible unavailable selection");
            Check(settings.Settings.PreferredMonitorId == "ui-check-disconnected-panel", "Rendering unavailable selection does not overwrite its identity");
            CheckControls(window);
            Save(window, $"display-unavailable-{language}-{theme}");
            var primary = native.First(display => display.IsPrimary);
            settings.UpdateMonitorChoices([primary with { Index = 1, DeviceId = "ui-check-disconnected-panel", Name = "Example workstation display with a deliberately long friendly name" }]);
            await Render();
            choice.BringIntoView(); await Render();
            Check((int)choice.SelectedValue == 1, "Reconnecting a saved display restores the selected option");
            Check(settings.Settings.PreferredMonitorId == "ui-check-disconnected-panel", "Reconnect changes no saved identity");
            CheckControls(window);
            Save(window, $"display-reconnected-{language}-{theme}");
        }
        finally
        {
            settings.Settings.PreferredMonitor = originalIndex;
            settings.Settings.PreferredMonitorId = originalId;
            settings.UpdateMonitorChoices(native);
        }
    }
}
