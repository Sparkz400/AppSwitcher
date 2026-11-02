using System.IO;
using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AppSwitcher;
using AppSwitcher.Models;
using AppSwitcher.Services;
using AppSwitcher.Tests;
using AppSwitcher.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;

internal static partial class Program
{
    private static int RunRecoveryHost(string directory)
    {
        AppPaths.IsolatedDataDirectory = directory;
        var app = new App();
        app.InitializeComponent();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        var attempts = 0;
        var reviewed = false;
        var authenticated = false;
        var confirmed = false;
        var deadline = Stopwatch.StartNew();
        timer.Tick += (_, _) =>
        {
            if (deadline.Elapsed > TimeSpan.FromSeconds(20)) { timer.Stop(); app.Shutdown(1); return; }
            var recovery = app.Windows.OfType<ConfigurationRecoveryWindow>().FirstOrDefault();
            if (recovery == null) return;
            if (recovery.Restored)
            {
                timer.Stop();
                File.WriteAllText(Path.Combine(directory, "recovery-result.json"), JsonSerializer.Serialize(new
                {
                    restored = true, authenticated, confirmed,
                    launcherOpened = app.Windows.OfType<MainWindow>().Any(),
                    preserved = Directory.GetFiles(Path.Combine(directory, "recovery")).Length
                }));
                recovery.Close();
                return;
            }
            if (app.Windows.OfType<PasswordDialog>().FirstOrDefault() is { } pin)
            {
                if (attempts++ > 0) return;
                // Exercise the actual PIN control using its keypad and confirmation handlers.
                foreach (var digit in new[] { "1", "2", "3", "4" })
                    Invoke(Descendants<Button>(pin).Single(button => Equals(button.Content, digit)));
                Invoke(Descendants<Button>(pin).Single(button => button.IsDefault));
                authenticated = true;
            }
            else if (app.Windows.OfType<ConfirmationDialog>().FirstOrDefault() is { } confirmation)
            {
                confirmed = true;
                Invoke((Button)confirmation.FindName("ConfirmButton"));
            }
            else if (!reviewed)
            {
                reviewed = true;
                ((ListBox)recovery.FindName("BackupList")).SelectedIndex = 0;
                Invoke((Button)recovery.FindName("RestoreButton"));
            }
        };
        timer.Start();
        try { return app.Run(); }
        finally { timer.Stop(); }
    }

