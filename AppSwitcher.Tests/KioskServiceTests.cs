using Microsoft.Extensions.Logging.Abstractions;

namespace AppSwitcher.Tests;

public class KioskServiceTests
{
    [Fact]
    public async Task ActivateAsync_Hides_All_Taskbars_And_DeactivateAsync_Restores_Them()
    {
        var platform = new FakeKioskPlatform();
        platform.TaskbarHandles.AddRange([(IntPtr)1, (IntPtr)2, (IntPtr)3]);
        platform.VisibleHandles.UnionWith(platform.TaskbarHandles);
        platform.QueueKeyboardInstall(success: true);

        var machineLockdown = new FakeMachineLockdownService
        {
            ApplyResult = KioskMachineLockdownResult.AlreadyApplied("configured"),
            RemoveResult = KioskMachineLockdownResult.Removed("removed")
        };

        var service = CreateService(platform, machineLockdown);

        await service.ActivateAsync();

        Assert.True(service.Status.TaskbarHidden);
        Assert.True(service.Status.KeyboardFilterConfigured);
        Assert.Equal(3, platform.HiddenHandles.Count);
        Assert.All(platform.TaskbarHandles, handle => Assert.Contains(handle, platform.HiddenHandles));

        await service.DeactivateAsync();

        Assert.False(service.IsActive);
        Assert.All(platform.TaskbarHandles, handle => Assert.Contains(handle, platform.ShownHandles));
    }

    [Fact]
    public async Task EnforceNow_Reinstalls_KeyboardHook_After_ActivationFailure()
    {
        var platform = new FakeKioskPlatform();
        platform.QueueKeyboardInstall(success: false);
        platform.QueueKeyboardInstall(success: true);

        var service = CreateService(platform, new FakeMachineLockdownService
        {
            ApplyResult = KioskMachineLockdownResult.AlreadyApplied("configured")
        });

        await service.ActivateAsync();
        Assert.False(service.Status.KeyboardHookInstalled);

        service.EnforceNow();

        Assert.True(service.Status.KeyboardHookInstalled);
        Assert.Equal("Keyboard hook", service.Status.LastSelfHealAction);
        Assert.Equal("Reinstalled", service.Status.LastSelfHealResult);
        Assert.Equal(2, platform.InstallKeyboardHookCalls);
    }

    [Fact]
    public async Task EnforceNow_Rehides_Taskbars_When_They_Reappear()
    {
        var platform = new FakeKioskPlatform();
        platform.QueueKeyboardInstall(success: true);
        platform.TaskbarHandles.AddRange([(IntPtr)1, (IntPtr)2]);
        platform.VisibleHandles.UnionWith(platform.TaskbarHandles);

        var service = CreateService(platform, new FakeMachineLockdownService
        {
            ApplyResult = KioskMachineLockdownResult.AlreadyApplied("configured")
        });

        await service.ActivateAsync();
        platform.VisibleHandles.UnionWith(platform.TaskbarHandles);

        service.EnforceNow();

        Assert.True(service.Status.TaskbarHidden);
        Assert.Equal("Taskbar", service.Status.LastSelfHealAction);
        Assert.Equal("Hidden again", service.Status.LastSelfHealResult);
        Assert.DoesNotContain((IntPtr)1, platform.VisibleHandles);
        Assert.DoesNotContain((IntPtr)2, platform.VisibleHandles);
    }

    [Fact]
    public async Task ActivateAsync_Reports_Unsupported_KeyboardFilter()
    {
        var platform = new FakeKioskPlatform();
        platform.QueueKeyboardInstall(success: true);

        var service = CreateService(platform, new FakeMachineLockdownService
        {
            ApplyResult = KioskMachineLockdownResult.Unsupported("unsupported")
        });

        await service.ActivateAsync();

        Assert.True(service.IsActive);
        Assert.False(service.Status.KeyboardFilterSupported);
        Assert.False(service.Status.KeyboardFilterConfigured);
        Assert.False(string.IsNullOrWhiteSpace(service.Status.LimitedProtectionMessage));
    }

    [Fact]
    public async Task ActivateAsync_Reports_RestartRequired_KeyboardFilter()
    {
        var platform = new FakeKioskPlatform();
        platform.QueueKeyboardInstall(success: true);

        var service = CreateService(platform, new FakeMachineLockdownService
        {
            ApplyResult = KioskMachineLockdownResult.RestartPending("restart")
        });

        await service.ActivateAsync();

        Assert.True(service.Status.KeyboardFilterSupported);
        Assert.False(service.Status.KeyboardFilterConfigured);
        Assert.True(service.Status.KeyboardFilterRestartRequired);
        Assert.False(string.IsNullOrWhiteSpace(service.Status.LimitedProtectionMessage));
    }

    [Fact]
    public async Task ActivateAsync_Reports_UacCancellation_KeyboardFilter()
    {
        var platform = new FakeKioskPlatform();
        platform.QueueKeyboardInstall(success: true);

        var service = CreateService(platform, new FakeMachineLockdownService
        {
            ApplyResult = KioskMachineLockdownResult.Cancelled("cancelled")
        });

        await service.ActivateAsync();

        Assert.True(service.IsActive);
        Assert.True(service.Status.KeyboardFilterSupported);
        Assert.False(service.Status.KeyboardFilterConfigured);
        Assert.False(string.IsNullOrWhiteSpace(service.Status.LimitedProtectionMessage));
    }

