using Microsoft.Extensions.Logging.Abstractions;

namespace AppSwitcher.Tests;

public class WatchdogServiceTests
{
    [Theory]
    [InlineData(ProcessExitKind.Normal)]
    [InlineData(ProcessExitKind.Unknown)]
    public async Task ManualOrUnconfirmedExitDoesNotCountOrRestart(ProcessExitKind kind)
    {
        var helper = new TestProcessHelper { ExitKind = kind };
        using var service = CreateService(processHelper: helper);
        var app = CreateApp();
        await service.CheckAndRestartAsync(app, true, false);
        await service.CheckAndRestartAsync(app, false, false);
        Assert.Equal(0, service.GetCrashStats(app.ProcessTarget).CrashCountToday);
        Assert.Null(service.GetStatus(app.ProcessTarget).NextRestartAt);
        Assert.Equal(0, helper.StartAttempts);
    }
    [Fact]
    public async Task Maintenance_Defers_Crash_Recovery_And_Resumes_It_After_Expiry()
    {
        var directory = Path.Combine(Path.GetTempPath(), "AppSwitcher.Tests." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
            using var maintenance = new MaintenanceService(Path.Combine(directory, "maintenance.json"), time);
            var helper = new TestProcessHelper();
            using var service = new WatchdogService(NullLogger<WatchdogService>.Instance, helper,
                new TestAppLifecycleService(helper), new NotificationService(), new TestCrashHistoryService(),
                new MemoryWatchdogCircuitStore(), time, maintenance);
            var app = CreateApp();
            maintenance.Pause(TimeSpan.FromMinutes(30));
            await service.CheckAndRestartAsync(app, true, false);
            Assert.Equal(0, helper.StartAttempts);
            Assert.Equal(0, service.GetCrashStats(app.ProcessTarget).CrashCountToday);
            time.Advance(TimeSpan.FromMinutes(31));
            await service.CheckAndRestartAsync(app, false, false);
            Assert.Equal(1, helper.StartAttempts);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Website_Unknown_State_Does_Not_Count_As_Crash_Or_Restart()
    {
        var helper = new TestProcessHelper();
        var service = CreateService(processHelper: helper);
        var app = CreateApp(); app.TargetKind = AppTargetKind.Website;
        await service.CheckAndRestartAsync(app, wasRunning: true, isRunningNow: false);
        Assert.Equal(0, helper.StartAttempts);
        Assert.Equal(0, service.GetCrashStats(app.ProcessTarget).CrashCountToday);
    }

    [Fact]
    public async Task CheckAndRestartAsync_Does_Not_Count_Expected_Stop_As_Crash()
    {
        var service = CreateService();
        var app = CreateApp();

        service.ExpectStop(app.ProcessTarget);

        await service.CheckAndRestartAsync(app, wasRunning: true, isRunningNow: false);
        var stats = service.GetCrashStats(app.ProcessTarget);

        Assert.False(service.IsStopExpected(app.ProcessTarget));
        Assert.Equal(0, stats.CrashCountToday);
        Assert.Equal(0, stats.TotalRestarts);
    }

    [Fact]
    public async Task CheckAndRestartAsync_Uses_Exponential_Backoff_And_Caps_At_60_Seconds()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var processHelper = new TestProcessHelper();
        var service = CreateService(timeProvider: time, processHelper: processHelper);
        var app = CreateApp(maxRestarts: 10);

        await service.CheckAndRestartAsync(app, wasRunning: true, isRunningNow: false);
        Assert.Equal(1, processHelper.StartAttempts);
        Assert.Equal(1, service.GetStatus(app.ProcessTarget).AttemptCount);

        await AssertPendingRestartAsync(service, app, time, TimeSpan.FromSeconds(2), expectedAttempt: 2);
        await AssertPendingRestartAsync(service, app, time, TimeSpan.FromSeconds(4), expectedAttempt: 3);
        await AssertPendingRestartAsync(service, app, time, TimeSpan.FromSeconds(8), expectedAttempt: 4);
        await AssertPendingRestartAsync(service, app, time, TimeSpan.FromSeconds(16), expectedAttempt: 5);
        await AssertPendingRestartAsync(service, app, time, TimeSpan.FromSeconds(32), expectedAttempt: 6);
        await AssertPendingRestartAsync(service, app, time, TimeSpan.FromSeconds(60), expectedAttempt: 7);
        await AssertPendingRestartAsync(service, app, time, TimeSpan.FromSeconds(60), expectedAttempt: 8);
    }

    [Fact]
    public async Task CheckAndRestartAsync_Opens_Circuit_After_Max_Restarts_And_Suppresses_Further_Restarts()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var processHelper = new TestProcessHelper();
        var crashHistory = new TestCrashHistoryService();
        var store = new MemoryWatchdogCircuitStore();
        var service = CreateService(time, processHelper, crashHistory, store);
        var app = CreateApp(maxRestarts: 3);

        await service.CheckAndRestartAsync(app, wasRunning: true, isRunningNow: false);
        await CrashAndExecutePendingRestartAsync(service, app, time, TimeSpan.FromSeconds(2));
        await CrashAndExecutePendingRestartAsync(service, app, time, TimeSpan.FromSeconds(4));

        await service.CheckAndRestartAsync(app, wasRunning: true, isRunningNow: false);
        var status = service.GetStatus(app.ProcessTarget);

        Assert.True(status.IsCircuitOpen);
        Assert.True(status.RequiresAcknowledgement);
        Assert.Null(status.NextRestartAt);
        Assert.Equal(3, status.AttemptCount);
        Assert.Equal(3, processHelper.StartAttempts);
        Assert.Contains(crashHistory.Events, e => e == "Circuit opened");
        Assert.Single(store.LoadOpenCircuits());

        time.Advance(TimeSpan.FromMinutes(1));
        await service.CheckAndRestartAsync(app, wasRunning: false, isRunningNow: false);
        Assert.Equal(3, processHelper.StartAttempts);
    }

