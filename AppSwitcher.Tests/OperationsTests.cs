using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;

namespace AppSwitcher.Tests;

public class OperationsTests
{
    [Fact]
    public async Task Queued_Scheduled_Restart_Still_Observes_Shutdown_After_The_Check_Pass_Returns()
    {
        using var helper = new RunningHelper(); var lifecycle = new WaitingLifecycle();
        using var watchdog = new WatchdogService(NullLogger<WatchdogService>.Instance, helper, lifecycle,
            new NotificationService(), new CrashHistoryService(NullLogger<CrashHistoryService>.Instance));
        using var scheduler = new SchedulerService(NullLogger<SchedulerService>.Instance, helper, lifecycle, watchdog, new NotificationService());
        using var shutdown = new CancellationTokenSource();
        var app = new AppItem { Name = "Worker", Path = "fake.exe", ScheduleRestartTime = DateTime.Now.ToString("HH:mm") };
        await scheduler.CheckSchedulesAsync(() => [app], shutdown.Token);
        await lifecycle.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        shutdown.Cancel();
        await lifecycle.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }
    [Fact]
    public async Task Scheduler_Skips_Paused_Events_Instead_Of_Catching_Up_On_Resume()
    {
        using var temp = new Temp();
        var clock = new TestClock(); clock.Advance(TimeSpan.FromMinutes(-2));
        using var maintenance = new MaintenanceService(temp.File, clock);
        maintenance.Pause(TimeSpan.FromMinutes(30));
        clock.Advance(TimeSpan.FromMinutes(3)); maintenance.Resume();
        using var helper = new StoppedHelper();
        var lifecycle = new CountingLifecycle();
        using var watchdog = new WatchdogService(NullLogger<WatchdogService>.Instance, helper, lifecycle,
            new NotificationService(), new CrashHistoryService(NullLogger<CrashHistoryService>.Instance));
        using var scheduler = new SchedulerService(NullLogger<SchedulerService>.Instance, helper, lifecycle,
            watchdog, new NotificationService(), maintenance);
        var app = new AppItem { Name = "Scheduled", Path = "fake.exe", ScheduleTime = DateTime.Now.ToString("HH:mm") };
        await scheduler.CheckSchedulesAsync(() => [app], default);
        Assert.Empty(lifecycle.Launched);
        using var normal = new SchedulerService(NullLogger<SchedulerService>.Instance, helper, lifecycle,
            watchdog, new NotificationService());
        await normal.CheckSchedulesAsync(() => [app], default);
        Assert.Equal(["Scheduled"], lifecycle.Launched);
    }

    [Fact]
    public async Task Scheduler_Does_Not_Start_A_Dependent_When_An_Existing_Dependency_Is_Not_Ready()
    {
        using var helper = new DependencyHelper();
        using var readiness = new AppReadinessService(helper);
        var lifecycle = new CountingLifecycle();
        using var watchdog = new WatchdogService(NullLogger<WatchdogService>.Instance, helper, lifecycle,
            new NotificationService(), new CrashHistoryService(NullLogger<CrashHistoryService>.Instance));
        using var scheduler = new SchedulerService(NullLogger<SchedulerService>.Instance, helper, lifecycle,
            watchdog, new NotificationService(), readiness: readiness);
        var dependency = new AppItem { Name = "Database", Path = "db.exe", Readiness = ReadinessMode.Window, ReadinessTimeoutSeconds = 1 };
        var app = new AppItem { Name = "Client", Path = "client.exe", DependsOn = "Database", ScheduleTime = DateTime.Now.ToString("HH:mm") };
        await scheduler.CheckSchedulesAsync(() => [dependency, app], default);
        Assert.Empty(lifecycle.Launched);
    }