    [Fact]
    public async Task DeactivateAsync_Keeps_Kiosk_Active_When_Machine_Removal_Is_Cancelled()
    {
        var platform = new FakeKioskPlatform();
        platform.QueueKeyboardInstall(success: true);
        platform.TaskbarHandles.AddRange([(IntPtr)1, (IntPtr)2]);
        platform.VisibleHandles.UnionWith(platform.TaskbarHandles);

        var service = CreateService(platform, new FakeMachineLockdownService
        {
            ApplyResult = KioskMachineLockdownResult.AlreadyApplied("configured"),
            RemoveResult = KioskMachineLockdownResult.Cancelled("cancelled")
        });

        await service.ActivateAsync();
        var result = await service.DeactivateAsync();

        Assert.Equal(KioskMachineLockdownState.Cancelled, result.State);
        Assert.True(service.IsActive);
        Assert.True(service.Status.KeyboardFilterConfigured);
        Assert.True(service.Status.TaskbarHidden);
        Assert.Empty(platform.ShownHandles);
    }

    [Theory]
    [InlineData(0x5B, false, false, false, true)]  // Left Win
    [InlineData(0x09, true, false, false, true)]   // Alt+Tab
    [InlineData(0x73, true, false, false, true)]   // Alt+F4
    [InlineData(0x1B, false, true, false, true)]   // Ctrl+Esc
    [InlineData(0x1B, false, true, true, true)]    // Ctrl+Shift+Esc
    [InlineData(0x41, false, false, false, false)] // A
    public void ShouldBlockShortcut_Matches_Kiosk_Policy(int vkCode, bool altPressed, bool ctrlPressed, bool shiftPressed, bool expected)
    {
        var result = KioskService.ShouldBlockShortcut(vkCode, altPressed, ctrlPressed, shiftPressed);

        Assert.Equal(expected, result);
    }

    private static KioskService CreateService(
        FakeKioskPlatform platform,
        FakeMachineLockdownService machineLockdown) =>
        new(
            NullLogger<KioskService>.Instance,
            platform,
            new FakeConfiguredAppMatcher(),
            new KioskForegroundPolicy(),
            machineLockdown);

    private sealed class FakeConfiguredAppMatcher : IKioskConfiguredAppMatcher
    {
        public bool IsConfiguredAppWindow(KioskWindowSnapshot snapshot, IReadOnlyCollection<AppProcessTarget> targets) => false;
    }

    private sealed class FakeMachineLockdownService : IKioskMachineLockdownService
    {
        public KioskMachineLockdownResult ApplyResult { get; set; } =
            KioskMachineLockdownResult.NotConfigured("not configured");

        public KioskMachineLockdownResult RemoveResult { get; set; } =
            KioskMachineLockdownResult.AlreadyRemoved("already removed");

        public KioskMachineLockdownResult QueryResult { get; set; } =
            KioskMachineLockdownResult.NotConfigured("not configured");

        public Task<KioskMachineLockdownResult> ApplyAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(ApplyResult);

        public Task<KioskMachineLockdownResult> RemoveAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(RemoveResult);

        public Task<KioskMachineLockdownResult> QueryAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(QueryResult);
    }

    private sealed class FakeKioskPlatform : IKioskPlatform
    {
        private readonly Queue<bool> _installResults = new();
        private int _nextHookHandle = 100;

        public List<IntPtr> TaskbarHandles { get; } = [];
        public HashSet<IntPtr> VisibleHandles { get; } = [];
        public List<IntPtr> HiddenHandles { get; } = [];
        public List<IntPtr> ShownHandles { get; } = [];
        public int InstallKeyboardHookCalls { get; private set; }
        public KioskWindowSnapshot? ForegroundWindow { get; set; }

        public void QueueKeyboardInstall(bool success) => _installResults.Enqueue(success);

        public bool TryInstallKeyboardHook(KioskKeyboardProc callback, out IntPtr hookHandle)
        {
            InstallKeyboardHookCalls++;

            var success = _installResults.Count == 0 || _installResults.Dequeue();
            hookHandle = success ? new IntPtr(_nextHookHandle++) : IntPtr.Zero;
            return success;
        }

        public void RemoveKeyboardHook(IntPtr hookHandle)
        {
        }

        public IReadOnlyList<IntPtr> GetTaskbarHandles() => TaskbarHandles;

        public bool HideWindow(IntPtr hwnd)
        {
            HiddenHandles.Add(hwnd);
            VisibleHandles.Remove(hwnd);
            return true;
        }

        public bool ShowWindow(IntPtr hwnd)
        {
            ShownHandles.Add(hwnd);
            VisibleHandles.Add(hwnd);
            return true;
        }

        public bool IsWindowVisible(IntPtr hwnd) => VisibleHandles.Contains(hwnd);

        public KioskWindowSnapshot? TryGetForegroundWindow() => ForegroundWindow;

        public bool RestoreWindow(IntPtr hwnd) => true;

        public bool SetForegroundWindow(IntPtr hwnd) => true;

        public bool BringWindowToTop(IntPtr hwnd) => true;

        public bool SetWindowTopmost(IntPtr hwnd, bool activate) => true;
    }
}
