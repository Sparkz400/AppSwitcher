using AppSwitcher;
using AppSwitcher.Models;
using AppSwitcher.Services;
using AppSwitcher.ViewModels;
using System.Windows.Controls;
using System.Windows;
using System.Windows.Threading;
using AppSwitcher.Tests;
using Microsoft.Extensions.Logging.Abstractions;
using System.IO;
using System.Diagnostics;
using System.Text.Json;

internal static partial class Program
{
    private static async Task CheckBrowserSetupPackage(string bundle)
    {
        var root = Path.Combine(Path.GetTempPath(), "AppSwitcher-PackageCheck-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            foreach (var browser in Enum.GetValues<BrowserKind>())
            {
                string? registered = null;
                var setup = new BrowserSetupService(Path.GetFullPath(bundle), root, (_, manifest) => registered = manifest);
                var result = await setup.PrepareAsync(browser, default);
                Check(result.Browser == browser && File.Exists(registered), "Published setup prepares the requested browser");
                var bundledXpi = Path.Combine(bundle, "appswitcher-firefox.xpi");
                if (browser == BrowserKind.Firefox && File.Exists(bundledXpi))
                {
                    Check(result.InstallKind == BrowserExtensionInstall.FirefoxFile && File.Exists(result.InstallTarget), "Published Firefox setup offers the bundled offline XPI");
                    Check(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(bundledXpi)).SequenceEqual(
                        System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(result.InstallTarget))), "Setup preserves the Mozilla-signed XPI byte for byte");
                }
                using var manifest = JsonDocument.Parse(File.ReadAllText(registered!));
                var host = manifest.RootElement.GetProperty("path").GetString()!;
                var info = new ProcessStartInfo(host) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
                info.ArgumentList.Add("unregistered-package-check-origin");
                using var process = Process.Start(info)!;
                try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
                finally { if (!process.HasExited) { process.Kill(); await process.WaitForExitAsync(); } }
                Check(process.ExitCode == 1 && (await process.StandardError.ReadToEndAsync()).Contains("Unregistered browser extension origin"), "Published self-contained host runs and rejects an unapproved origin");
                Check((await process.StandardOutput.ReadToEndAsync()).Length == 0, "Host diagnostics do not corrupt the native messaging channel");
                var original = registered;
                await setup.PrepareAsync(browser, default);
                Check(original == registered, "Published setup is idempotent");
            }
        }
        finally { Directory.Delete(root, true); }
    }

    private static async Task CheckBrowserSetupPresentation(string language, string theme, bool compact)
    {
        IReadOnlyList<BrowserProfileChoice> profiles = [];
        using var model = new BrowserSetupViewModel(BrowserKind.Edge, "", new SetupPresentationService(), () => profiles, () => true);
        var window = new BrowserSetupWindow(model, start: false);
        if (compact) { window.Width = 540; window.Height = 480; }
        window.Show(); await model.PrepareCommand.ExecuteAsync(null); await Render();
        Check(model.HostReady && !model.CanFinish && model.ShowFolder, "Setup prepares bridge and waits for extension approval");
        Check(!model.OpenStartupCommand.CanExecute(null) && model.InstallSteps.Count == 3, "Offline installation gives three steps before startup settings");
        CheckControls(window); Save(window, $"browser-setup-{language}-{theme.ToLowerInvariant()}-install");
        var first = new BrowserProfileChoice(Guid.NewGuid().ToString(), BrowserKind.Edge, "Production");
        var second = new BrowserProfileChoice(Guid.NewGuid().ToString(), BrowserKind.Edge, "Maintenance");
        profiles = [first, second]; model.RefreshConnection(); await Render();
        Check(model.ShowProfiles && !model.CanFinish, "Setup requires a choice between connected profiles");
        ((ComboBox)window.FindName("ProfilePicker")).SelectedItem = second; await Render();
        Check(model.CanFinish && !model.ShowFolder, "Setup hides installation steps after connection");
        CheckControls(window); Save(window, $"browser-setup-{language}-{theme.ToLowerInvariant()}-connected");
        profiles = []; model.RefreshConnection(); await Render();
        Check(!model.CanFinish, "Disconnected profile cannot complete setup");
        window.Close();
        Check(!model.Authorized, "Closing setup releases the operation lifetime");
        using var firefox = new BrowserSetupViewModel(BrowserKind.Firefox, "", new SetupPresentationService(), () => [], () => true);
        var firefoxWindow = new BrowserSetupWindow(firefox, start: false);
        if (compact) { firefoxWindow.Width = 540; firefoxWindow.Height = 480; }
        firefoxWindow.Show(); await firefox.PrepareCommand.ExecuteAsync(null); await Render();
        Check(firefox.HostReady && !firefox.CanInstall && !firefox.CanFinish, "Unsigned Firefox package is not offered as permanent installation");
        Check(firefox.PackageBlocked && !firefox.ShowInstallActions && !firefox.ShowFinish
            && firefox.Status == LocalizationService.T("Setup_FirefoxBlocked"), "Missing Firefox payload is explained as a package prerequisite, without a dead-end install button");
        CheckControls(firefoxWindow); Save(firefoxWindow, $"browser-setup-{language}-{theme.ToLowerInvariant()}-firefox");
        firefoxWindow.Close();
    }
    private sealed class SetupPresentationService : IBrowserSetupService
    {
        public void CopyPath(string path) { }
        public Task<BrowserSetupPackage> PrepareAsync(BrowserKind browser, CancellationToken cancellation) => Task.FromResult(new BrowserSetupPackage(browser,
            browser == BrowserKind.Firefox ? BrowserExtensionInstall.Unavailable : BrowserExtensionInstall.Unpacked, BrowserSetupService.ExtensionsPage(browser),
            @"C:\Users\Operator\AppData\Local\Programs\AppSwitcherBrowserSetup\extensions\Edge\afmmehaldghpcccgholfklbeemokekoj"));
        public void Open(BrowserKind browser, string target) { }
    }

    private static async Task CheckBrowserSetupSessionExpiry(MainViewModelAuditTests.TestHarness fixture, SessionTimeoutService session)
    {
        var runtime = fixture.ViewModel;
        foreach (var expireSession in new[] { false, true })
        {
            runtime.CurrentAccessLevel = AccessLevel.Admin;
            await runtime.SaveConfigAsync();
            using var settings = new SettingsViewModel(runtime, fixture.ConfigService, new SecurityService(), fixture.NotificationService,
                new ScreenLayoutService(NullLogger<ScreenLayoutService>.Instance, fixture.ProcessHelper),
                new CrashHistoryService(NullLogger<CrashHistoryService>.Instance), session, AccessLevel.Admin);
            var pending = new PendingSetupService();
            settings.BrowserSetupServiceFactory = () => pending;
            var owner = new SettingsWindow { DataContext = settings };
            runtime.PrepareWindow(owner, privileged: true);
            owner.Show(); await Render();
            var originalCount = settings.Apps.Count;
            var deadline = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
            deadline.Tick += (_, _) => { deadline.Stop(); Console.Error.WriteLine("Browser setup modal did not exit."); Application.Current.Shutdown(1); };
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                if (expireSession)
                    typeof(MainViewModel).GetMethod("OnSessionTimedOut", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(runtime, null);
                else owner.Close();
            };
            deadline.Start(); timer.Start();
            try { settings.ConfigureBrowserConnectionCommand.Execute(new BrowserConnectionRow { Browser = BrowserKind.Edge }); }
            finally { timer.Stop(); deadline.Stop(); }
            await Render();
            Check((!expireSession || runtime.CurrentAccessLevel == AccessLevel.None) && !owner.IsVisible
                && !Application.Current.Windows.OfType<BrowserSetupWindow>().Any(), "Owner close/session expiry exits browser setup's modal frame");
            Check(pending.Cancellation.IsCancellationRequested && settings.Apps.Count == originalCount, "Closing setup cancels pending installation without editing applications");
            foreach (var toast in Application.Current.Windows.OfType<ToastWindow>().ToArray()) toast.Close();
        }
    }
    private sealed class PendingSetupService : IBrowserSetupService
    {
        public void CopyPath(string path) => throw new InvalidOperationException("Closed setup cannot modify the clipboard.");
        public CancellationToken Cancellation { get; private set; }
        public async Task<BrowserSetupPackage> PrepareAsync(BrowserKind browser, CancellationToken cancellation)
        {
            Cancellation = cancellation;
            await Task.Delay(Timeout.Infinite, cancellation);
            throw new InvalidOperationException("Unreachable");
        }
        public void Open(BrowserKind browser, string target) => throw new InvalidOperationException("Expired setup cannot launch a browser.");
    }
}
