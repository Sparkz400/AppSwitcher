using System.Collections.Concurrent;

namespace AppSwitcher.Services;

public class ResourceMonitorService : IDisposable
{
    private readonly ILogger<ResourceMonitorService> _logger;
    private readonly ProcessHelper _processHelper;
    private readonly NotificationService _notifications;
    private readonly ConcurrentDictionary<string, CpuSnapshot> _cpuSnapshots = new();
    private readonly ConcurrentDictionary<string, DateTime> _lastThresholdAlert = new();
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public ResourceMonitorService(
        ILogger<ResourceMonitorService> logger,
        ProcessHelper processHelper,
        NotificationService notifications)
    {
        _logger = logger;
        _processHelper = processHelper;
        _notifications = notifications;
    }

    public void Start(Func<IReadOnlyList<AppItem>> getApps, Func<bool>? isVisible = null)
    {
        if (_cts != null) return;

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _loop = Task.Run(() => RunLoop(getApps, isVisible, token));
        _logger.LogInformation("Resource monitor service started");
    }

    private async Task RunLoop(Func<IReadOnlyList<AppItem>> getApps, Func<bool>? isVisible, CancellationToken ct)
    {
        var lastCollection = DateTime.MinValue;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(ct))
                    break;

                var apps = getApps();
                if (isVisible?.Invoke() == false && !apps.Any(a => a.MemoryThresholdMB > 0)
                    && DateTime.UtcNow - lastCollection < TimeSpan.FromSeconds(30)) continue;
                await Task.Run(() => CollectMetrics(apps), ct);
                lastCollection = DateTime.UtcNow;
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in resource monitor loop, continuing...");
            }
        }
    }

    internal void CollectMetrics(IReadOnlyList<AppItem> apps)
    {
        using var observation = _processHelper.ObserveProcesses();
        var processorCount = Environment.ProcessorCount;
        var activeSnapshotKeys = new HashSet<string>(StringComparer.Ordinal);
        var metricsThisPass = new Dictionary<string, (double Memory, double Cpu)>();

        foreach (var app in apps)
        {
            if (app.IsWebsite || !app.IsRunning || string.IsNullOrWhiteSpace(app.Path))
            {
                ResetMetrics(app);
                continue;
            }

            try
            {
                var processes = _processHelper.GetMatchingProcesses(app.ProcessTarget);

                if (processes.Count == 0)
                {
                    ResetMetrics(app);
                    continue;
                }

                double totalMemoryMB = 0;
                double totalCpuPercent = 0;

                foreach (var proc in processes)
                {
                    using (proc)
                    {
                        try
                        {
                            var key = BuildSnapshotKey(proc);
                            activeSnapshotKeys.Add(key);
                            if (metricsThisPass.TryGetValue(key, out var cached))
                            {
                                totalMemoryMB += cached.Memory;
                                totalCpuPercent += cached.Cpu;
                                continue;
                            }
                            var memory = proc.WorkingSet64 / (1024.0 * 1024.0);
                            double cpu = 0;

                            var now = DateTime.UtcNow;
                            var currentCpuTime = proc.TotalProcessorTime;

                            if (_cpuSnapshots.TryGetValue(key, out var snapshot))
                            {
                                var elapsed = now - snapshot.Timestamp;
                                if (elapsed.TotalMilliseconds > 0)
                                {
                                    var cpuUsed = (currentCpuTime - snapshot.CpuTime).TotalMilliseconds;
                                    cpu = Math.Max(0, (cpuUsed / elapsed.TotalMilliseconds / processorCount) * 100);
                                }
                            }

                            _cpuSnapshots[key] = new CpuSnapshot(now, currentCpuTime);
                            metricsThisPass[key] = (memory, cpu);
                            totalMemoryMB += memory;
                            totalCpuPercent += cpu;
                        }
                        catch (InvalidOperationException) { } // Process exited
                        catch (System.ComponentModel.Win32Exception) { } // Access denied
                    }
                }

                var memMB = Math.Round(totalMemoryMB, 1);
                var cpuPct = Math.Round(Math.Min(totalCpuPercent, 100), 1);

                UiDispatcher.TryBeginInvoke(() =>
                {
                    app.MemoryMB = memMB;
                    app.CpuPercent = cpuPct;
                });

                // Memory threshold alert (throttled to once per 5 minutes per app)
                if (app.MemoryThresholdMB > 0 && memMB > app.MemoryThresholdMB)
                {
                    var alertKey = app.Path.ToLowerInvariant();
                    var now = DateTime.UtcNow;
                    if (!_lastThresholdAlert.TryGetValue(alertKey, out var lastAlert)
                        || (now - lastAlert).TotalMinutes >= 5)
                    {
                        _lastThresholdAlert[alertKey] = now;
                        _notifications.Show(
                            LocalizationService.T("Msg_MemoryThresholdExceeded", app.Name, (int)memMB, app.MemoryThresholdMB),
                            NotificationType.Warning);
                        _logger.LogWarning("Memory threshold exceeded for {Name}: {MB}MB > {Threshold}MB",
                            app.Name, (int)memMB, app.MemoryThresholdMB);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error collecting metrics for {Name}", app.Name);
            }
        }

        CleanupSnapshots(activeSnapshotKeys);
    }

    public void Dispose()
    {
        StopSynchronously();
    }

    private static string BuildSnapshotKey(Process process)
    {
        try
        {
            return $"{process.Id}:{process.StartTime.ToUniversalTime().Ticks}";
        }
        catch
        {
            return $"{process.Id}";
        }
    }

    private void CleanupSnapshots(HashSet<string> activeSnapshotKeys)
    {
        foreach (var key in _cpuSnapshots.Keys.Where(key => !activeSnapshotKeys.Contains(key)).ToList())
            _cpuSnapshots.TryRemove(key, out _);
    }

    private static void ResetMetrics(AppItem app)
    {
        if (app.CpuPercent != 0 || app.MemoryMB != 0)
        {
            UiDispatcher.TryBeginInvoke(() =>
            {
                app.CpuPercent = 0;
                app.MemoryMB = 0;
            });
        }
    }

    private record CpuSnapshot(DateTime Timestamp, TimeSpan CpuTime);

    public async Task StopAsync()
    {
        var cts = _cts;
        var loop = _loop;

        _cts = null;
        _loop = null;

        if (cts == null)
            return;

        cts.Cancel();
        try
        {
            if (loop != null)
                await loop;
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            cts.Dispose();
        }
    }

    private void StopSynchronously() => StopAsync().GetAwaiter().GetResult();
}
