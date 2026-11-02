using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace AppSwitcher.Tests;

public class AppLifecycleServiceTests
{
    [Fact]
    public async Task Website_Activation_Skips_Hooks_And_Shared_Process_Start()
    {
        var helper = new TestProcessHelper { IsRunningNow = true };
        var hooks = new TestHookRunner();
        var service = CreateService(helper, hooks);
        var app = CreateApp(); app.TargetKind = AppTargetKind.Website;
        await service.LaunchAsync(app);
        Assert.Empty(hooks.Stages);
        Assert.Equal(0, helper.StartCalls);
        Assert.Equal(1, helper.WebsiteStartCalls);
    }

    [Fact]
    public async Task Website_Force_Stop_Closes_Only_The_Managed_Tab()
    {
        var helper = new TestProcessHelper { IsRunningNow = true };
        var hooks = new TestHookRunner();
        var service = CreateService(helper, hooks);
        var app = CreateApp(); app.TargetKind = AppTargetKind.Website;
        await service.StopAsync(app, AppStopMode.ForceKill);
        Assert.Equal(1, helper.WebsiteStopCalls);
        Assert.Equal(0, helper.KillCalls);
        Assert.Equal(0, helper.CloseCalls);
        Assert.Equal([AppLifecycleHookStage.PreStop, AppLifecycleHookStage.PostStop], hooks.Stages);
    }

    [Fact]
    public async Task LaunchAsync_Runs_Pre_And_Post_Launch_Hooks_For_New_Start()
    {
        var processHelper = new TestProcessHelper { IsRunningNow = false };
        var hooks = new TestHookRunner();
        var service = CreateService(processHelper, hooks);

        await service.LaunchAsync(CreateApp());

        Assert.Equal([AppLifecycleHookStage.PreLaunch, AppLifecycleHookStage.PostLaunch], hooks.Stages);
        Assert.Equal(1, processHelper.StartCalls);
    }

    [Fact]
    public async Task LaunchAsync_Skips_Hooks_When_App_Is_Already_Running()
    {
        var processHelper = new TestProcessHelper { IsRunningNow = true };
        var hooks = new TestHookRunner();
        var service = CreateService(processHelper, hooks);

        await service.LaunchAsync(CreateApp());

        Assert.Empty(hooks.Stages);
        Assert.Equal(1, processHelper.StartCalls);
    }

    [Fact]
    public async Task LaunchAsync_PreLaunch_Failure_Blocks_Start()
    {
        var processHelper = new TestProcessHelper();
        var hooks = new TestHookRunner { FailingStage = AppLifecycleHookStage.PreLaunch };
        var service = CreateService(processHelper, hooks);

        await Assert.ThrowsAsync<AppHookExecutionException>(() => service.LaunchAsync(CreateApp()));
        Assert.Equal(0, processHelper.StartCalls);
    }

    [Fact]
    public async Task LaunchAsync_PostLaunch_Failure_Does_Not_Fail_Operation()
    {
        var processHelper = new TestProcessHelper();
        var hooks = new TestHookRunner { FailingStage = AppLifecycleHookStage.PostLaunch };
        var service = CreateService(processHelper, hooks);

        await service.LaunchAsync(CreateApp());

        Assert.Equal(1, processHelper.StartCalls);
    }

    [Fact]
    public async Task StopAsync_ForceKill_Runs_Pre_And_Post_Stop_Hooks()
    {
        var processHelper = new TestProcessHelper { IsRunningNow = true };
        var hooks = new TestHookRunner();
        var service = CreateService(processHelper, hooks);

        await service.StopAsync(CreateApp(), AppStopMode.ForceKill);

        Assert.Equal([AppLifecycleHookStage.PreStop, AppLifecycleHookStage.PostStop], hooks.Stages);
        Assert.Equal(1, processHelper.KillCalls);
    }

    [Fact]
    public async Task StopAsync_Graceful_Stop_Falls_Back_To_Kill()
    {
        var processHelper = new TestProcessHelper
        {
            IsRunningNow = true,
            CloseStopsProcess = false,
            KillStopsProcess = true
        };
        var service = CreateService(processHelper, new TestHookRunner());

        await service.StopAsync(CreateApp(), AppStopMode.Restart);

        Assert.Equal(1, processHelper.CloseCalls);
        Assert.Equal(1, processHelper.KillCalls);
    }

    [Fact]
    public async Task StopAsync_PreStop_Failure_Blocks_Stop()
    {
        var processHelper = new TestProcessHelper { IsRunningNow = true };
        var hooks = new TestHookRunner { FailingStage = AppLifecycleHookStage.PreStop };
        var service = CreateService(processHelper, hooks);

        await Assert.ThrowsAsync<AppHookExecutionException>(() => service.StopAsync(CreateApp(), AppStopMode.ForceKill));
        Assert.Equal(0, processHelper.CloseCalls);
        Assert.Equal(0, processHelper.KillCalls);
    }

