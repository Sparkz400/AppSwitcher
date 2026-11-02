using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AppSwitcher;
using AppSwitcher.Models;
using AppSwitcher.Services;
using AppSwitcher.ViewModels;

internal static class StartupMemoryChecks
{
    internal static async Task<int> RunAsync(string[] args)
    {
        var root = Path.GetFullPath(Path.Combine("artifacts", "startup-memory", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")));
        Directory.CreateDirectory(root);
        var runs = new List<object>();
        var exercised = new HashSet<string>();
        var comparison = args.Contains("--compare-hosts");
        var modes = comparison ? new[] { "baseline", "candidate", "candidate", "baseline", "baseline", "candidate" }
            : new[] { "hardware", "software", "software", "hardware", "hardware", "software" };
        foreach (var mode in modes)
        {
            var directory = Path.Combine(root, runs.Count.ToString());
            Directory.CreateDirectory(directory);
            var executable = Path.Combine(Environment.SystemDirectory, "cmd.exe");
            var config = new ConfigRoot
            {
                Settings = new AppSettings { Language = "en", Theme = "Dark", PosX = 40, PosY = 40 },
                Apps = [new() { Name = "Editor", Path = executable, WinPattern = Guid.NewGuid().ToString() },
                    new() { Name = "Scanner", Path = executable, WinPattern = Guid.NewGuid().ToString() },
                    new() { Name = "Browser", TargetKind = AppTargetKind.Website, WebsiteUrl = "https://example.com", BrowserProfileId = Guid.NewGuid().ToString("N") }]
            };
            await File.WriteAllTextAsync(Path.Combine(directory, "app_config.json"), JsonSerializer.Serialize(config, AppJsonOptions.Default));
            var host = comparison ? args[Array.IndexOf(args, "--compare-hosts") + (mode == "baseline" ? 1 : 2)] : Environment.ProcessPath!;
            var start = new ProcessStartInfo(host) { UseShellExecute = false, CreateNoWindow = true };
            start.ArgumentList.Add("--app-host");
            start.Environment["APPSWITCHER_UI_TEST_DATA"] = directory;
            start.Environment["APPSWITCHER_UI_TEST_RENDERING"] = comparison ? "software" : mode;
            var timer = Stopwatch.StartNew();
            using var process = Process.Start(start)!;
            try
            {
                while (!File.Exists(Path.Combine(directory, "rendered.json")))
                {
                    if (process.HasExited || timer.Elapsed.TotalSeconds > 20) throw new InvalidOperationException("Host did not render");
                    await Task.Delay(10);
                }
                var startup = timer.Elapsed.TotalMilliseconds;
                await Task.Delay(2000);
                process.Refresh();
                var cpuStart = process.TotalProcessorTime.TotalSeconds;
                var idle = Stopwatch.StartNew();
                var privateBytes = new List<long>();
                var workingBytes = new List<long>();
                for (var sample = 0; sample < 7; sample++)
                {
                    process.Refresh(); privateBytes.Add(process.PrivateMemorySize64); workingBytes.Add(process.WorkingSet64);
                    await Task.Delay(1000);
                }
                process.Refresh();
                var idleCpu = 100 * (process.TotalProcessorTime.TotalSeconds - cpuStart) / idle.Elapsed.TotalSeconds;
                var state = await ProcessChecks.Request(process.Id, "state");
                var modules = process.Modules.Cast<ProcessModule>().Select(module => module.ModuleName)
                    .Where(name => name.StartsWith("ig", StringComparison.OrdinalIgnoreCase) || name.StartsWith("d3d", StringComparison.OrdinalIgnoreCase)).ToArray();
                privateBytes.Sort(); workingBytes.Sort();
                JsonElement? workspace = null;
                if (args.Contains("--exercise") && exercised.Add(mode))
                {
                    var opening = Stopwatch.StartNew();
                    await ProcessChecks.Request(process.Id, "settings");
                    while ((await ProcessChecks.Request(process.Id, "state")).GetProperty("settings").GetInt64() == 0)
                    {
                        if (opening.Elapsed.TotalSeconds > 15) throw new InvalidOperationException("Editor did not open");
                        await Task.Delay(20);
                    }
                    workspace = await ProcessChecks.Request(process.Id, "scroll-workload");
                    await ProcessChecks.Request(process.Id, "close-settings");
                }
                runs.Add(new { mode, startupMilliseconds = startup, privateMiB = privateBytes[3] / 1048576.0,
                    workingSetMiB = workingBytes[3] / 1048576.0, cpuPercentOneCore = idleCpu,
                    threads = process.Threads.Count, managedMiB = state.GetProperty("managedBytes").GetInt64() / 1048576.0,
                    gcCommittedMiB = state.GetProperty("committedGcBytes").GetInt64() / 1048576.0, modules, workspace });
                Console.WriteLine(JsonSerializer.Serialize(runs[^1]));
                await ProcessChecks.Request(process.Id, "quit");
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            }
            finally { if (!process.HasExited) process.Kill(); }
        }
        await File.WriteAllTextAsync(Path.Combine(root, "results.json"), JsonSerializer.Serialize(runs, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Results: " + root);
        return 0;
    }

    internal static async Task<string> ScrollWorkloadAsync(App app)
    {
        var window = app.Windows.OfType<SettingsWindow>().Single();
        window.Width = 720; window.Height = 640;
        ((SettingsViewModel)window.DataContext).ActiveSection = "System";
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
        var scroll = Descendants<ScrollViewer>(window).First(view => view.IsVisible && view.ScrollableHeight > 0);
        using var process = Process.GetCurrentProcess();
        var cpuStart = process.TotalProcessorTime.TotalSeconds;
        var total = Stopwatch.StartNew();
        var costs = new List<double>();
        for (var index = 0; index < 120; index++)
        {
            var timer = Stopwatch.StartNew();
            scroll.ScrollToVerticalOffset(index % 2 == 0 ? 0 : scroll.ScrollableHeight);
            window.UpdateLayout();
            costs.Add(timer.Elapsed.TotalMilliseconds);
            await Task.Delay(16);
        }
        process.Refresh(); costs.Sort();
        return JsonSerializer.Serialize(new { iterations = costs.Count,
            layoutMedianMilliseconds = costs[60], layoutP95Milliseconds = costs[114],
            cpuPercentOfOneCore = 100 * (process.TotalProcessorTime.TotalSeconds - cpuStart) / total.Elapsed.TotalSeconds,
            privateMiB = process.PrivateMemorySize64 / 1048576.0, workingSetMiB = process.WorkingSet64 / 1048576.0 });
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
}
