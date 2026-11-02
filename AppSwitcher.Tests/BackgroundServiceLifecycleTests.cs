using Microsoft.Extensions.Logging.Abstractions;

namespace AppSwitcher.Tests;

public class BackgroundServiceLifecycleTests
{
    [Fact]
    public async Task ImmediateStopAndRestartNeverReadAClearedCancellationSource()
    {
        using var helper = new ProcessHelper(NullLogger<ProcessHelper>.Instance);
        var notifications = new NotificationService();
        var lifecycle = new NoOpAppLifecycleService();
        using var watchdog = new WatchdogService(NullLogger<WatchdogService>.Instance, helper, lifecycle,
            notifications, new CrashHistoryService(NullLogger<CrashHistoryService>.Instance));
        using var scheduler = new SchedulerService(NullLogger<SchedulerService>.Instance, helper, lifecycle, watchdog, notifications);
        using var health = new HealthCheckService(NullLogger<HealthCheckService>.Instance);
        using var resources = new ResourceMonitorService(NullLogger<ResourceMonitorService>.Instance, helper, notifications);
        using var versions = new AppVersionService(NullLogger<AppVersionService>.Instance, notifications);
        using var session = new SessionTimeoutService(NullLogger<SessionTimeoutService>.Instance);
        using var windows = new WindowPolicyEnforcementService(NullLogger<WindowPolicyEnforcementService>.Instance,
            helper, new WindowsWindowPolicyPlatform());
        for (var iteration = 0; iteration < 100; iteration++)
        {
            scheduler.Start(() => []);
            health.Start(() => []);
            resources.Start(() => []);
            versions.Start(() => []);
            session.Start(1, () => Assert.Fail("An immediately stopped session must not expire."));
            windows.Start(() => []);
            await Task.WhenAll(scheduler.StopAsync(), health.StopAsync(), resources.StopAsync(),
                versions.StopAsync(), session.StopAsync(), windows.StopAsync()).WaitAsync(TimeSpan.FromSeconds(5));
        }
    }
}
