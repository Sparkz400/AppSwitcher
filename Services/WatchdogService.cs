using System.Collections.Concurrent;

namespace AppSwitcher.Services;

public readonly record struct WatchdogStatus(
    bool IsCircuitOpen,
    bool RequiresAcknowledgement,
    DateTime? NextRestartAt,
    int AttemptCount)
{
    public static WatchdogStatus None => new(false, false, null, 0);
}

public class WatchdogService : IDisposable
{
    private static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan ExpectedStopWindow = TimeSpan.FromSeconds(15);
    private readonly ILogger<WatchdogService> _logger;
    private readonly ProcessHelper _processHelper;
    private readonly IAppLifecycleService _appLifecycleService;
    private readonly NotificationService _notifications;
    private readonly CrashHistoryService _crashHistory;
    private readonly IWatchdogCircuitStore _circuitStore;
    private readonly TimeProvider _timeProvider;
    private readonly MaintenanceService? _maintenance;
    private readonly ConcurrentDictionary<string, bool> _pausedStops = new();
    private readonly ConcurrentDictionary<string, RestartState> _restartStates = new();
    private readonly ConcurrentDictionary<string, CrashStats> _crashStats = new();
    private readonly ConcurrentDictionary<string, DateTime> _expectedStops = new();

    /// <summary>
    /// Raised when an app exhausts all restart attempts. Subscribers can use this for persistent alerts.
    /// </summary>
    public event Action<AppItem>? MaxRestartsExhausted;

    public WatchdogService(
        ILogger<WatchdogService> logger,
        ProcessHelper processHelper,
        IAppLifecycleService appLifecycleService,
        NotificationService notifications,
        CrashHistoryService crashHistory)
        : this(logger, processHelper, appLifecycleService, notifications, crashHistory, new MemoryWatchdogCircuitStore(), TimeProvider.System)
    {
    }

    public WatchdogService(
        ILogger<WatchdogService> logger,
        ProcessHelper processHelper,
        IAppLifecycleService appLifecycleService,
        NotificationService notifications,
        CrashHistoryService crashHistory,
        IWatchdogCircuitStore circuitStore,
        TimeProvider timeProvider, MaintenanceService? maintenance = null)
    {
        _logger = logger;
        _processHelper = processHelper;
        _appLifecycleService = appLifecycleService;
        _notifications = notifications;
        _crashHistory = crashHistory;
        _circuitStore = circuitStore;
        _timeProvider = timeProvider;
        _maintenance = maintenance;

        LoadPersistedCircuits();
    }

    public void ExpectStop(AppProcessTarget target)
    {
        if (!string.IsNullOrWhiteSpace(target.Path))
            _expectedStops[target.Key] = UtcNow.Add(ExpectedStopWindow);
    }

