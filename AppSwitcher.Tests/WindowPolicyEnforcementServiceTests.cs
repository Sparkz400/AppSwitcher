using Microsoft.Extensions.Logging.Abstractions;

namespace AppSwitcher.Tests;

public class WindowPolicyEnforcementServiceTests
{
    [Fact]
    public void ForcedIdentitySurvivesReorderAndRecoversFromDisconnectWithoutChangingConfiguration()
    {
        var resolver = new FakeWindowHandleResolver();
        var platform = CreatePlatform();
        platform.Monitors[0] = platform.Monitors[0] with { DeviceId = "A" };
        platform.Monitors[1] = platform.Monitors[1] with { DeviceId = "B" };
        var hwnd = (IntPtr)110;
        var target = new AppProcessTarget(@"C:\Apps\worker.exe");
        resolver.SetHandle(target, hwnd);
        platform.Windows[hwnd] = new FakeWindow { Rect = NativeRect.FromPositionAndSize(50, 50, 800, 600), Style = new WindowStyleState(0x00CF0000, 0) };
        using var service = CreateService(resolver, platform);
        var app = new AppItem { Name = "Worker", Path = target.Path, ForcedMonitor = 1, ForcedMonitorId = "B", PlacementMode = WindowPlacementMode.Maximize };
        service.EnforceNow([app]);
        Assert.Equal(platform.Monitors[1].WorkArea, platform.Windows[hwnd].Rect);
        var secondary = platform.Monitors[1] with { Index = 1 };
        var primary = platform.Monitors[0] with { Index = 2 };
        platform.Monitors = [secondary, primary];
        service.EnforceNow([app]);
        Assert.Equal(secondary.WorkArea, platform.Windows[hwnd].Rect);
        platform.Monitors = [primary];
        service.EnforceNow([app]);
        Assert.Equal(primary.WorkArea, platform.Windows[hwnd].Rect);
        platform.Monitors = [primary, secondary];
        service.EnforceNow([app]);
        Assert.Equal(secondary.WorkArea, platform.Windows[hwnd].Rect);
        Assert.Equal("B", app.ForcedMonitorId);
        Assert.Equal(1, app.ForcedMonitor);
    }

    [Fact]
    public void EnforceNow_MaximizePolicy_Restores_Moves_And_Maximizes_Window()
    {
        var resolver = new FakeWindowHandleResolver();
        var platform = CreatePlatform();
        var hwnd = (IntPtr)101;
        var target = new AppProcessTarget(@"C:\Apps\worker.exe");
        resolver.SetHandle(target, hwnd);
        platform.Windows[hwnd] = new FakeWindow
        {
            Rect = NativeRect.FromPositionAndSize(50, 50, 1000, 600),
            IsMinimized = true,
            Style = new WindowStyleState(0x00CF0000, 0)
        };

        var service = CreateService(resolver, platform);
        var app = new AppItem
        {
            Name = "Worker",
            Path = target.Path,
            PlacementMode = WindowPlacementMode.Maximize,
            ForcedMonitor = 2
        };

        service.EnforceNow([app]);

        Assert.Contains("Restore:101", platform.Calls);
        Assert.Contains("Maximize:101", platform.Calls);
        Assert.Equal(platform.Monitors[1].WorkArea, platform.Windows[hwnd].Rect);
        Assert.True(platform.Windows[hwnd].IsMaximized);
    }

    [Fact]
    public void EnforceNow_FullscreenPolicy_Restores_Original_Style_And_Rect_When_Disabled()
    {
        var resolver = new FakeWindowHandleResolver();
        var platform = CreatePlatform();
        var hwnd = (IntPtr)102;
        var target = new AppProcessTarget(@"C:\Apps\worker.exe");
        var originalRect = NativeRect.FromPositionAndSize(100, 120, 900, 700);
        var originalStyle = new WindowStyleState(0x00CF0000, 0x00000100);
        resolver.SetHandle(target, hwnd);
        platform.Windows[hwnd] = new FakeWindow
        {
            Rect = originalRect,
            Style = originalStyle
        };

        var service = CreateService(resolver, platform);
        var fullscreenApp = new AppItem
        {
            Name = "Worker",
            Path = target.Path,
            PlacementMode = WindowPlacementMode.Fullscreen
        };

        service.EnforceNow([fullscreenApp]);

        Assert.Equal(platform.Monitors[0].Bounds, platform.Windows[hwnd].Rect);
        Assert.NotEqual(originalStyle, platform.Windows[hwnd].Style);

        var plainApp = new AppItem
        {
            Name = "Worker",
            Path = target.Path,
            PlacementMode = WindowPlacementMode.None
        };

        service.EnforceNow([plainApp]);

        Assert.Equal(originalRect, platform.Windows[hwnd].Rect);
        Assert.Equal(originalStyle, platform.Windows[hwnd].Style);
    }

