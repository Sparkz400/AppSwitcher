using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using AppSwitcher;
using AppSwitcher.Controls;
using AppSwitcher.Models;
using AppSwitcher.Services;
using AppSwitcher.Tests;
using AppSwitcher.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;

internal static partial class Program
{
    private static async Task CheckHighContrast()
    {
        using var fixture = await MainViewModelAuditTests.CreateHarnessAsync(new ConfigRoot
        {
            Settings = new() { Language = "en", Theme = "Dark", BgColor = "#777777", FgColor = "#777777", ButtonOpacity = 0.2, GlowEnabled = true },
            Apps = [new() { Name = "Accessible example", Path = "ui-accessibility-only.exe" }]
        });
        var runtime = fixture.ViewModel;
        runtime.CurrentAccessLevel = AccessLevel.Admin;
        using var session = new SessionTimeoutService(NullLogger<SessionTimeoutService>.Instance);
        var theme = new ThemeService(NullLogger<ThemeService>.Instance);
        using var settings = new SettingsViewModel(runtime, fixture.ConfigService, new SecurityService(), fixture.NotificationService,
            new ScreenLayoutService(NullLogger<ScreenLayoutService>.Instance, fixture.ProcessHelper),
            new CrashHistoryService(NullLogger<CrashHistoryService>.Instance), session);
        var launcher = new MainWindow(session) { DataContext = runtime };
        var editor = new SettingsWindow { DataContext = settings, Width = 720, Height = 640 };
        launcher.Show(); editor.Show();
        var resources = Application.Current.Resources;
        var keys = new[] { SystemColors.WindowColorKey, SystemColors.WindowTextColorKey, SystemColors.HighlightColorKey, SystemColors.HighlightTextColorKey };
        try
        {
            foreach (var (language, background, foreground) in new[] { ("en", Colors.Black, Colors.Yellow), ("de", Colors.White, Colors.Black) })
            {
                runtime.Settings.Language = language;
                resources[SystemColors.WindowColorKey] = background;
                resources[SystemColors.WindowTextColorKey] = foreground;
                resources[SystemColors.HighlightColorKey] = foreground;
                resources[SystemColors.HighlightTextColorKey] = background;
                theme.ApplyTheme(runtime.Settings.Theme, highContrast: true);
                await Render();
                CheckContrast();
                var dictionary = resources.MergedDictionaries.Single(dict => dict.Source?.OriginalString == "Themes/HighContrastTheme.xaml");
                theme.ApplyTheme("Light", highContrast: true);
                Check(resources.MergedDictionaries.Contains(dictionary), "Theme choices retain Windows high contrast while it is active");
                var visual = Descendants<LauncherVisual>(launcher).Single();
                var surface = (Path)visual.FindName("LauncherSurface");
                Check(((SolidColorBrush)surface.Fill).Color == background && ((SolidColorBrush)surface.Stroke).Color == foreground,
                    "High contrast replaces custom launcher colors with system colors");
                Check(((FrameworkElement)visual.FindName("GlowPath")).Visibility == Visibility.Collapsed, "High contrast suppresses decorative glow");
                Check(((Button)launcher.FindName("MainGrid")).Opacity == 1, "High contrast overrides launcher transparency");
                foreach (var icon in new[] { "Hamburger", "Dots", "Arrow", "Ring", "Power" })
                {
                    runtime.Settings.IconStyle = icon; await Render();
                    var strokes = Descendants<Shape>(visual).Where(shape => shape.IsVisible && shape != surface).ToArray();
                    Check(strokes.Any(shape => (shape.Fill as SolidColorBrush)?.Color == foreground || (shape.Stroke as SolidColorBrush)?.Color == foreground), "Every launcher icon has a high-contrast foreground");
                }
                Save(launcher, $"high-contrast-launcher-{language}");
                foreach (var section in new[] { "Setup", "Applications", "Monitoring", "Access", "System" })
                {
                    settings.ActiveSection = section; await Render(); CheckControls(editor);
                    Save(editor, $"high-contrast-{language}-{section.ToLowerInvariant()}");
                }
                var pin = new PasswordDialog(); pin.Show(); await Render(); CheckControls(pin); Save(pin, $"high-contrast-pin-{language}"); pin.Close();
                theme.ApplyTheme(runtime.Settings.Theme, highContrast: false);
                await Render();
                Check(((Button)launcher.FindName("MainGrid")).Opacity == 0.2, "Leaving high contrast restores configured transparency");
                Check(((SolidColorBrush)surface.Fill).Color == Color.FromRgb(119, 119, 119), "Leaving high contrast restores configured launcher colors");
                Check(runtime.Settings.BgColor == "#777777" && runtime.Settings.FgColor == "#777777" && runtime.Settings.GlowEnabled,
                    "Accessibility overrides never rewrite saved appearance settings");
            }
        }
        finally
        {
            foreach (var key in keys) resources.Remove(key);
            theme.ApplyTheme("Dark");
            editor.Close(); launcher.Close();
        }
    }
}
