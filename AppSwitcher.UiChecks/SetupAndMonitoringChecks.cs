using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation.Peers;
using AppSwitcher.Models;
using AppSwitcher.Services;
using AppSwitcher.ViewModels;

internal static partial class Program
{
    private static async Task CheckSetupNavigation(Window window, SettingsViewModel settings)
    {
        foreach (var step in settings.SetupSteps)
        {
            settings.ActiveSection = "Setup";
            await Render();
            var button = Descendants<Button>(window).Single(button => button.CommandParameter == step);
            button.BringIntoView();
            await Render();
            CheckControls(window);
            Check(new ButtonAutomationPeer(button).GetName() == step.Title, "Setup action exposes its destination to assistive technology");
            Invoke(button);
            await Render();
            Check(settings.ActiveSection == step.Section, "Setup action opens the corresponding settings page");
        }
        settings.ActiveSection = "Setup";
        await Render();
    }

    private static async Task CheckAttentionPresentation(Window window, MainViewModel runtime, string language, string theme)
    {
        runtime.ShowNeedsAttentionOnly = true;
        await Render();
        Check(runtime.MonitoringApps.All(app => app.NeedsAttention), "Monitoring only lists entries needing attention");
        Check(Descendants<TextBlock>(window).Any(text => text.IsVisible && text.Text == runtime.MaintenanceStatus), "Maintenance remains visible with the attention filter");
        Save(window, $"monitoring-attention-{language}-{theme}");
        var changed = runtime.Apps.Where(app => app.NeedsAttention).Select(app => (app, app.State, app.IsResponding, app.RequiresWatchdogAcknowledgement)).ToArray();
        foreach (var (app, _, _, _) in changed) { app.State = AppState.Stopped; app.IsResponding = true; app.RequiresWatchdogAcknowledgement = false; }
        await Render();
        Check(Descendants<TextBlock>(window).Any(text => text.IsVisible && text.Text == LocalizationService.T("Monitoring_NoAttention")), "An empty attention filter explains what it excludes");
        Save(window, $"monitoring-no-faults-{language}-{theme}");
        foreach (var (app, state, responding, acknowledgement) in changed) { app.State = state; app.IsResponding = responding; app.RequiresWatchdogAcknowledgement = acknowledgement; }
        runtime.ShowNeedsAttentionOnly = false;
        await Render();
    }
}