    [Fact]
    public void EnforceNow_Reasserts_Topmost_Without_Reapplying_Geometry()
    {
        var resolver = new FakeWindowHandleResolver();
        var platform = CreatePlatform();
        var hwnd = (IntPtr)103;
        var target = new AppProcessTarget(@"C:\Apps\worker.exe");
        resolver.SetHandle(target, hwnd);
        platform.Windows[hwnd] = new FakeWindow
        {
            Rect = NativeRect.FromPositionAndSize(10, 10, 800, 600),
            Style = new WindowStyleState(0x00CF0000, 0)
        };

        var service = CreateService(resolver, platform);
        var app = new AppItem
        {
            Name = "Worker",
            Path = target.Path,
            AlwaysOnTop = true
        };

        service.EnforceNow([app]);
        Assert.True(platform.Windows[hwnd].IsTopmost);

        var setRectCallsAfterFirstPass = platform.Calls.Count(call => call.StartsWith("SetRect:", StringComparison.Ordinal));
        platform.Windows[hwnd].IsTopmost = false;

        service.EnforceNow([app]);

        Assert.True(platform.Windows[hwnd].IsTopmost);
        Assert.Equal(setRectCallsAfterFirstPass, platform.Calls.Count(call => call.StartsWith("SetRect:", StringComparison.Ordinal)));
        Assert.Equal(2, platform.Calls.Count(call => call == "Topmost:103"));
    }

    [Fact]
    public void EnforceNow_Does_Not_Reapply_Geometry_After_Manual_Move_When_Continuous_Mode_Is_Off()
    {
        var resolver = new FakeWindowHandleResolver();
        var platform = CreatePlatform();
        var hwnd = (IntPtr)104;
        var target = new AppProcessTarget(@"C:\Apps\worker.exe");
        resolver.SetHandle(target, hwnd);
        platform.Windows[hwnd] = new FakeWindow
        {
            Rect = NativeRect.FromPositionAndSize(100, 100, 900, 600),
            Style = new WindowStyleState(0x00CF0000, 0)
        };

        var service = CreateService(resolver, platform);
        var app = new AppItem
        {
            Name = "Worker",
            Path = target.Path,
            ForcedMonitor = 2,
            ReapplyPlacementContinuously = false
        };

        service.EnforceNow([app]);
        var initialSetRectCalls = platform.Calls.Count(call => call.StartsWith("SetRect:", StringComparison.Ordinal));
        var manualRect = NativeRect.FromPositionAndSize(2222, 333, 700, 500);
        platform.Windows[hwnd].Rect = manualRect;

        service.EnforceNow([app]);

        Assert.Equal(initialSetRectCalls, platform.Calls.Count(call => call.StartsWith("SetRect:", StringComparison.Ordinal)));
        Assert.Equal(manualRect, platform.Windows[hwnd].Rect);
    }

    [Fact]
    public void EnforceNow_Falls_Back_To_Primary_Monitor_When_Configured_Monitor_Is_Missing()
    {
        var resolver = new FakeWindowHandleResolver();
        var platform = CreatePlatform();
        var hwnd = (IntPtr)105;
        var target = new AppProcessTarget(@"C:\Apps\worker.exe");
        resolver.SetHandle(target, hwnd);
        platform.Windows[hwnd] = new FakeWindow
        {
            Rect = NativeRect.FromPositionAndSize(2100, 100, 800, 600),
            Style = new WindowStyleState(0x00CF0000, 0)
        };

        var service = CreateService(resolver, platform);
        var app = new AppItem
        {
            Name = "Worker",
            Path = target.Path,
            ForcedMonitor = 9
        };

        service.EnforceNow([app]);

        Assert.Equal(NativeRect.FromPositionAndSize(560, 220, 800, 600), platform.Windows[hwnd].Rect);
    }

    private static WindowPolicyEnforcementService CreateService(
        FakeWindowHandleResolver resolver,
        FakeWindowPolicyPlatform platform) =>
        new(
            NullLogger<WindowPolicyEnforcementService>.Instance,
            resolver,
            platform);