    [Fact]
    public void Maintenance_Persists_Expires_And_Does_Not_Replay_Any_Paused_Schedule()
    {
        using var temp = new Temp();
        var clock = new TestClock();
        var firstDue = clock.GetUtcNow().AddMinutes(1);
        using (var service = new MaintenanceService(temp.File, clock))
        {
            var ongoing = service.AutomaticToken;
            service.Pause(TimeSpan.FromMinutes(30));
            Assert.True(ongoing.IsCancellationRequested);
            clock.Advance(TimeSpan.FromMinutes(2));
            service.Resume();
            Assert.False(service.AutomaticToken.IsCancellationRequested);
            service.Pause(TimeSpan.FromMinutes(30));
        }
        using var restored = new MaintenanceService(temp.File, clock);
        Assert.True(restored.IsPaused);
        Assert.True(restored.AutomaticToken.IsCancellationRequested);
        Assert.True(restored.Covers(firstDue));
        clock.Advance(TimeSpan.FromMinutes(31));
        Assert.False(restored.IsPaused);
        Assert.False(restored.AutomaticToken.IsCancellationRequested);
        Assert.True(restored.Covers(firstDue));
        Assert.False(restored.Covers(clock.GetUtcNow()));
    }

    [Fact]
    public void Maintenance_Corrupt_State_Is_Visible_And_Blocks_Automation_For_Bounded_Time()
    {
        using var temp = new Temp(); File.WriteAllText(temp.File, "broken");
        var clock = new TestClock();
        using var service = new MaintenanceService(temp.File, clock);
        Assert.NotNull(service.LoadError);
        Assert.True(service.IsPaused);
        clock.Advance(TimeSpan.FromMinutes(31));
        Assert.False(service.IsPaused);
        Assert.False(service.AutomaticToken.IsCancellationRequested);
    }

    [Fact]
    public void Maintenance_Failed_Persist_Does_Not_Report_A_Successful_Pause()
    {
        using var temp = new Temp();
        using var service = new MaintenanceService(temp.File, TimeProvider.System);
        Directory.CreateDirectory(temp.File); // destination is a directory, so atomic replacement fails
        var failure = Record.Exception(() => service.Pause(TimeSpan.FromMinutes(30)));
        Assert.True(failure is IOException or UnauthorizedAccessException);
        Assert.False(service.IsPaused);
    }

