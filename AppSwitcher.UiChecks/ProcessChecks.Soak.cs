using System.Diagnostics;
using System.IO;
using System.Text.Json;
using AppSwitcher.Models;
using AppSwitcher.Services;

internal static partial class ProcessChecks
{
    internal static async Task<int> RunSoakAsync(int minutes)
    {
        var root = Path.GetFullPath(Path.Combine("artifacts", "soak", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        var title = "AppSwitcher soak probe " + Guid.NewGuid().ToString("N");
        var samples = new List<object>();
        var owned = new List<Process>();
        var watch = Stopwatch.StartNew();
        var restartCount = 0;
        var passed = false;
        var configPath = Path.Combine(root, "app_config.json");
        var reportPath = Path.Combine(root, "results.json");
        async Task SaveReport() => await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
        {
            passed, requestedMinutes = minutes, elapsedSeconds = watch.Elapsed.TotalSeconds, abruptHostRestarts = restartCount,
            testedAssemblySha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(typeof(AppSwitcher.App).Assembly.Location))),
            scope = "Isolated real App/monitoring host with one synthetic managed window, settings interaction and process termination. No production configuration, OS reboot, power loss, network outage or hardware kiosk qualification.",
            samples
        }, new JsonSerializerOptions { WriteIndented = true }));
        async Task<Process> StartHost()
        {
            File.Delete(Path.Combine(root, "ready.json"));
            var process = Start(["--app-host"], root); owned.Add(process);
            await Until(() => Task.FromResult(File.Exists(Path.Combine(root, "ready.json"))), "Soak host did not become ready");
            await Until(async () => (await Request(process.Id, "state")).GetProperty("apps")[0].GetProperty("IsRunning").GetBoolean(), "Soak host lost the existing managed window");
            return process;
        }
        try
        {
            var probe = Start(["--probe-window", title]); owned.Add(probe);
            await Until(() => { probe.Refresh(); return Task.FromResult(probe.MainWindowHandle != IntPtr.Zero); }, "Soak probe did not start");
            var config = new ConfigRoot
            {
                Settings = new AppSettings { Language = "en", PosX = 40, PosY = 40 },
                Apps = [new() { Name = "Soak fixture", Path = Environment.ProcessPath!, WinPattern = title, WatchdogEnabled = true }]
            };
            await File.WriteAllTextAsync(configPath, JsonSerializer.Serialize(config, AppJsonOptions.Default));
            var host = await StartHost();
            var nextRestart = TimeSpan.FromMinutes(5);
            while (watch.Elapsed < TimeSpan.FromMinutes(minutes))
            {
                if (probe.HasExited) throw new InvalidOperationException("Soak managed process exited unexpectedly");
                var response = Stopwatch.StartNew();
                var state = await Request(host.Id, "state");
                if (!state.GetProperty("apps")[0].GetProperty("IsRunning").GetBoolean()
                    || state.GetProperty("apps")[0].GetProperty("CrashCountToday").GetInt32() != 0)
                    throw new InvalidOperationException("Healthy soak process was misclassified");
                var responseMs = response.Elapsed.TotalMilliseconds;
                await Request(host.Id, "settings");
                await Until(async () => (await Request(host.Id, "state")).GetProperty("settings").GetInt64() != 0, "Soak settings did not open");
                await Request(host.Id, "close-settings");
                host.Refresh();
                samples.Add(new { elapsedSeconds = watch.Elapsed.TotalSeconds, hostPid = host.Id, managedPid = probe.Id,
                    responseMs, privateBytes = host.PrivateMemorySize64, workingSetBytes = host.WorkingSet64,
                    handles = host.HandleCount, threads = host.Threads.Count, cpuSeconds = host.TotalProcessorTime.TotalSeconds,
                    managedBytes = state.GetProperty("managedBytes").GetInt64() });
                if (watch.Elapsed >= nextRestart)
                {
                    // Kill only the harness host; the independently started managed window must survive.
                    host.Kill(); await host.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                    using var saved = JsonDocument.Parse(await File.ReadAllTextAsync(configPath));
                    host = await StartHost(); restartCount++;
                    nextRestart += TimeSpan.FromMinutes(5);
                }
                await SaveReport();
                await Task.Delay(TimeSpan.FromSeconds(10));
            }
            await Request(host.Id, "quit");
            await host.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            if (probe.HasExited) throw new InvalidOperationException("Soak detach closed its managed window");
            passed = true;
            Console.WriteLine($"Soak passed: {watch.Elapsed.TotalMinutes:F1} minutes, {samples.Count} samples, {restartCount} abrupt host restarts. {reportPath}");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally
        {
            await SaveReport();
            foreach (var process in owned)
            {
                try { if (!process.HasExited) process.Kill(); } catch (InvalidOperationException) { }
                process.Dispose();
            }
        }
    }
}