    private static FakeWindowPolicyPlatform CreatePlatform() =>
        new()
        {
            Monitors =
            [
                new NativeMonitorInfo(1, NativeRect.FromPositionAndSize(0, 0, 1920, 1080), NativeRect.FromPositionAndSize(0, 0, 1920, 1040), true),
                new NativeMonitorInfo(2, NativeRect.FromPositionAndSize(1920, 0, 1920, 1080), NativeRect.FromPositionAndSize(1920, 0, 1920, 1040), false)
            ]
        };

    private sealed class FakeWindowHandleResolver : IWindowHandleResolver
    {
        private readonly Dictionary<string, IntPtr> _handles = new(StringComparer.Ordinal);

        public void SetHandle(AppProcessTarget target, IntPtr hwnd) => _handles[target.Key] = hwnd;

        public IntPtr GetMainWindowHandle(AppProcessTarget target) =>
            _handles.TryGetValue(target.Key, out var hwnd) ? hwnd : IntPtr.Zero;
    }

    private sealed class FakeWindowPolicyPlatform : IWindowPolicyPlatform
    {
        public Dictionary<IntPtr, FakeWindow> Windows { get; } = [];
        public List<NativeMonitorInfo> Monitors { get; set; } = [];
        public List<string> Calls { get; } = [];

        public bool IsWindow(IntPtr hwnd) => Windows.ContainsKey(hwnd);

        public bool IsWindowMinimized(IntPtr hwnd) => Windows[hwnd].IsMinimized;

        public bool RestoreWindow(IntPtr hwnd)
        {
            Calls.Add($"Restore:{hwnd}");
            Windows[hwnd].IsMinimized = false;
            Windows[hwnd].IsMaximized = false;
            return true;
        }

        public bool MaximizeWindow(IntPtr hwnd)
        {
            Calls.Add($"Maximize:{hwnd}");
            var monitor = ResolveMonitorForWindow(hwnd);
            Windows[hwnd].Rect = monitor.WorkArea;
            Windows[hwnd].IsMaximized = true;
            Windows[hwnd].IsMinimized = false;
            return true;
        }

        public bool TryGetWindowRect(IntPtr hwnd, out NativeRect rect)
        {
            if (Windows.TryGetValue(hwnd, out var window))
            {
                rect = window.Rect;
                return true;
            }

            rect = default;
            return false;
        }

        public bool SetWindowRect(IntPtr hwnd, NativeRect rect, bool activate)
        {
            Calls.Add($"SetRect:{hwnd}:{rect.Left},{rect.Top},{rect.Width},{rect.Height}");
            Windows[hwnd].Rect = rect;
            Windows[hwnd].IsMinimized = false;
            Windows[hwnd].IsMaximized = false;
            return true;
        }

        public bool IsTopmost(IntPtr hwnd) => Windows[hwnd].IsTopmost;

        public bool SetTopmost(IntPtr hwnd, bool topmost, bool activate)
        {
            Calls.Add($"{(topmost ? "Topmost" : "NotTopmost")}:{hwnd}");
            Windows[hwnd].IsTopmost = topmost;
            return true;
        }

        public bool TryGetWindowStyle(IntPtr hwnd, out WindowStyleState style)
        {
            if (Windows.TryGetValue(hwnd, out var window))
            {
                style = window.Style;
                return true;
            }

            style = default;
            return false;
        }

        public bool SetWindowStyle(IntPtr hwnd, WindowStyleState style)
        {
            Calls.Add($"SetStyle:{hwnd}");
            Windows[hwnd].Style = style;
            return true;
        }

        public IReadOnlyList<NativeMonitorInfo> GetMonitors() => Monitors;

        public int GetCurrentMonitorIndex(IntPtr hwnd) => ResolveMonitorForWindow(hwnd).Index;

        private NativeMonitorInfo ResolveMonitorForWindow(IntPtr hwnd)
        {
            var window = Windows[hwnd];
            var centerX = window.Rect.Left + (window.Rect.Width / 2);
            var centerY = window.Rect.Top + (window.Rect.Height / 2);

            foreach (var monitor in Monitors)
            {
                if (centerX >= monitor.Bounds.Left
                    && centerX < monitor.Bounds.Right
                    && centerY >= monitor.Bounds.Top
                    && centerY < monitor.Bounds.Bottom)
                {
                    return monitor;
                }
            }

            return Monitors.First(monitor => monitor.IsPrimary);
        }
    }

    private sealed class FakeWindow
    {
        public NativeRect Rect { get; set; }
        public bool IsMinimized { get; set; }
        public bool IsMaximized { get; set; }
        public bool IsTopmost { get; set; }
        public WindowStyleState Style { get; set; }
    }
}
