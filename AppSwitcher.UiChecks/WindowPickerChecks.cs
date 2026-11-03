using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using AppSwitcher;
using AppSwitcher.Models;
using AppSwitcher.Services;
using AppSwitcher.ViewModels;
using AppSwitcher.Tests;
using Microsoft.Extensions.Logging.Abstractions;
using System.Windows.Threading;

internal static partial class Program
{
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint handle, int command);
    [DllImport("user32.dll", EntryPoint = "SetWindowTextW", CharSet = CharSet.Unicode)] private static extern bool SetWindowText(nint handle, string text);

    private static async Task CheckNativeWindowCatalog()
    {
        var title = "AppSwitcher picker probe " + Guid.NewGuid().ToString("N");
        var info = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
        info.ArgumentList.Add("--probe-window"); info.ArgumentList.Add(title);
        using var child = Process.Start(info)!;
        try
        {
            var catalog = new OpenWindowCatalog();
            OpenWindowCandidate? candidate = null;
            var deadline = Stopwatch.StartNew();
            var scan = new Stopwatch();
            while (candidate == null && deadline.Elapsed < TimeSpan.FromSeconds(10))
            {
                scan.Restart();
                var candidates = await catalog.ScanAsync(CancellationToken.None);
                scan.Stop();
                Check(candidates.All(row => row.ProcessId != Environment.ProcessId), "Window picker excludes its own process");
                candidate = candidates.FirstOrDefault(row => row.ProcessId == child.Id && row.Title == title);
                if (candidate == null) await Task.Delay(100);
            }
            Check(candidate != null, "Native picker discovers a real application window");
            Check(candidate!.Kind == OpenWindowKind.Native && candidate.ProcessCreated > 0
                && OpenWindowCatalog.SameExecutable(candidate.ExecutablePath, Environment.ProcessPath!), "Native picker reads executable path and process birth time");
            ShowWindow(candidate.Handle, 6);
            Check((await catalog.ScanAsync(CancellationToken.None)).Any(row => row.ProcessId == child.Id), "Minimized windows remain available in the picker");
            SetWindowText(candidate.Handle, title + " changed document");
            Check(await catalog.RevalidateAsync(candidate, CancellationToken.None) is { }, "A document title change preserves application identity");
            child.CloseMainWindow();
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Check(await catalog.RevalidateAsync(candidate, CancellationToken.None) == null, "A closed native window fails acceptance revalidation");
            File.WriteAllText(Path.Combine(Output, "window-picker-native.json"), System.Text.Json.JsonSerializer.Serialize(new
            { scanMilliseconds = scan.Elapsed.TotalMilliseconds, verified = new[] { "own-process exclusion", "path and creation time", "minimized window", "changing document title", "closed window rejected" } }));
        }
        finally
        {
            if (!child.HasExited) { child.Kill(); await child.WaitForExitAsync(); }
        }
    }

    private static async Task CheckWindowPickerPresentation(string language, string theme, bool compact)
    {
        var panel = new OpenWindowCandidate((nint)42, 123, 456, @"C:\Industrial applications\Production panel\panel.exe",
            "Production overview — line 4", "panel", OpenWindowKind.Native);
        var browser = panel with { Handle = 43, ExecutablePath = @"C:\Browser\msedge.exe", Title = "Production documentation — Edge", AppName = "msedge", Kind = OpenWindowKind.Website, Browser = BrowserKind.Edge };
        var unavailable = panel with { Handle = 44, ExecutablePath = "", AppName = "", Title = "Protected application", Kind = OpenWindowKind.Unavailable };
        var existing = panel with { Handle = 45, ExecutablePath = @"C:\Tools\quality.exe", Title = "Quality inspection", AppName = "quality" };
        var catalog = new PickerFixtureCatalog([panel, browser, unavailable, existing]);
        using var model = new OpenWindowPickerViewModel(catalog, () => true,
            () => [new AppItem { Name = "Quality", Path = existing.ExecutablePath }]);
        var picker = new OpenWindowPicker(model) { Width = compact ? 600 : 780, Height = compact ? 480 : 640 };
        picker.Show();
        await model.RefreshCommand.ExecutionTask!;
        await Render();
        Check(!((Button)picker.FindName("AddButton")).IsEnabled, "Picker requires an explicit selection");
        model.SelectedWindow = model.Windows[0];
        await Render();
        CheckControls(picker);
        Check(((TextBox)picker.FindName("AppNameBox")).IsVisible, "Native selection offers a reviewable name");
        model.Name = " ";
        Check(!model.ConfirmCommand.CanExecute(null), "A native app requires a nonempty name");
        model.Name = "panel";
        Save(picker, $"window-picker-{language}-{theme.ToLowerInvariant()}-native");
        model.SelectedWindow = model.Windows[1];
        await Render();
        CheckControls(picker);
        Check(!((TextBox)picker.FindName("AppNameBox")).IsVisible, "Browser windows route to website setup");
        Save(picker, $"window-picker-{language}-{theme.ToLowerInvariant()}-website");
        model.SelectedWindow = model.Windows[2];
        await Render();
        Check(!((Button)picker.FindName("AddButton")).IsEnabled && model.ManualCommand.CanExecute(null), "Unreadable app has a manual fallback without a blind import");
        model.SelectedWindow = model.Windows[3];
        await Render();
        Check(model.ActionLabel == LocalizationService.T("WindowPicker_ShowExisting"), "Duplicate entry offers review instead of another entry");
        model.Search = "nothing matches";
        await Render();
        Check(model.IsEmpty && !((Button)picker.FindName("AddButton")).IsEnabled, "Empty filtered picker cannot add a stale selection");
        Save(picker, $"window-picker-{language}-{theme.ToLowerInvariant()}-empty");
        picker.Close();
        Check(!model.RefreshCommand.CanExecute(null), "Closing picker releases its snapshot and disables further work");
    }

    private sealed class PickerFixtureCatalog(IReadOnlyList<OpenWindowCandidate> windows) : IOpenWindowCatalog
    {
        public Task<IReadOnlyList<OpenWindowCandidate>> ScanAsync(CancellationToken token) => Task.FromResult(windows);
        public Task<OpenWindowCandidate?> RevalidateAsync(OpenWindowCandidate candidate, CancellationToken token) => Task.FromResult<OpenWindowCandidate?>(candidate);
    }

    private static async Task CheckWindowPickImport(SettingsWindow window, SettingsViewModel settings)
    {
        var original = settings.SelectedApp;
        var originalApps = settings.Apps.ToArray();
        var count = originalApps.Length;
        var candidate = new OpenWindowCandidate((nint)42, 123, 456, @"C:\Apps\window-picker-fixture.exe", "Changing document", "window-picker-fixture", OpenWindowKind.Native);
        try
        {
            Check(settings.ApplyWindowPick(new(WindowPickAction.Native, candidate, "Imported panel")), "Verified native picker selection is imported");
            var imported = settings.SelectedApp!;
            Check(imported.Path == candidate.ExecutablePath && imported.Name == "Imported panel" && imported.IsNative,
                "Picker fills the reviewed name and executable");
            Check(imported.WinPattern == "" && imported.Args == "" && imported.WorkingDirectory == "" && !imported.AutoLaunch && !imported.WatchdogEnabled
                && imported.RequireConfirmation, "Import preserves safe defaults and does not capture a document title or command line");
            Check(settings.ApplyWindowPick(new(WindowPickAction.Native, candidate, "Duplicate")) && settings.Apps.Count == count + 1
                && settings.SelectedApp == imported && imported.Name == "Imported panel", "Repeated import selects the existing entry without renaming it");
            var browser = candidate with { ExecutablePath = @"C:\Browser\firefox.exe", Kind = OpenWindowKind.Website, Browser = BrowserKind.Firefox };
            Check(!settings.ApplyWindowPick(new(WindowPickAction.Native, browser, "Browser")), "A browser cannot enter through the native import route");
            Check(settings.ApplyWindowPick(new(WindowPickAction.Website, browser)), "Browser selection opens explicit website setup");
            var website = settings.SelectedApp!;
            Check(website.IsWebsite && website.Browser == BrowserKind.Firefox && website.Path == "" && website.WebsiteUrl == "" && website.BrowserProfileId == "",
                "Website setup does not guess a URL or bind a profile from a window title");
            settings.Apps.Remove(website);
            settings.Runtime.CurrentAccessLevel = AccessLevel.None;
            Check(!settings.AddFromOpenWindowCommand.CanExecute(null)
                && !settings.ApplyWindowPick(new(WindowPickAction.Manual)), "Picker and import reject expired admin access");
        }
        finally
        {
            settings.Runtime.CurrentAccessLevel = AccessLevel.Admin;
            foreach (var added in settings.Apps.Except(originalApps).ToArray()) settings.Apps.Remove(added);
            settings.SelectedApp = original;
        }
        await Render();
        var add = (Button)window.FindName("AddAppMenuButton");
        Invoke(add); await Render();
        Check(add.ContextMenu.Items.OfType<MenuItem>().Any(item => ReferenceEquals(item.Command, settings.AddFromOpenWindowCommand)),
            "Add app exposes the native window picker");
        add.ContextMenu.IsOpen = false;
        var closed = new TaskCompletionSource();
        _ = window.Dispatcher.InvokeAsync(async () =>
        {
            await Render();
            var picker = Application.Current.Windows.OfType<OpenWindowPicker>().Single();
            try { Check(picker.Owner == window, "Window picker belongs to the settings editor"); }
            catch (Exception ex) { closed.TrySetException(ex); }
            finally { picker.Close(); closed.TrySetResult(); }
        });
        settings.AddFromOpenWindowCommand.Execute(null);
        await closed.Task;
        Check(settings.Apps.Count == count, "Cancelling the actual picker does not create an app");
    }

    private static async Task CheckWindowPickerSessionExpiry(MainViewModelAuditTests.TestHarness fixture, SessionTimeoutService session)
    {
        var runtime = fixture.ViewModel;
        foreach (var expireSession in new[] { false, true })
        {
            runtime.CurrentAccessLevel = AccessLevel.Admin;
            await runtime.SaveConfigAsync(); // This scenario tests owner closure, not discard confirmation.
            using var settings = new SettingsViewModel(runtime, fixture.ConfigService, new SecurityService(), fixture.NotificationService,
                new ScreenLayoutService(NullLogger<ScreenLayoutService>.Instance, fixture.ProcessHelper),
                new CrashHistoryService(NullLogger<CrashHistoryService>.Instance), session, AccessLevel.Admin);
            var owner = new SettingsWindow { DataContext = settings };
            runtime.PrepareWindow(owner, privileged: true);
            owner.Show(); await Render();
            var originalCount = settings.Apps.Count;
            var deadline = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
            deadline.Tick += (_, _) => { deadline.Stop(); Console.Error.WriteLine("Picker modal did not exit after owner close/session expiry."); Application.Current.Shutdown(1); };
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                if (expireSession)
                    typeof(MainViewModel).GetMethod("OnSessionTimedOut", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(runtime, null);
                else owner.Close();
            };
            deadline.Start(); timer.Start();
            try { settings.AddFromOpenWindowCommand.Execute(null); }
            finally { timer.Stop(); deadline.Stop(); }
            await Render();
            Check((!expireSession || runtime.CurrentAccessLevel == AccessLevel.None) && !owner.IsVisible
                && !Application.Current.Windows.OfType<OpenWindowPicker>().Any(), "Owner close/session expiry closes the picker and exits its modal frame");
            Check(settings.Apps.Count == originalCount, "A closed or expired picker cannot create an entry");
            foreach (var toast in Application.Current.Windows.OfType<ToastWindow>().ToArray()) toast.Close();
        }
    }
}