    [Fact]
    public async Task Readiness_Retries_And_Timeout_Is_Distinct_From_Caller_Cancellation()
    {
        var app = new AppItem { Name = "Worker", Readiness = ReadinessMode.Process, ReadinessTimeoutSeconds = 1 };
        int calls = 0;
        await AppReadinessService.WaitUntilAsync(app, _ => Task.FromResult(++calls == 2), default);
        Assert.Equal(2, calls);
        await Assert.ThrowsAsync<TimeoutException>(() => AppReadinessService.WaitUntilAsync(app, _ => Task.FromResult(false), default));
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => AppReadinessService.WaitUntilAsync(app,
            token => Task.Delay(1, token).ContinueWith(_ => false, token), cancel.Token));
    }

    [Fact]
    public async Task Http_Readiness_Completes_From_Headers_Without_Downloading_The_Response_Body()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var helper = new RunningHelper(); using var readiness = new AppReadinessService(helper);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var app = new AppItem { Name = "Web", Path = "fake.exe", Readiness = ReadinessMode.Http,
            HealthCheckUrl = $"http://127.0.0.1:{port}/", ReadinessTimeoutSeconds = 3 };
        var ready = readiness.WaitAsync(app, deadline.Token);
        using var client = await listener.AcceptTcpClientAsync(deadline.Token);
        var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
        while (!string.IsNullOrEmpty(await reader.ReadLineAsync(deadline.Token))) { }
        await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 100000000\r\n\r\n"), deadline.Token);
        await ready.WaitAsync(TimeSpan.FromSeconds(2)); // no body was ever sent
    }

    [Theory]
    [InlineData(ReadinessMode.Window, true, "", false)]
    [InlineData(ReadinessMode.Http, false, "file:///tmp", false)]
    [InlineData(ReadinessMode.Http, false, "https://user:secret@host/", false)]
    [InlineData(ReadinessMode.Http, false, "https://192.168.1.78:8443/health", true)]
    public void Readiness_Configuration_Is_Validated(ReadinessMode mode, bool website, string health, bool valid)
    {
        var app = new AppItem { Name = "Worker", Path = "fake.exe", Readiness = mode, HealthCheckUrl = health };
        if (website) { app.TargetKind = AppTargetKind.Website; app.WebsiteUrl = "https://example.org"; app.BrowserProfileId = Guid.NewGuid().ToString(); }
        var validation = new ConfigValidationService(NullLogger<ConfigValidationService>.Instance);
        Assert.Equal(valid, !validation.Validate(new ConfigRoot { Apps = [app] }).HasErrors);
    }

    [Fact]
    public void Workspace_Uses_Identity_After_Rename_And_Includes_Group_Dependencies_Once()
    {
        var database = new AppItem { Name = "Database", Path = "db.exe" };
        var renamed = new AppItem { Name = "Renamed", Path = "ui.exe", Group = "production", DependsOn = "Database" };
        var other = new AppItem { Name = "Other", Path = "other.exe", Group = "production", DependsOn = "Database" };
        var layout = new ScreenLayout { AppIds = [renamed.AppId], GroupKeys = ["production"],
            Positions = [new WindowPosition { AppId = renamed.AppId, AppName = "Old name" }] };
        Assert.Equal([database, renamed, other], WorkspacePlanner.Resolve(layout, [database, renamed, other]));
        layout.AppIds.Add(Guid.NewGuid().ToString());
        Assert.Throws<InvalidOperationException>(() => WorkspacePlanner.Resolve(layout, [database, renamed, other]));
    }

    [Fact]
    public async Task Workspace_Rejects_Operator_Scope_Before_Any_Launch()
    {
        var allowed = new AppItem { Name = "Allowed", Path = "allowed.exe", DependsOn = "Forbidden" };
        var forbidden = new AppItem { Name = "Forbidden", Path = "forbidden.exe" };
        using var fixture = await MainViewModelAuditTests.CreateHarnessAsync(new ConfigRoot
        {
            Apps = [allowed, forbidden], Settings = new AppSettings { OperatorScope = new() { AllowedAppNames = ["Allowed"] } }
        });
        fixture.ViewModel.CurrentAccessLevel = AccessLevel.Operator;
        await fixture.ViewModel.StartWorkspaceAsync(new ScreenLayout { Name = "Test", AppIds = [allowed.AppId] });
        Assert.Equal("Failed", fixture.AuditLog.GetRecentEntries().Single(e => e.Action == "StartWorkspace").Outcome);
    }

    [Fact]
    public void Filter_Preserves_Order_And_Matches_Localized_Groups()
    {
        var first = new AppItem { Name = "A", Group = "prod" };
        var second = new AppItem { Name = "B", Group = "prod" };
        var other = new AppItem { Name = "C" };
        Assert.Equal([first, second], MainViewModel.FilterLauncherApps([first, other, second], "Produktion",
            [new GroupDefinition { Key = "prod", Names = new() { ["de"] = "Produktion" } }], "de"));
        Assert.Empty(MainViewModel.FilterLauncherApps([first], "absent", [], "en"));
    }

    [Fact]
    public void Support_Report_Excludes_Secrets_Even_From_Untrusted_Diagnostic_Fields()
    {
        var app = new AppItem { Name = "SECRET", Path = "C:\\SECRET", Args = "SECRET", WebsiteUrl = "https://SECRET",
            HealthStatus = "SECRET", BrowserProfileId = "SECRET", CrashCountToday = 4 };
        var report = SupportReportService.Build([app], [new("SECRET", BrowserKind.Edge, "SECRET", "SECRET")],
            [new(DateTime.Now, "SECRET", "SECRET", "SECRET", "SECRET", "SECRET", "SECRET")], false, 1, 2);
        Assert.DoesNotContain("SECRET", report);
        using var parsed = JsonDocument.Parse(report);
        Assert.Equal(4, parsed.RootElement.GetProperty("applications")[0].GetProperty("CrashCountToday").GetInt32());
    }

    [Fact]
    public async Task Slow_Responsiveness_Probes_Are_Bounded_And_Do_Not_Block_Their_Caller()
    {
        var monitor = new ResponsivenessMonitor();
        using var release = new ManualResetEventSlim();
        using var cancel = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int active = 0, maximum = 0, completed = 0;
        try
        {
            for (int i = 0; i < 12; i++) monitor.Queue(i.ToString(), TimeSpan.Zero, () =>
            {
                var count = Interlocked.Increment(ref active);
                lock (monitor) maximum = Math.Max(maximum, count);
                started.TrySetResult();
                release.Wait(); Interlocked.Decrement(ref active); return true;
            }, _ => Interlocked.Increment(ref completed), cancel.Token);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.InRange(maximum, 1, 4);
            Assert.Equal(0, completed);
        }
        finally { release.Set(); await monitor.DrainAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
        Assert.InRange(maximum, 1, 4);
        Assert.Equal(12, completed);
    }

    [Fact]
    public void Process_Observation_Shares_Discovery_But_Returns_Independent_Disposable_Handles()
    {
        using var current = Process.GetCurrentProcess(); using var inventory = new ProcessInventory();
        var first = inventory.Candidates(current.ProcessName); var second = inventory.Candidates(current.ProcessName);
        Assert.Equal(1, inventory.EnumerationCount);
        Assert.Contains(first, p => p.Id == current.Id);
        foreach (var process in first) process.Dispose();
        Assert.False(second.Single(p => p.Id == current.Id).HasExited);
        foreach (var process in second) process.Dispose();
    }

    [Fact]
    public async Task Process_Exit_Wakes_Observation_Before_The_Reconciliation_Deadline()
    {
        // Only this test-owned, hidden process is terminated.
        using var process = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "ping.exe"), "-n 30 127.0.0.1")
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!;
        try
        {
            using var inventory = new ProcessInventory();
            var candidates = inventory.Candidates("ping");
            Assert.Contains(candidates, p => p.Id == process.Id);
            foreach (var candidate in candidates) candidate.Dispose();
            var changed = inventory.WaitForChangeAsync(TimeSpan.FromSeconds(30), default);
            process.Kill(); await process.WaitForExitAsync();
            await changed.WaitAsync(TimeSpan.FromSeconds(3));
        }
        finally { if (!process.HasExited) { process.Kill(); await process.WaitForExitAsync(); } }
    }

    private sealed class RunningHelper() : ProcessHelper(NullLogger<ProcessHelper>.Instance)
    { public override bool IsAppRunning(AppProcessTarget target) => true; }
    private sealed class StoppedHelper() : ProcessHelper(NullLogger<ProcessHelper>.Instance)
    { public override bool IsAppRunning(AppProcessTarget target) => false; }
    private sealed class DependencyHelper() : ProcessHelper(NullLogger<ProcessHelper>.Instance)
    {
        public override bool IsAppRunning(AppProcessTarget target) => target.Path == "db.exe";
        public override IntPtr GetMainWindowHandle(AppProcessTarget target) => IntPtr.Zero;
    }
    private sealed class CountingLifecycle : IAppLifecycleService
    {
        public List<string> Launched { get; } = [];
        public Task LaunchAsync(AppItem app, CancellationToken ct = default) { Launched.Add(app.Name); return Task.CompletedTask; }
        public Task StopAsync(AppItem app, AppStopMode mode, CancellationToken ct = default) => Task.CompletedTask;
    }
    private sealed class WaitingLifecycle : IAppLifecycleService
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task LaunchAsync(AppItem app, CancellationToken ct = default)
        {
            Started.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, ct); }
            catch (OperationCanceledException) { Cancelled.TrySetResult(); throw; }
        }
        public Task StopAsync(AppItem app, AppStopMode mode, CancellationToken ct = default) => Task.CompletedTask;
    }
    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }
    private sealed class Temp : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "AppSwitcher.Tests." + Guid.NewGuid().ToString("N"));
        public Temp() => Directory.CreateDirectory(_directory);
        public string File => Path.Combine(_directory, "maintenance.json");
        public void Dispose() => Directory.Delete(_directory, true);
    }
}