    private static async Task<int> CheckRecoveryProcess()
    {
        var directory = Path.Combine(Path.GetTempPath(), "AppSwitcher-RecoveryProcess-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(directory, "config_backups"));
        var passed = false;
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "app_config.json"), "broken original");
            var backup = new ConfigRoot { Settings = new AppSettings { AdminPasswordHash = new SecurityService().HashPassword("1234") } };
            await File.WriteAllTextAsync(Path.Combine(directory, "config_backups", "app_config_20261102_120000.json"), JsonSerializer.Serialize(backup));
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
            start.ArgumentList.Add("--recovery-host"); start.ArgumentList.Add(directory);
            using var host = Process.Start(start)!;
            try { await host.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30)); }
            finally { if (!host.HasExited) { host.Kill(entireProcessTree: true); await host.WaitForExitAsync(); } }
            if (host.ExitCode != 0) throw new InvalidOperationException("The recovery host did not finish successfully.");
            var receipt = await File.ReadAllTextAsync(Path.Combine(directory, "recovery-result.json"));
            using var result = JsonDocument.Parse(receipt);
            var root = result.RootElement;
            if (!root.GetProperty("restored").GetBoolean() || !root.GetProperty("authenticated").GetBoolean()
                || !root.GetProperty("confirmed").GetBoolean() || root.GetProperty("launcherOpened").GetBoolean()
                || root.GetProperty("preserved").GetInt32() != 1) throw new InvalidOperationException("Incomplete recovery flow.");
            var original = Directory.GetFiles(Path.Combine(directory, "recovery")).Single();
            if (await File.ReadAllTextAsync(original) != "broken original") throw new InvalidOperationException("Failed configuration was not preserved.");
            Directory.CreateDirectory(Output);
            await File.WriteAllTextAsync(Path.Combine(Output, "recovery-process.json"), receipt);
            Console.WriteLine("Native startup recovery passed: authenticated backup, explicit confirmation, original preserved, no launcher opened.");
            passed = true;
            return 0;
        }
        finally
        {
            if (passed) Directory.Delete(directory, true);
            else Console.Error.WriteLine($"Failed recovery evidence retained: {directory}");
        }
    }

    private static async Task CheckConfigurationRecoveryPresentation(string language, string theme, bool compact)
    {
        using var config = new RecoveryPresentationService();
        var window = new ConfigurationRecoveryWindow(config, new SecurityService(), "Synthetic configuration error; no user file was read.");
        if (compact) { window.Width = 600; window.Height = 480; }
        window.Show(); await Render();
        CheckControls(window);
        Check(!((Button)window.FindName("RestoreButton")).IsEnabled, "Recovery requires a selected backup");
        Save(window, $"configuration-recovery-{language}-{theme}-empty");
        ((ListBox)window.FindName("BackupList")).SelectedIndex = 0;
        Invoke((Button)window.FindName("RestoreButton"));
        await Render();
        Check(((TextBlock)window.FindName("StatusText")).Text == LocalizationService.T("Recovery_InvalidBackup"),
            "Unreadable recovery backup has a visible explanation and can be retried");
        Check(!window.Restored && ((Button)window.FindName("RestoreButton")).IsEnabled, "Failed recovery does not report success or disable retry");
        Save(window, $"configuration-recovery-{language}-{theme}-invalid");
        Descendants<Expander>(window).Single().IsExpanded = true;
        ((TextBlock)window.FindName("FailureText")).Text = string.Join(" ", Enumerable.Repeat("Synthetic long configuration failure detail.", 60));
        await Render();
        var close = (Button)window.FindName("CloseButton");
        var position = close.TranslatePoint(new Point(), window);
        Check(position.Y + close.ActualHeight <= window.ActualHeight, "Long recovery details keep the close action in the window");
        Save(window, $"configuration-recovery-{language}-{theme}-details");
        window.Close();

        config.Empty = true;
        var empty = new ConfigurationRecoveryWindow(config, new SecurityService(), "Synthetic error");
        empty.Show(); await Render();
        Check(((TextBlock)empty.FindName("StatusText")).Text == LocalizationService.T("Recovery_NoBackups"), "Recovery without backups explains the repair path");
        empty.Close();
    }

    private static async Task CheckPinDialogSessionExpiry()
    {
        using var fixture = await MainViewModelAuditTests.CreateHarnessAsync();
        using var session = new SessionTimeoutService(NullLogger<SessionTimeoutService>.Instance);
        var runtime = fixture.ViewModel;
        var hash = new SecurityService().HashPassword("1234");
        runtime.Settings.AdminPasswordHash = hash;
        using var settings = new SettingsViewModel(runtime, fixture.ConfigService, new SecurityService(), fixture.NotificationService,
            new ScreenLayoutService(NullLogger<ScreenLayoutService>.Instance, fixture.ProcessHelper),
            new CrashHistoryService(NullLogger<CrashHistoryService>.Instance), session, AccessLevel.Admin);
        foreach (var command in new System.Windows.Input.ICommand[] { settings.SetAdminPinCommand, settings.SetOperatorPinCommand, settings.RemoveAdminPinCommand })
        {
            runtime.CurrentAccessLevel = AccessLevel.Admin;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            var deadline = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            deadline.Tick += (_, _) => { deadline.Stop(); Console.Error.WriteLine("PIN dialog did not close after session expiry."); Application.Current.Shutdown(1); };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                typeof(MainViewModel).GetMethod("OnSessionTimedOut", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(runtime, null);
            };
            timer.Start(); deadline.Start();
            try { command.Execute(null); }
            finally { timer.Stop(); deadline.Stop(); }
            await Render();
            Check(!Application.Current.Windows.OfType<PasswordDialog>().Any(window => window.IsVisible), "Session expiry closes privileged PIN dialogs");
            Check(runtime.Settings.AdminPasswordHash == hash && runtime.Settings.OperatorPasswordHash == "", "Expired PIN dialogs cannot change credentials");
        }
    }

    private sealed class RecoveryPresentationService() : ConfigService(NullLogger<ConfigService>.Instance,
        new ConfigValidationService(NullLogger<ConfigValidationService>.Instance))
    {
        public bool Empty { get; set; }
        public override List<ConfigBackupInfo> GetBackups() => Empty ? [] :
            [new("synthetic-backup.json", "app_config_20261102_120000.json", new DateTime(2026, 11, 2, 12, 0, 0), 1024)];
        public override Task<BackupPreview?> ReadBackupPreviewAsync(string path) => Task.FromResult<BackupPreview?>(null);
    }
}