    [Fact]
    public async Task Restart_Then_Launch_Preserves_Hook_Order()
    {
        var timeline = new List<string>();
        var processHelper = new TestProcessHelper
        {
            IsRunningNow = true,
            CloseStopsProcess = false,
            KillStopsProcess = true,
            Timeline = timeline
        };
        var hooks = new TestHookRunner { Timeline = timeline };
        var service = CreateService(processHelper, hooks);
        var app = CreateApp();

        await service.StopAsync(app, AppStopMode.Restart);
        await service.LaunchAsync(app);

        Assert.Equal(
            ["hook:PreStop", "close", "kill", "hook:PostStop", "hook:PreLaunch", "start", "hook:PostLaunch"],
            timeline.ToArray());
    }

    [Fact]
    public async Task LaunchAsync_Serializes_Concurrent_Operations_Per_App()
    {
        var processHelper = new TestProcessHelper();
        var hooks = new TestHookRunner { BlockStage = AppLifecycleHookStage.PreLaunch };
        var service = CreateService(processHelper, hooks);
        var app = CreateApp();

        var firstLaunch = service.LaunchAsync(app);
        await hooks.Blocked.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var secondLaunch = service.LaunchAsync(app);
        await Task.Delay(100);

        Assert.Equal(1, hooks.PreLaunchCallCount);
        Assert.False(secondLaunch.IsCompleted);

        hooks.ReleaseBlock.SetResult();
        await Task.WhenAll(firstLaunch, secondLaunch);

        Assert.Equal(1, hooks.PreLaunchCallCount);
        Assert.Equal(2, processHelper.StartCalls);
    }

    private static AppItem CreateApp() =>
        new()
        {
            Name = "Worker",
            Path = "worker.exe",
            PreLaunchHookPath = "pre.ps1",
            PostLaunchHookPath = "post.cmd",
            PreStopHookPath = "before-stop.ps1",
            PostStopHookPath = "after-stop.cmd"
        };

    private static AppLifecycleService CreateService(TestProcessHelper processHelper, TestHookRunner hooks)
    {
        var services = new ServiceCollection().BuildServiceProvider();
        return new AppLifecycleService(
            processHelper,
            hooks,
            services,
            new NotificationService(),
            NullLogger<AppLifecycleService>.Instance);
    }

    private sealed class TestProcessHelper : ProcessHelper
    {
        public TestProcessHelper() : base(NullLogger<ProcessHelper>.Instance)
        {
        }

        public bool IsRunningNow { get; set; }
        public bool CloseStopsProcess { get; set; }
        public bool KillStopsProcess { get; set; } = true;
        public int StartCalls { get; private set; }
        public int CloseCalls { get; private set; }
        public int KillCalls { get; private set; }
        public List<string> Events { get; } = [];
        public List<string>? Timeline { get; set; }
        public int WebsiteStartCalls { get; private set; }
        public int WebsiteStopCalls { get; private set; }
        public override async Task<AppLaunchResult> StartWebsiteAsync(AppItem app, Func<Task>? beforeCreate, CancellationToken ct = default)
        {
            WebsiteStartCalls++;
            if (IsRunningNow) return new(AppLaunchKind.ActivatedExisting);
            if (beforeCreate != null) await beforeCreate();
            IsRunningNow = true;
            return new(AppLaunchKind.StartedNewProcess);
        }
        public override Task StopWebsiteAsync(AppItem app, CancellationToken ct = default)
        {
            WebsiteStopCalls++;
            IsRunningNow = false;
            return Task.CompletedTask;
        }

        public override Task<AppLaunchResult> StartAppAsync(AppItem app, CancellationToken ct = default)
        {
            StartCalls++;
            Events.Add("start");
            Timeline?.Add("start");

            var result = IsRunningNow
                ? new AppLaunchResult(AppLaunchKind.ActivatedExisting)
                : new AppLaunchResult(AppLaunchKind.StartedNewProcess);

            IsRunningNow = true;
            return Task.FromResult(result);
        }

        public override bool IsAppRunning(AppProcessTarget target) => IsRunningNow;

        public override void CloseApp(AppProcessTarget target)
        {
            CloseCalls++;
            Events.Add("close");
            Timeline?.Add("close");
            if (CloseStopsProcess)
                IsRunningNow = false;
        }

        public override int KillApp(AppProcessTarget target)
        {
            KillCalls++;
            Events.Add("kill");
            Timeline?.Add("kill");
            if (KillStopsProcess)
                IsRunningNow = false;
            return 1;
        }
    }

    private sealed class TestHookRunner : IAppHookRunner
    {
        public AppLifecycleHookStage? FailingStage { get; set; }
        public AppLifecycleHookStage? BlockStage { get; set; }
        public TaskCompletionSource Blocked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseBlock { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<AppLifecycleHookStage> Stages { get; } = [];
        public List<string> Events { get; } = [];
        public List<string>? Timeline { get; set; }
        public int PreLaunchCallCount { get; private set; }

        public async Task RunAsync(AppItem app, AppLifecycleHookStage stage, CancellationToken ct = default)
        {
            Stages.Add(stage);
            Events.Add($"hook:{stage}");
            Timeline?.Add($"hook:{stage}");

            if (stage == AppLifecycleHookStage.PreLaunch)
                PreLaunchCallCount++;

            if (BlockStage == stage)
            {
                Blocked.TrySetResult();
                await ReleaseBlock.Task.WaitAsync(ct);
            }

            if (FailingStage == stage)
                throw new AppHookExecutionException(app, stage, "boom");
        }
    }
}