    public bool IsStopExpected(AppProcessTarget target)
    {
        if (string.IsNullOrWhiteSpace(target.Path))
            return false;

        if (!_expectedStops.TryGetValue(target.Key, out var expiresAt))
            return false;

        if (expiresAt < UtcNow)
        {
            _expectedStops.TryRemove(target.Key, out _);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Called from the process monitor tick. Detects crashes and auto-restarts with exponential backoff.
    /// </summary>
    public async Task CheckAndRestartAsync(AppItem app, bool wasRunning, bool isRunningNow, CancellationToken ct = default)
    {
        if (app.IsWebsite && _processHelper.GetWebsitePresence(app.ProcessTarget) is WebsitePresence.Unknown or WebsitePresence.Conflict)
            return;
        if (!app.WatchdogEnabled || string.IsNullOrWhiteSpace(app.ProcessTarget.Path))
            return;

        var target = app.ProcessTarget;
        var key = target.Key;
        if (wasRunning && !isRunningNow && _processHelper.GetLastExitKind(target) != ProcessExitKind.Unexpected)
        {
            // A normal user close (or an unconfirmed disappearance) is not a crash.
            ConsumeExpectedStop(target);
            _pausedStops.TryRemove(key, out _);
            if (_restartStates.TryGetValue(key, out var stopped))
                lock (stopped) { stopped.NextRestartAtUtc = null; stopped.PendingAttemptNumber = 0; }
            return;
        }
        if (_maintenance?.IsPaused == true)
        {
            if (wasRunning && !isRunningNow && !IsStopExpected(target)) _pausedStops[key] = true;
            if (isRunningNow || IsStopExpected(target)) _pausedStops.TryRemove(key, out _);
            return;
        }
        wasRunning |= _pausedStops.TryRemove(key, out _);
        using var automatic = CancellationTokenSource.CreateLinkedTokenSource(ct, _maintenance?.AutomaticToken ?? CancellationToken.None);
        ct = automatic.Token;
        var now = UtcNow;

        if (isRunningNow)
        {
            if (_restartStates.TryGetValue(key, out var runningState))
            {
                lock (runningState)
                {
                    runningState.NextRestartAtUtc = null;
                    runningState.PendingAttemptNumber = 0;
                }
            }

            return;
        }

        if (wasRunning && !isRunningNow)
        {
            if (ConsumeExpectedStop(target))
                return;

            _crashHistory.RecordCrash(app.Name, app.Path);
            IncrementCrashCount(target, now);

            var state = GetOrCreateState(target);
            var openedCircuit = false;
            var scheduledRestart = false;
            var attemptNumber = 0;
            var backoffSeconds = 0;

            lock (state)
            {
                state.Target = target;
                PruneExpiredAttempts(state, now);
                state.NextRestartAtUtc = null;
                state.PendingAttemptNumber = 0;

                if (!state.CircuitOpen)
                {
                    if (state.RestartAttemptTimestampsUtc.Count >= app.MaxRestarts)
                    {
                        state.CircuitOpen = true;
                        state.CircuitOpenAttemptCount = state.RestartAttemptTimestampsUtc.Count;
                        state.CircuitOpenedAtUtc = now;
                        openedCircuit = true;
                    }
                    else
                    {
                        attemptNumber = state.RestartAttemptTimestampsUtc.Count + 1;
                        backoffSeconds = CalculateBackoffSeconds(attemptNumber);
                        state.PendingAttemptNumber = attemptNumber;
                        state.NextRestartAtUtc = now.AddSeconds(backoffSeconds);
                        scheduledRestart = true;
                    }
                }
            }

            if (scheduledRestart)
            {
                _logger.LogWarning(
                    "App {Name} crashed, scheduling restart in {Backoff}s ({Count}/{Max})",
                    app.Name,
                    backoffSeconds,
                    attemptNumber,
                    app.MaxRestarts);
                _notifications.Show(
                    LocalizationService.T("Msg_AppCrashRestart", app.Name, attemptNumber, app.MaxRestarts),
                    NotificationType.Warning);
            }
            else if (openedCircuit)
            {
                _crashHistory.RecordEvent(app.Name, app.Path, "Circuit opened");
                PersistOpenCircuits();
                _logger.LogError("App {Name} exceeded max restarts ({Max})", app.Name, app.MaxRestarts);
                _notifications.Show(
                    LocalizationService.T("Msg_MaxRestartsExhausted", app.Name, app.MaxRestarts),
                    NotificationType.Error);
                MaxRestartsExhausted?.Invoke(app);
            }
        }

        if (!_restartStates.TryGetValue(key, out var pending))
            return;

        var shouldRestart = false;
        var pendingAttemptNumber = 0;

        lock (pending)
        {
            pending.Target = target;
            PruneExpiredAttempts(pending, now);

            if (pending.CircuitOpen || pending.NextRestartAtUtc == null || now < pending.NextRestartAtUtc.Value)
                return;

            pendingAttemptNumber = pending.PendingAttemptNumber > 0
                ? pending.PendingAttemptNumber
                : pending.RestartAttemptTimestampsUtc.Count + 1;

            pending.NextRestartAtUtc = null;
            pending.PendingAttemptNumber = 0;
            pending.RestartAttemptTimestampsUtc.Add(now);
            shouldRestart = true;
        }

        if (!shouldRestart)
            return;

        try
        {
            ct.ThrowIfCancellationRequested();
            IncrementRestartCount(target, now);
            _crashHistory.RecordRestart(app.Name, app.Path, pendingAttemptNumber);
            await _appLifecycleService.LaunchAsync(app, ct);
            _logger.LogInformation("Watchdog restarted {Name} (attempt {Count})", app.Name, pendingAttemptNumber);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Watchdog restart cancelled for {Name}", app.Name);
        }
        catch (Exception ex)
        {
            if (ex is AppLifecycleException lifecycleException)
                _notifications.Show(lifecycleException.Message, NotificationType.Error);

            _logger.LogError(ex, "Failed to restart {Name}", app.Name);
        }
    }

    public WatchdogStatus GetStatus(AppProcessTarget target)
    {
        if (string.IsNullOrWhiteSpace(target.Path))
            return WatchdogStatus.None;

        if (!_restartStates.TryGetValue(target.Key, out var state))
            return WatchdogStatus.None;

        lock (state)
        {
            PruneExpiredAttempts(state, UtcNow);

            var attemptCount = state.CircuitOpen
                ? state.CircuitOpenAttemptCount
                : state.PendingAttemptNumber > 0
                    ? state.PendingAttemptNumber
                    : state.RestartAttemptTimestampsUtc.Count;

            return new WatchdogStatus(
                state.CircuitOpen,
                state.CircuitOpen,
                state.NextRestartAtUtc,
                attemptCount);
        }
    }

    public void AcknowledgeCircuit(AppProcessTarget target)
    {
        ResetCount(target);
    }

    /// <summary>
    /// Gets crash statistics for an app.
    /// </summary>
    public (int CrashCountToday, int TotalRestarts) GetCrashStats(AppProcessTarget target)
    {
        if (string.IsNullOrWhiteSpace(target.Path))
            return (0, 0);

        if (_crashStats.TryGetValue(target.Key, out var stats))
        {
            lock (stats)
            {
                ResetDailyStatsIfNeeded(stats, UtcNow);
                return (stats.CrashCountToday, stats.TotalRestarts);
            }
        }

        return (0, 0);
    }

    public void ResetCount(AppProcessTarget target)
    {
        if (string.IsNullOrWhiteSpace(target.Path))
            return;

        _restartStates.TryRemove(target.Key, out _);
        _expectedStops.TryRemove(target.Key, out _);
        PersistOpenCircuits();
    }

    public void Dispose()
    {
    }

    private DateTime UtcNow => _timeProvider.GetUtcNow().UtcDateTime;

    private RestartState GetOrCreateState(AppProcessTarget target) =>
        _restartStates.GetOrAdd(target.Key, _ => new RestartState { Target = target });

    private void IncrementCrashCount(AppProcessTarget target, DateTime now)
    {
        var stats = _crashStats.GetOrAdd(target.Key, _ => new CrashStats());
        lock (stats)
        {
            ResetDailyStatsIfNeeded(stats, now);
            stats.CrashCountToday++;
        }
    }

    private void IncrementRestartCount(AppProcessTarget target, DateTime now)
    {
        var stats = _crashStats.GetOrAdd(target.Key, _ => new CrashStats());
        lock (stats)
        {
            ResetDailyStatsIfNeeded(stats, now);
            stats.TotalRestarts++;
        }
    }

    private static void ResetDailyStatsIfNeeded(CrashStats stats, DateTime now)
    {
        var today = now.Date;
        if (stats.LastResetDate == today)
            return;

        stats.CrashCountToday = 0;
        stats.LastResetDate = today;
    }

    private static void PruneExpiredAttempts(RestartState state, DateTime now)
    {
        if (state.RestartAttemptTimestampsUtc.Count == 0)
            return;

        var cutoff = now - FailureWindow;
        state.RestartAttemptTimestampsUtc.RemoveAll(timestamp => timestamp < cutoff);
    }

    private static int CalculateBackoffSeconds(int attemptNumber)
    {
        if (attemptNumber <= 1)
            return 0;

        return Math.Min(60, (int)Math.Pow(2, attemptNumber - 1));
    }

    private bool ConsumeExpectedStop(AppProcessTarget target)
    {
        if (!IsStopExpected(target))
            return false;

        _expectedStops.TryRemove(target.Key, out _);
        return true;
    }

    private void LoadPersistedCircuits()
    {
        foreach (var circuit in _circuitStore.LoadOpenCircuits())
        {
            if (string.IsNullOrWhiteSpace(circuit.Path))
                continue;

            var target = new AppProcessTarget(circuit.Path, circuit.WinPattern, circuit.WebsiteId, circuit.BrowserProfileId, circuit.Browser);
            _restartStates[target.Key] = new RestartState
            {
                Target = target,
                CircuitOpen = true,
                CircuitOpenAttemptCount = Math.Max(0, circuit.AttemptCount),
                CircuitOpenedAtUtc = circuit.OpenedAtUtc
            };
        }
    }

    private void PersistOpenCircuits()
    {
        var openCircuits = new List<WatchdogCircuitSnapshot>();

        foreach (var pair in _restartStates)
        {
            var state = pair.Value;
            lock (state)
            {
                if (!state.CircuitOpen || string.IsNullOrWhiteSpace(state.Target.Path))
                    continue;

                openCircuits.Add(new WatchdogCircuitSnapshot(
                    state.Target.Path,
                    state.Target.WinPattern,
                    state.CircuitOpenAttemptCount,
                    state.CircuitOpenedAtUtc ?? UtcNow,
                    state.Target.WebsiteId, state.Target.BrowserProfileId, state.Target.Browser));
            }
        }

        _circuitStore.SaveOpenCircuits(openCircuits);
    }

    private class RestartState
    {
        public AppProcessTarget Target;
        public List<DateTime> RestartAttemptTimestampsUtc { get; } = [];
        public DateTime? NextRestartAtUtc;
        public int PendingAttemptNumber;
        public bool CircuitOpen;
        public int CircuitOpenAttemptCount;
        public DateTime? CircuitOpenedAtUtc;
    }

    private class CrashStats
    {
        public int CrashCountToday;
        public int TotalRestarts;
        public DateTime LastResetDate = DateTime.UtcNow.Date;
    }
}
