using System.Collections.Concurrent;

namespace AppSwitcher.Services;

/// <summary>
/// Manages time-based scheduled app launches, restarts, and shutdowns.
/// Checks every minute if any app with a configured schedule should be acted upon.
/// </summary>
public class SchedulerService : IDisposable
{
    private readonly ILogger<SchedulerService> _logger;
    private readonly ProcessHelper _processHelper;
    private readonly IAppLifecycleService _appLifecycleService;
    private readonly NotificationService _notifications;
    private readonly ConcurrentDictionary<string, DateTime> _launchedToday = new();
    private readonly ConcurrentDictionary<string, DateTime> _restartedToday = new();
    private readonly ConcurrentDictionary<string, DateTime> _shutdownToday = new();
    private readonly ConcurrentDictionary<string, Task> _pendingOperations = new();
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private readonly MaintenanceService? _maintenance;
    private readonly AppReadinessService? _readiness;

    public SchedulerService(
        ILogger<SchedulerService> logger,
        ProcessHelper processHelper,
        IAppLifecycleService appLifecycleService,
        WatchdogService watchdogService,
        NotificationService notifications, MaintenanceService? maintenance = null, AppReadinessService? readiness = null)
    {
        _logger = logger;
        _processHelper = processHelper;
        _appLifecycleService = appLifecycleService;
        _notifications = notifications;
        _maintenance = maintenance;
        _readiness = readiness;
    }

    public void Start(Func<IReadOnlyList<AppItem>> getApps)
    {
        if (_cts != null)
            return;

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _loop = Task.Run(() => RunLoop(getApps, token));
        _logger.LogInformation("Scheduler service started");
    }

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