    [Fact]
    public async Task CheckAndRestartAsync_Expires_Attempts_Outside_Rolling_Window()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var processHelper = new TestProcessHelper();
        var service = CreateService(timeProvider: time, processHelper: processHelper);
        var app = CreateApp(maxRestarts: 3);

        await service.CheckAndRestartAsync(app, wasRunning: true, isRunningNow: false);
        Assert.Equal(1, processHelper.StartAttempts);

        time.Advance(TimeSpan.FromMinutes(11));
        await service.CheckAndRestartAsync(app, wasRunning: true, isRunningNow: false);
        Assert.Equal(2, processHelper.StartAttempts);
        Assert.Equal(1, service.GetStatus(app.ProcessTarget).AttemptCount);

        await service.CheckAndRestartAsync(app, wasRunning: true, isRunningNow: false);
        var status = service.GetStatus(app.ProcessTarget);
        Assert.Equal(2, status.AttemptCount);
        Assert.Equal(time.GetUtcNow().UtcDateTime.AddSeconds(2), status.NextRestartAt);
    }

    [Fact]
    public async Task Open_Circuit_Persists_Across_Service_Recreation_And_Can_Be_Acknowledged()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var store = new MemoryWatchdogCircuitStore();
        var app = CreateApp(maxRestarts: 3);

        var firstService = CreateService(timeProvider: time, store: store);
        await firstService.CheckAndRestartAsync(app, wasRunning: true, isRunningNow: false);
        await CrashAndExecutePendingRestartAsync(firstService, app, time, TimeSpan.FromSeconds(2));
        await CrashAndExecutePendingRestartAsync(firstService, app, time, TimeSpan.FromSeconds(4));
        await firstService.CheckAndRestartAsync(app, wasRunning: true, isRunningNow: false);

        var recreatedService = CreateService(timeProvider: time, store: store);
        var status = recreatedService.GetStatus(app.ProcessTarget);

        Assert.True(status.IsCircuitOpen);
        Assert.Single(store.LoadOpenCircuits());

        recreatedService.AcknowledgeCircuit(app.ProcessTarget);

        Assert.Equal(WatchdogStatus.None, recreatedService.GetStatus(app.ProcessTarget));
        Assert.Empty(store.LoadOpenCircuits());
    }

    [Fact]
    public async Task AcknowledgeCircuit_Resets_Attempt_Counting()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var processHelper = new TestProcessHelper();
        var store = new MemoryWatchdogCircuitStore();
        var service = CreateService(time, processHelper, store: store);
        var app = CreateApp(maxRestarts: 3);

        await service.CheckAndRestartAsync(app, wasRunning: true, isRunningNow: false);
        await CrashAndExecutePendingRestartAsync(service, app, time, TimeSpan.FromSeconds(2));
        await CrashAndExecutePendingRestartAsync(service, app, time, TimeSpan.FromSeconds(4));
        await service.CheckAndRestartAsync(app, wasRunning: true, isRunningNow: false);

        service.AcknowledgeCircuit(app.ProcessTarget);

        await service.CheckAndRestartAsync(app, wasRunning: true, isRunningNow: false);
        Assert.Equal(4, processHelper.StartAttempts);
        Assert.Equal(1, service.GetStatus(app.ProcessTarget).AttemptCount);

        await service.CheckAndRestartAsync(app, wasRunning: true, isRunningNow: false);
        var status = service.GetStatus(app.ProcessTarget);
        Assert.Equal(2, status.AttemptCount);
        Assert.Equal(time.GetUtcNow().UtcDateTime.AddSeconds(2), status.NextRestartAt);
    }

    [Fact]
    public async Task TotalRestarts_Tracks_Executed_Attempts_Not_Crash_Detections()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var service = CreateService(timeProvider: time);
        var app = CreateApp(maxRestarts: 3);

        await service.CheckAndRestartAsync(app, wasRunning: true, isRunningNow: false);
        Assert.Equal((1, 1), service.GetCrashStats(app.ProcessTarget));

        await service.CheckAndRestartAsync(app, wasRunning: true, isRunningNow: false);
        Assert.Equal((2, 1), service.GetCrashStats(app.ProcessTarget));

        time.Advance(TimeSpan.FromSeconds(2));
        await service.CheckAndRestartAsync(app, wasRunning: false, isRunningNow: false);
        Assert.Equal((2, 2), service.GetCrashStats(app.ProcessTarget));
    }

    private static async Task AssertPendingRestartAsync(
        WatchdogService service,
        AppItem app,
        FakeTimeProvider time,
        TimeSpan expectedDelay,
        int expectedAttempt)
    {
        await service.CheckAndRestartAsync(app, wasRunning: true, isRunningNow: false);

        var status = service.GetStatus(app.ProcessTarget);
        Assert.Equal(expectedAttempt, status.AttemptCount);
        Assert.Equal(time.GetUtcNow().UtcDateTime.Add(expectedDelay), status.NextRestartAt);

        time.Advance(expectedDelay);
        await service.CheckAndRestartAsync(app, wasRunning: false, isRunningNow: false);
    }

    private static async Task CrashAndExecutePendingRestartAsync(
        WatchdogService service,
        AppItem app,
        FakeTimeProvider time,
        TimeSpan delay)
    {
        await service.CheckAndRestartAsync(app, wasRunning: true, isRunningNow: false);
        time.Advance(delay);
        await service.CheckAndRestartAsync(app, wasRunning: false, isRunningNow: false);
    }

    private static WatchdogService CreateService(
        FakeTimeProvider? timeProvider = null,
        TestProcessHelper? processHelper = null,
        TestCrashHistoryService? crashHistory = null,
        IWatchdogCircuitStore? store = null)
    {
        return CreateConfiguredService(
            timeProvider ?? new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            processHelper ?? new TestProcessHelper(),
            crashHistory ?? new TestCrashHistoryService(),
            store ?? new MemoryWatchdogCircuitStore());
    }

    private static WatchdogService CreateConfiguredService(
        FakeTimeProvider timeProvider,
        TestProcessHelper? processHelper = null,
        TestCrashHistoryService? crashHistory = null,
        IWatchdogCircuitStore? store = null)
    {
        var helper = processHelper ?? new TestProcessHelper();
        var lifecycleService = new TestAppLifecycleService(helper);
        return new WatchdogService(
            NullLogger<WatchdogService>.Instance,
            helper,
            lifecycleService,
            new NotificationService(),
            crashHistory ?? new TestCrashHistoryService(),
            store ?? new MemoryWatchdogCircuitStore(),
            timeProvider);
    }

    private static AppItem CreateApp(int maxRestarts = 3) =>
        new()
        {
            Name = "Worker",
            Path = "worker.exe",
            WatchdogEnabled = true,
            MaxRestarts = maxRestarts
        };

    private sealed class TestProcessHelper : ProcessHelper
    {
        public TestProcessHelper() : base(NullLogger<ProcessHelper>.Instance)
        {
        }

        public int StartAttempts { get; private set; }
        public ProcessExitKind ExitKind { get; set; } = ProcessExitKind.Unexpected;
        public override ProcessExitKind GetLastExitKind(AppProcessTarget target) => ExitKind;

        public override Task<AppLaunchResult> StartAppAsync(AppItem app, CancellationToken ct = default)
        {
            StartAttempts++;
            return Task.FromResult(new AppLaunchResult(AppLaunchKind.StartedNewProcess));
        }
    }

    private sealed class TestAppLifecycleService : IAppLifecycleService
    {
        private readonly TestProcessHelper _processHelper;

        public TestAppLifecycleService(TestProcessHelper processHelper)
        {
            _processHelper = processHelper;
        }

        public int LaunchAttempts { get; private set; }

        public async Task LaunchAsync(AppItem app, CancellationToken ct = default)
        {
            LaunchAttempts++;
            await _processHelper.StartAppAsync(app, ct);
        }

        public Task StopAsync(AppItem app, AppStopMode mode, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class TestCrashHistoryService : CrashHistoryService
    {
        public TestCrashHistoryService() : base(NullLogger<CrashHistoryService>.Instance)
        {
        }

        public List<string> Events { get; } = [];

        public override void RecordCrash(string appName, string appPath)
        {
            Events.Add("Crash");
        }

        public override void RecordRestart(string appName, string appPath, int attemptNumber)
        {
            Events.Add($"Restart:{attemptNumber}");
        }

        public override void RecordEvent(string appName, string appPath, string eventType)
        {
            Events.Add(eventType);
        }
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;

        public FakeTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration)
        {
            _utcNow = _utcNow.Add(duration);
        }
    }
}
