using Microsoft.Extensions.Logging.Abstractions;

namespace AppSwitcher.Tests;

public class SchedulerServiceTests
{
    [Fact]
    public async Task StopAsync_Cancels_Tracked_Pending_Operations()
    {
        var scheduler = CreateScheduler();
        scheduler.Start(() => []);

        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var trackedTask = scheduler.TrackOperationAsync("test-operation", async ct =>
        {
            started.SetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            }
            catch (OperationCanceledException)
            {
                cancelled.SetResult();
                throw;
            }
        });

        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await scheduler.StopAsync();
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(trackedTask.IsCompleted);
    }

    [Fact]
    public void ResolveScheduledApp_Returns_Null_When_Schedule_Changes()
    {
        var currentApps = new[]
        {
            new AppItem { Name = "Worker", Path = "worker.exe", ScheduleRestartTime = "09:30" }
        };

        var resolved = SchedulerService.ResolveScheduledApp(
            currentApps,
            "Worker",
            "08:00",
            app => app.ScheduleRestartTime);

        Assert.Null(resolved);
    }

    [Fact]
    public void ResolveScheduledApp_Returns_Current_App_When_Schedule_Is_Unchanged()
    {
        var currentApps = new[]
        {
            new AppItem { Name = "Worker", Path = "worker.exe", ScheduleShutdownTime = "18:00" }
        };

        var resolved = SchedulerService.ResolveScheduledApp(
            currentApps,
            "Worker",
            "18:00",
            app => app.ScheduleShutdownTime);

        Assert.NotNull(resolved);
        Assert.Equal("Worker", resolved!.Name);
    }

    [Theory]
    [InlineData("08:30", true)]
    [InlineData("1:2", false)]
    [InlineData("7", false)]
    [InlineData("25:99", false)]
    public void TryParseScheduledTime_Requires_Exact_HH_mm_Format(string value, bool expected)
    {
        var success = SchedulerService.TryParseScheduledTime(value, out _);

        Assert.Equal(expected, success);
    }

    private static SchedulerService CreateScheduler() =>
        CreateSchedulerCore();

    private static SchedulerService CreateSchedulerCore()
    {
        var lifecycleService = new TestAppLifecycleService();
        return new SchedulerService(
            NullLogger<SchedulerService>.Instance,
            new ProcessHelper(NullLogger<ProcessHelper>.Instance),
            lifecycleService,
            new WatchdogService(
                NullLogger<WatchdogService>.Instance,
                new ProcessHelper(NullLogger<ProcessHelper>.Instance),
                lifecycleService,
                new NotificationService(),
                new CrashHistoryService(NullLogger<CrashHistoryService>.Instance)),
            new NotificationService());
    }

    private sealed class TestAppLifecycleService : IAppLifecycleService
    {
        public Task LaunchAsync(AppItem app, CancellationToken ct = default) => Task.CompletedTask;

        public Task StopAsync(AppItem app, AppStopMode mode, CancellationToken ct = default) => Task.CompletedTask;
    }
}
