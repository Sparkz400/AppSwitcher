using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using AppSwitcher;
using AppSwitcher.Models;
using AppSwitcher.Services;
using AppSwitcher.Tests;
using AppSwitcher.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;

internal static partial class Program
{
    private static async Task CheckEditorPerformance()
    {
        var reports = new List<object>();
        foreach (var count in new[] { 10, 100, 500 })
        {
            using var fixture = await MainViewModelAuditTests.CreateHarnessAsync(new ConfigRoot
            { Apps = Enumerable.Range(0, count).Select(i => new AppItem { Name = $"Synthetic {i}", Path = $"synthetic-{i}.exe" }).ToList() });
            fixture.ViewModel.CurrentAccessLevel = AccessLevel.Admin;
            using var session = new SessionTimeoutService(NullLogger<SessionTimeoutService>.Instance);
            using var editor = new SettingsViewModel(fixture.ViewModel, fixture.ConfigService, new SecurityService(), fixture.NotificationService,
                new ScreenLayoutService(NullLogger<ScreenLayoutService>.Instance, fixture.ProcessHelper),
                new CrashHistoryService(NullLogger<CrashHistoryService>.Instance), session);
            var window = new SettingsWindow { DataContext = editor };
            var open = Stopwatch.StartNew();
            window.Show(); await Render(); open.Stop();
            var times = new List<double>();
            var allocations = GC.GetTotalAllocatedBytes(true);
            for (var i = 0; i < 20; i++)
            {
                var timer = Stopwatch.StartNew();
                editor.Apps[0].Name = $"Typed {i}";
                editor.Settings.ButtonSize = 80 + i;
                editor.SettingsSearch = i % 2 == 0 ? "theme" : "";
                await Render();
                times.Add(timer.Elapsed.TotalMilliseconds);
            }
            allocations = GC.GetTotalAllocatedBytes(true) - allocations;
            times.Sort();
            await editor.DiscardChangesAsync();
            window.Close(); await Render();
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            using var process = Process.GetCurrentProcess();
            reports.Add(new { entries = count, openMilliseconds = open.Elapsed.TotalMilliseconds,
                inputAndRenderMedianMs = times[10], inputAndRenderP95Ms = times[18], allocatedBytes = allocations,
                privateBytes = process.PrivateMemorySize64, handles = process.HandleCount });
        }
        var json = JsonSerializer.Serialize(new { workload = "20 name + slider + search changes with actual WPF rendering; includes harness render delay; synthetic config", reports }, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(Output, "editor-performance.json"), json);
        Console.WriteLine(json);
    }
}
