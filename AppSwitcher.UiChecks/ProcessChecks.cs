using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using AppSwitcher;
using AppSwitcher.Models;
using AppSwitcher.Services;
using AppSwitcher.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;

internal static partial class ProcessChecks
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);

    internal static int RunProbe(string[] args)
    {
        var app = new Application();
        app.Run(new Window { Title = args[1], Width = 420, Height = 220,
            Content = new TextBlock { Text = "AppSwitcher isolated test application", Margin = new Thickness(20) } });
        return args.Length > 2 ? int.Parse(args[2]) : 0;
    }

    internal static int RunAppHost(string directory)
    {
        AppPaths.IsolatedDataDirectory = directory;
        var rendering = Environment.GetEnvironmentVariable("APPSWITCHER_UI_TEST_RENDERING");
        AppContext.SetSwitch("AppSwitcher.UseHardwareRendering", rendering == "hardware");
        if (rendering == "software") System.Windows.Media.RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
        var app = new App();
        app.InitializeComponent();
        app.Startup += async (_, _) =>
        {
            while (app.MainWindow?.DataContext is not MainViewModel)
            {
                if (app.Dispatcher.HasShutdownStarted) return;
                await Task.Delay(20);
            }
            var vm = (MainViewModel)app.MainWindow.DataContext;
            app.MainWindow.ContentRendered += (_, _) => File.WriteAllText(Path.Combine(directory, "rendered.json"), "true");
            vm.CurrentAccessLevel = AccessLevel.Admin;
            var ready = JsonSerializer.Serialize(new { pid = Environment.ProcessId,
                hwnd = new WindowInteropHelper(app.MainWindow).Handle.ToInt64() });
            await File.WriteAllTextAsync(Path.Combine(directory, "ready.tmp"), ready);
            File.Move(Path.Combine(directory, "ready.tmp"), Path.Combine(directory, "ready.json"), true);
            _ = Task.Run(async () =>
            {
                while (!app.Dispatcher.HasShutdownStarted)
                {
                    using var pipe = new NamedPipeServerStream("AppSwitcher.UiCheck." + Environment.ProcessId,
                        PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await pipe.WaitForConnectionAsync();
                    using var reader = new StreamReader(pipe, leaveOpen: true);
                    using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
                    var command = await reader.ReadLineAsync();
                    if (command == "scroll-workload")
                    {
                        var workload = await app.Dispatcher.InvokeAsync(() => StartupMemoryChecks.ScrollWorkloadAsync(app));
                        await writer.WriteLineAsync(await workload);
                        continue;
                    }
                    var response = await app.Dispatcher.InvokeAsync(() =>
                    {
                        var settings = app.Windows.OfType<SettingsWindow>().FirstOrDefault();
                        if (command == "settings") _ = app.Dispatcher.BeginInvoke(() => vm.OpenSettingsCommand.Execute(null));
                        if (command == "minimize" && settings != null) settings.WindowState = WindowState.Minimized;
                        if (command == "close-settings" && settings != null) settings.Close();
                        if (command == "restart") _ = app.Dispatcher.BeginInvoke(() =>
                            ((SettingsViewModel)settings!.DataContext).RestartLauncherCommand.Execute(null));
                        if (command == "quit") _ = app.Dispatcher.BeginInvoke(async () =>
                        { await vm.ShutdownAsync(stopManagedApps: false); app.Shutdown(); });
                        return JsonSerializer.Serialize(new
                        {
                            pid = Environment.ProcessId,
                            settings = settings == null ? 0 : new WindowInteropHelper(settings).Handle.ToInt64(),
                            minimized = settings?.WindowState == WindowState.Minimized,
                            managedBytes = GC.GetTotalMemory(false), committedGcBytes = GC.GetGCMemoryInfo().TotalCommittedBytes,
                            apps = vm.Apps.Select(item => new { item.Name, state = item.State.ToString(), item.IsRunning, item.CrashCountToday }).ToArray()
                        });
                    });
                    try { await writer.WriteLineAsync(response); } catch (IOException) { }
                }
            });
        };
        return app.Run();
    }

    private static Process Start(string[] arguments, string? directory = null)
    {
        var info = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        if (directory != null) info.Environment["APPSWITCHER_UI_TEST_DATA"] = directory;
        else info.Environment.Remove("APPSWITCHER_UI_TEST_DATA");
        return Process.Start(info)!;
    }

    internal static async Task<JsonElement> Request(int pid, string command)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var pipe = new NamedPipeClientStream(".", "AppSwitcher.UiCheck." + pid, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(timeout.Token);
        using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(pipe, leaveOpen: true);
        await writer.WriteLineAsync(command);
        using var document = JsonDocument.Parse((await reader.ReadLineAsync(timeout.Token))!);
        return document.RootElement.Clone();
    }

    private static async Task Until(Func<Task<bool>> predicate, string message, int seconds = 15)
    {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < TimeSpan.FromSeconds(seconds))
        {
            if (await predicate()) return;
            await Task.Delay(50);
        }
        throw new InvalidOperationException(message);
    }

    internal static async Task<int> RunAsync()
    {
        var directory = Path.GetFullPath(Path.Combine("artifacts", "process-checks", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(directory);
        var key = Guid.NewGuid().ToString("N");
        var titleA = "AppSwitcher test A " + key;
        var titleB = "AppSwitcher test B " + key;
        var owned = new List<Process>();
        var hostPid = 0;
        var path = Environment.ProcessPath!;
        using var helper = new ProcessHelper(NullLogger<ProcessHelper>.Instance);
        try
        {
            var a = Start(["--probe-window", titleA]); owned.Add(a);
            var b = Start(["--probe-window", titleB]); owned.Add(b);
            await Until(() => { a.Refresh(); b.Refresh(); return Task.FromResult(a.MainWindowHandle != IntPtr.Zero && b.MainWindowHandle != IntPtr.Zero); }, "Probe windows did not start");
            var config = new ConfigRoot
            {
                Settings = new AppSettings { Language = "en", PosX = 40, PosY = 40 },
                Apps = [new() { Name = "A", Path = path, WinPattern = titleA, Args = $"--probe-window \"{titleA}\"", AutoLaunch = true, WatchdogEnabled = true },
                    new() { Name = "B", Path = path, WinPattern = titleB, Args = $"--probe-window \"{titleB}\"", AutoLaunch = true }]
            };
            await File.WriteAllTextAsync(Path.Combine(directory, "app_config.json"), JsonSerializer.Serialize(config, AppJsonOptions.Default));
            var startup = Stopwatch.StartNew();
            var host = Start(["--app-host"], directory); owned.Add(host); hostPid = host.Id;
            await Until(() => Task.FromResult(File.Exists(Path.Combine(directory, "ready.json"))), "Application host did not start");
            var startupMs = startup.Elapsed.TotalMilliseconds;
            await Until(async () => (await Request(hostPid, "state")).GetProperty("apps").EnumerateArray().All(item => item.GetProperty("IsRunning").GetBoolean()), "Applications were not observed");
            a.CloseMainWindow(); await a.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            await Until(async () => (await Request(hostPid, "state")).GetProperty("apps")[0].GetProperty("state").GetString() == "Stopped", "Manual exit was not classified as stopped");
            await Task.Delay(1500);
            var afterClose = await Request(hostPid, "state");
            if (afterClose.GetProperty("apps")[0].GetProperty("CrashCountToday").GetInt32() != 0
                || afterClose.GetProperty("apps")[0].GetProperty("IsRunning").GetBoolean()) throw new InvalidOperationException("Manual close counted/restarted");
            await Request(hostPid, "settings");
            await Until(async () => (await Request(hostPid, "state")).GetProperty("settings").GetInt64() != 0, "Settings did not open");
            var activations = new List<double>();
            var foregroundActivations = 0;
            for (var index = 0; index < 5; index++)
            {
                var state = await Request(hostPid, "minimize");
                var handle = new IntPtr(state.GetProperty("settings").GetInt64());
                var activation = Stopwatch.StartNew();
                var duplicate = Start(["--app-host"], directory); owned.Add(duplicate);
                await duplicate.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
                await Until(async () => !(await Request(hostPid, "state")).GetProperty("minimized").GetBoolean(), "Second launch did not restore settings");
                var restored = await Request(hostPid, "state");
                if (restored.GetProperty("settings").GetInt64() != handle.ToInt64())
                    throw new InvalidOperationException("Second launch replaced the original settings window");
                // A background test runner cannot assume foreground permission.
                // User input in another app can revoke it at any point.
                if (GetForegroundWindow() == handle) foregroundActivations++;
                activations.Add(activation.Elapsed.TotalMilliseconds);
            }
            await Request(hostPid, "restart");
            await host.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(40));
            await Until(async () =>
            {
                using var ready = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory, "ready.json")));
                var newPid = ready.RootElement.GetProperty("pid").GetInt32();
                if (newPid == hostPid) return false;
                hostPid = newPid; return true;
            }, "Restart did not produce a new instance", 40);
            owned.Add(Process.GetProcessById(hostPid));
            await Until(async () => (await Request(hostPid, "state")).GetProperty("apps")[1].GetProperty("IsRunning").GetBoolean(), "Running app was lost after restart");
            if (b.HasExited || helper.IsAppRunning(config.Apps[0].ProcessTarget)) throw new InvalidOperationException("Restart changed managed application lifecycle");
            var switchTimes = new List<double>();
            var foregroundSwitchTimes = new List<double>();
            for (var index = 0; index < 30; index++)
            {
                b.Refresh(); ShowWindow(b.MainWindowHandle, 6);
                var timer = Stopwatch.StartNew();
                helper.BringToFront(config.Apps[1].ProcessTarget);
                await Until(() => Task.FromResult(!IsIconic(b.MainWindowHandle)), "Native switching did not restore the existing window", 3);
                switchTimes.Add(timer.Elapsed.TotalMilliseconds);
                if (GetForegroundWindow() == b.MainWindowHandle) foregroundSwitchTimes.Add(timer.Elapsed.TotalMilliseconds);
            }
            var failure = Start(["--probe-window", titleA, "7"]); owned.Add(failure);
            await Until(async () => (await Request(hostPid, "state")).GetProperty("apps")[0].GetProperty("IsRunning").GetBoolean(), "Failing probe was not observed");
            failure.CloseMainWindow(); await failure.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            await Until(async () => (await Request(hostPid, "state")).GetProperty("apps")[0].GetProperty("CrashCountToday").GetInt32() > 0, "Confirmed failure did not count as a crash");
            await Until(async () => (await Request(hostPid, "state")).GetProperty("apps")[0].GetProperty("IsRunning").GetBoolean(), "Confirmed failure did not recover");
            // Capture the recovery child after its window exists, before detaching.
            // Process.Start can return before the child's title is discoverable.
            owned.AddRange(helper.GetMatchingProcesses(config.Apps[0].ProcessTarget));
            await Request(hostPid, "quit");
            await Process.GetProcessById(hostPid).WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            if (b.HasExited) throw new InvalidOperationException("Detach shutdown closed the managed application");
            switchTimes.Sort(); activations.Sort(); foregroundSwitchTimes.Sort();
            var report = new { runtime = Environment.Version.ToString(), processors = Environment.ProcessorCount,
                scenario = "Real process lifecycle and native window switching, isolated synthetic configuration in UiChecks host",
                initialStartupMilliseconds = startupMs, repeatedLaunchMilliseconds = activations, repeatedLaunchForegroundCount = foregroundActivations,
                nativeSwitch = new { trials = switchTimes.Count, restoredMedianMilliseconds = switchTimes[15], restoredP95Milliseconds = switchTimes[28], restoredMaximumMilliseconds = switchTimes[^1],
                    confirmedForegroundTrials = foregroundSwitchTimes.Count,
                    confirmedActivationMedianMilliseconds = foregroundSwitchTimes.Count == 0 ? (double?)null : foregroundSwitchTimes[foregroundSwitchTimes.Count / 2] },
                verified = new[] { "normal exit stays stopped without crash count/restart", "failed exit counted and recovered", "five second launches restore the same settings window", "restart preserves live PID and keeps closed autostart app stopped", "detach leaves managed app alive", "30 native switches restore the same existing window" },
                limitation = "UiChecks host includes test dependencies; startup is not production EXE startup. Native switching uses a synthetic WPF window, not a browser tab or industrial visualization. Foreground counts are reported separately: Windows may deny background-console activation or revoke permission when the user works in another app." };
            var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(Path.GetFullPath("artifacts/process-performance.json"), json);
            Console.WriteLine(json);
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally
        {
            // Only this run's unique fixture windows and explicitly launched processes.
            foreach (var target in new[] { new AppProcessTarget(path, titleA), new AppProcessTarget(path, titleB) })
                foreach (var process in helper.GetMatchingProcesses(target))
                    using (process)
                    {
                        try
                        {
                            process.CloseMainWindow();
                            if (!process.WaitForExit(1000)) process.Kill();
                        }
                        catch (InvalidOperationException) { }
                    }
            foreach (var process in owned)
            {
                try { if (!process.HasExited) process.Kill(); } catch (InvalidOperationException) { }
                process.Dispose();
            }
        }
    }
}