            var pending = _pendingOperations.Values.ToArray();
            if (pending.Length > 0)
                await Task.WhenAll(pending);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            cts.Dispose();
        }
    }

    private async Task RunLoop(Func<IReadOnlyList<AppItem>> getApps, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(ct))
                    break;

                await CheckSchedulesAsync(getApps, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (OperationCanceledException) { /* Maintenance cancelled this pass, not the scheduler. */ }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in scheduler loop, continuing...");
            }
        }
    }

    internal async Task CheckSchedulesAsync(Func<IReadOnlyList<AppItem>> getApps, CancellationToken ct)
    {
        if (_maintenance?.IsPaused == true) return;
        var shutdown = ct;
        using var automatic = CancellationTokenSource.CreateLinkedTokenSource(ct, _maintenance?.AutomaticToken ?? CancellationToken.None);
        ct = automatic.Token;
        var now = DateTime.Now;
        var todayKey = now.ToString("yyyyMMdd");
        var apps = getApps();

        foreach (var app in apps)
        {
            ct.ThrowIfCancellationRequested();

            await CheckScheduledLaunchAsync(app, now, todayKey, getApps, ct);
            // Queued operations outlive this pass and must link to the original shutdown token.
            QueueScheduledRestart(app, now, todayKey, getApps, shutdown);
            QueueScheduledShutdown(app, now, todayKey, getApps, shutdown);
        }

        PurgeStaleKeys(_launchedToday, todayKey);
        PurgeStaleKeys(_restartedToday, todayKey);
        PurgeStaleKeys(_shutdownToday, todayKey);
    }

    private async Task CheckScheduledLaunchAsync(
        AppItem app,
        DateTime now,
        string todayKey,
        Func<IReadOnlyList<AppItem>> getApps,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(app.ScheduleTime))
            return;
        if (!TryParseScheduledTime(app.ScheduleTime, out var scheduledTime))
            return;
        if (_maintenance?.Covers(new DateTimeOffset(now.Date + scheduledTime.ToTimeSpan())) == true) return;

        var key = $"launch_{NormalizeIdentity(app.Name)}_{todayKey}";
        if (_launchedToday.ContainsKey(key))
            return;
        if (TimeOnly.FromDateTime(now) < scheduledTime)
            return;
        if ((TimeOnly.FromDateTime(now) - scheduledTime).TotalMinutes > 5)
            return;
        if (_processHelper.IsAppRunning(app.ProcessTarget))
            return;

        try
        {
            await StartScheduledAppWithDependenciesAsync(app, getApps(), ct);
            _launchedToday[key] = now;
            _logger.LogInformation("Scheduled launch: {Name} at {Time}", app.Name, app.ScheduleTime);
            _notifications.Show(
                LocalizationService.T("Msg_ScheduledLaunch", app.Name, app.ScheduleTime),
                NotificationType.Info);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Scheduled launch cancelled for {Name}", app.Name);
            throw;
        }
        catch (Exception ex)
        {
            if (ex is AppLifecycleException lifecycleException)
                _notifications.Show(lifecycleException.Message, NotificationType.Error);

            _logger.LogError(ex, "Scheduled launch failed for {Name}", app.Name);
        }
    }

    private void QueueScheduledRestart(
        AppItem app,
        DateTime now,
        string todayKey,
        Func<IReadOnlyList<AppItem>> getApps,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(app.ScheduleRestartTime))
            return;
        if (!TryParseScheduledTime(app.ScheduleRestartTime, out var scheduledTime))
            return;
        if (_maintenance?.Covers(new DateTimeOffset(now.Date + scheduledTime.ToTimeSpan())) == true) return;

        var key = $"restart_{NormalizeIdentity(app.Name)}_{todayKey}";
        if (_restartedToday.ContainsKey(key))
            return;
        if (TimeOnly.FromDateTime(now) < scheduledTime)
            return;
        if ((TimeOnly.FromDateTime(now) - scheduledTime).TotalMinutes > 5)
            return;
        if (!_processHelper.IsAppRunning(app.ProcessTarget))
            return;

        QueueTrackedOperation(
            key,
            operationCt => ExecuteScheduledRestartAsync(app, key, now, getApps, operationCt),
            ct);
    }

    private void QueueScheduledShutdown(
        AppItem app,
        DateTime now,
        string todayKey,
        Func<IReadOnlyList<AppItem>> getApps,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(app.ScheduleShutdownTime))
            return;
        if (!TryParseScheduledTime(app.ScheduleShutdownTime, out var scheduledTime))
            return;
        if (_maintenance?.Covers(new DateTimeOffset(now.Date + scheduledTime.ToTimeSpan())) == true) return;

        var key = $"shutdown_{NormalizeIdentity(app.Name)}_{todayKey}";
        if (_shutdownToday.ContainsKey(key))
            return;
        if (TimeOnly.FromDateTime(now) < scheduledTime)
            return;
        if ((TimeOnly.FromDateTime(now) - scheduledTime).TotalMinutes > 5)
            return;
        if (!_processHelper.IsAppRunning(app.ProcessTarget))
            return;

        QueueTrackedOperation(
            key,
            operationCt => ExecuteScheduledShutdownAsync(app, key, now, getApps, operationCt),
            ct);
    }

    private void QueueTrackedOperation(string key, Func<CancellationToken, Task> operation, CancellationToken ct)
    {
        TrackOperationAsync(key, operation, ct);
    }

    internal Task TrackOperationAsync(string key, Func<CancellationToken, Task> operation) =>
        TrackOperationAsync(key, operation, _cts?.Token ?? CancellationToken.None);

    internal Task TrackOperationAsync(string key, Func<CancellationToken, Task> operation, CancellationToken ct)
    {
        var task = _pendingOperations.GetOrAdd(key, _ => RunTrackedOperationAsync(key, operation, ct));
        return task;
    }

    private async Task RunTrackedOperationAsync(string key, Func<CancellationToken, Task> operation, CancellationToken ct)
    {
        try
        {
            await operation(ct);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Cancelled scheduled operation {Key}", key);
        }
        catch (Exception ex)
        {
            if (ex is AppLifecycleException lifecycleException)
                _notifications.Show(lifecycleException.Message, NotificationType.Error);

            _logger.LogError(ex, "Scheduled operation failed: {Key}", key);
        }
        finally
        {
            _pendingOperations.TryRemove(key, out _);
        }
    }

    private async Task ExecuteScheduledRestartAsync(
        AppItem originalApp,
        string key,
        DateTime triggeredAt,
        Func<IReadOnlyList<AppItem>> getApps,
        CancellationToken ct)
    {
        if (_maintenance?.IsPaused == true) return;
        using var automatic = CancellationTokenSource.CreateLinkedTokenSource(ct, _maintenance?.AutomaticToken ?? CancellationToken.None);
        ct = automatic.Token;
        var currentApp = ResolveScheduledApp(getApps(), originalApp.Name, originalApp.ScheduleRestartTime, app => app.ScheduleRestartTime);
        if (currentApp == null)
            return;

        await _appLifecycleService.StopAsync(currentApp, AppStopMode.Restart, ct);

        currentApp = ResolveScheduledApp(getApps(), originalApp.Name, originalApp.ScheduleRestartTime, app => app.ScheduleRestartTime);
        if (currentApp == null)
            return;

        await _appLifecycleService.LaunchAsync(currentApp, ct);
        _restartedToday[key] = triggeredAt;
        _logger.LogInformation("Scheduled restart: {Name} at {Time}", currentApp.Name, currentApp.ScheduleRestartTime);
        _notifications.Show(
            LocalizationService.T("Msg_ScheduledRestart", currentApp.Name, currentApp.ScheduleRestartTime),
            NotificationType.Info);
    }

    private async Task ExecuteScheduledShutdownAsync(
        AppItem originalApp,
        string key,
        DateTime triggeredAt,
        Func<IReadOnlyList<AppItem>> getApps,
        CancellationToken ct)
    {
        if (_maintenance?.IsPaused == true) return;
        using var automatic = CancellationTokenSource.CreateLinkedTokenSource(ct, _maintenance?.AutomaticToken ?? CancellationToken.None);
        ct = automatic.Token;
        var currentApp = ResolveScheduledApp(getApps(), originalApp.Name, originalApp.ScheduleShutdownTime, app => app.ScheduleShutdownTime);
        if (currentApp == null)
            return;

        await _appLifecycleService.StopAsync(currentApp, AppStopMode.Shutdown, ct);

        _shutdownToday[key] = triggeredAt;
        _logger.LogInformation("Scheduled shutdown: {Name} at {Time}", currentApp.Name, currentApp.ScheduleShutdownTime);
        _notifications.Show(
            LocalizationService.T("Msg_ScheduledShutdown", currentApp.Name, currentApp.ScheduleShutdownTime),
            NotificationType.Info);
    }

    internal static AppItem? ResolveScheduledApp(
        IEnumerable<AppItem> apps,
        string appName,
        string expectedScheduleValue,
        Func<AppItem, string> scheduleSelector)
    {
        var currentApp = AppDependencyPlanner.FindByName(apps, appName);
        if (currentApp == null)
            return null;

        var currentSchedule = scheduleSelector(currentApp)?.Trim() ?? string.Empty;
        return string.Equals(currentSchedule, expectedScheduleValue?.Trim(), StringComparison.Ordinal)
            ? currentApp
            : null;
    }

    private static void PurgeStaleKeys(ConcurrentDictionary<string, DateTime> dict, string todayKey)
    {
        var staleKeys = dict.Keys.Where(k => !k.EndsWith(todayKey, StringComparison.Ordinal)).ToList();
        foreach (var stale in staleKeys)
            dict.TryRemove(stale, out _);
    }

    private static string NormalizeIdentity(string name) =>
        string.IsNullOrWhiteSpace(name) ? "_" : name.Trim().ToLowerInvariant();

    private async Task StartScheduledAppWithDependenciesAsync(
        AppItem requestedApp,
        IReadOnlyList<AppItem> allApps,
        CancellationToken ct)
    {
        var launchedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var plannedApp in AppDependencyPlanner.BuildLaunchPlan([requestedApp], allApps))
        {
            ct.ThrowIfCancellationRequested();

            var dependency = AppDependencyPlanner.FindByName(allApps, plannedApp.DependsOn);
            if (dependency != null && !_processHelper.IsAppRunning(dependency.ProcessTarget))
            {
                throw new InvalidOperationException(
                    $"Dependency '{dependency.Name}' is not running for scheduled launch of '{plannedApp.Name}'.");
            }

            var key = plannedApp.ProcessTarget.Key;
            if (launchedKeys.Contains(key) || _processHelper.IsAppRunning(plannedApp.ProcessTarget))
            {
                if (_readiness != null) await _readiness.WaitAsync(plannedApp, ct);
                launchedKeys.Add(key);
                continue;
            }

            if (plannedApp.LaunchDelay > 0)
                await Task.Delay(plannedApp.LaunchDelay, ct);

            await _appLifecycleService.LaunchAsync(plannedApp, ct);
            launchedKeys.Add(key);
        }
    }

    internal static bool TryParseScheduledTime(string? value, out TimeOnly scheduledTime) =>
        TimeOnly.TryParseExact(
            value?.Trim(),
            "HH:mm",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out scheduledTime);

    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
    }
}
