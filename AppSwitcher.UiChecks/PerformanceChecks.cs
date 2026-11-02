using System.Diagnostics;
using System.IO;
using System.Text.Json;
using AppSwitcher.Services;
using Microsoft.Extensions.Logging.Abstractions;

internal static class PerformanceChecks
{
    // An isolated, repeatable workload: no production config, managed apps or browser profiles are touched.
    internal static async Task RunAsync()
    {
        using var current = Process.GetCurrentProcess();
        using var helper = new ProcessHelper(NullLogger<ProcessHelper>.Instance);
        var path = current.MainModule!.FileName!;
        var target = new AppSwitcher.Models.AppProcessTarget(path, "AppSwitcher benchmark unmatched title");
        const int queries = 50;
        object Measure(Action action)
        {
            action(); // warm the runtime
            var elapsed = new List<double>();
            var cpuBefore = current.TotalProcessorTime;
            var allocatedBefore = GC.GetTotalAllocatedBytes(true);
            for (int trial = 0; trial < 5; trial++)
            {
                var timer = Stopwatch.StartNew(); action(); elapsed.Add(timer.Elapsed.TotalMilliseconds);
            }
            elapsed.Sort();
            return new { medianMilliseconds = elapsed[2], cpuMilliseconds = (current.TotalProcessorTime - cpuBefore).TotalMilliseconds,
                allocatedBytes = GC.GetTotalAllocatedBytes(true) - allocatedBefore, trials = 5, queriesPerTrial = queries };
        }
        var old = Measure(() =>
        {
            for (int n = 0; n < queries; n++)
                foreach (var process in Process.GetProcesses())
                    using (process) { try { _ = process.MainWindowTitle.Contains(target.WinPattern!); } catch { } }
        });
        var optimized = Measure(() =>
        {
            using var observation = helper.ObserveProcesses();
            for (int n = 0; n < queries; n++)
                foreach (var process in helper.GetMatchingProcesses(target)) process.Dispose();
        });
        var responsiveness = new ResponsivenessMonitor();
        var batch = Stopwatch.StartNew();
        for (int n = 0; n < 12; n++) responsiveness.Queue(n.ToString(), TimeSpan.Zero,
            () => { Thread.Sleep(100); return false; }, _ => { }, default);
        var enqueueMs = batch.Elapsed.TotalMilliseconds;
        await responsiveness.DrainAsync();
        var report = new { timestampUtc = DateTimeOffset.UtcNow, runtime = Environment.Version.ToString(), processors = Environment.ProcessorCount,
            workload = "50 native window-pattern lookups; five warmed trials. Baseline enumerates all processes as before this change.",
            baseline = old, optimized,
            responsiveness = new { probes = 12, simulatedProbeMilliseconds = 100, enqueueMilliseconds = enqueueMs,
                completionMilliseconds = batch.Elapsed.TotalMilliseconds, maximumConcurrency = 4 },
            limitation = "Synthetic workload on the development machine, not an idle CPU/RAM or startup benchmark on an industrial panel. No Rust comparison." };
        var output = Path.GetFullPath("artifacts/performance.json");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(output, json);
        Console.WriteLine(json);
    }
}
