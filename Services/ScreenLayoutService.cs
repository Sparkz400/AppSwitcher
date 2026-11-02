namespace AppSwitcher.Services;

public class ScreenLayoutService
{
    private readonly ILogger<ScreenLayoutService> _logger;
    private readonly ProcessHelper _processHelper;

    public ScreenLayoutService(ILogger<ScreenLayoutService> logger, ProcessHelper processHelper)
    {
        _logger = logger;
        _processHelper = processHelper;
    }

    /// <summary>
    /// Captures current window positions for all running managed apps.
    /// </summary>
    public ScreenLayout CaptureLayout(string name, IReadOnlyList<AppItem> apps)
    {
        var layout = new ScreenLayout { Name = name, AppIds = apps.Where(a => a.IsRunning).Select(a => a.AppId).ToList() };
        var displays = MonitorHelper.GetDisplays();

        foreach (var app in apps)
        {
            if (!app.IsRunning || string.IsNullOrWhiteSpace(app.Path)) continue;

            var rect = _processHelper.GetWindowPosition(app.ProcessTarget);
            if (rect == null) continue;

            var r = rect.Value;
            var windowBounds = new Rect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
            layout.Positions.Add(CapturePosition(app, windowBounds, displays));

            _logger.LogDebug("Captured position for {Name}: ({L},{T}) {W}x{H}",
                app.Name, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
        }

        _logger.LogInformation("Layout '{Name}' captured with {Count} window positions", name, layout.Positions.Count);
        return layout;
    }

    internal static WindowPosition CapturePosition(AppItem app, Rect bounds, IReadOnlyList<DisplayInfo> displays)
    {
        var monitorIndex = FindMonitorIndex(bounds, displays.Select(display => display.WorkArea).ToArray());
        var display = displays.FirstOrDefault(display => display.Index == monitorIndex);
        var monitor = display?.WorkArea ?? new Rect();
        return new()
        {
            AppName = app.Name, AppId = app.AppId, Left = bounds.Left, Top = bounds.Top, Width = bounds.Width, Height = bounds.Height,
            MonitorIndex = monitorIndex, MonitorDeviceId = display?.DeviceId ?? "",
            SavedMonitorLeft = monitor.Left, SavedMonitorTop = monitor.Top, SavedMonitorWidth = monitor.Width, SavedMonitorHeight = monitor.Height
        };
    }

    /// <summary>
    /// Restores window positions from a saved layout.
    /// </summary>
    public void RestoreLayout(ScreenLayout layout, IReadOnlyList<AppItem> apps)
    {
        var monitors = MonitorHelper.GetDisplays();
        var appsByName = apps
            .Where(app => !string.IsNullOrWhiteSpace(app.Name))
            .GroupBy(app => app.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var pos in layout.Positions)
        {
            var app = !string.IsNullOrEmpty(pos.AppId) ? apps.FirstOrDefault(a => a.AppId == pos.AppId) : appsByName.GetValueOrDefault(pos.AppName);
            if (app == null || app.IsWebsite || !_processHelper.IsAppRunning(app.ProcessTarget)) continue;

            var bounds = ComputeRestoreBounds(pos, monitors);
            _processHelper.SetWindowPosition(
                app.ProcessTarget,
                (int)Math.Round(bounds.Left),
                (int)Math.Round(bounds.Top),
                (int)Math.Round(bounds.Width),
                (int)Math.Round(bounds.Height));
        }

        _logger.LogInformation("Layout '{Name}' restored", layout.Name);
    }

    internal static int FindMonitorIndex(Rect windowBounds, IReadOnlyList<Rect> monitors)
    {
        if (monitors.Count == 0)
            return 0;

        var windowCenter = new Point(
            windowBounds.Left + (windowBounds.Width / 2),
            windowBounds.Top + (windowBounds.Height / 2));

        for (var i = 0; i < monitors.Count; i++)
        {
            if (monitors[i].Contains(windowCenter))
                return i + 1;
        }

        return 0;
    }

    internal static Rect ComputeRestoreBounds(WindowPosition position, IReadOnlyList<Rect> monitors)
    {
        var width = Math.Max(1, position.Width);
        var height = Math.Max(1, position.Height);

        if (monitors.Count == 0)
            return new Rect(position.Left, position.Top, width, height);

        if (position.MonitorIndex > 0 && position.MonitorIndex <= monitors.Count)
        {
            var targetMonitor = monitors[position.MonitorIndex - 1];
            return MapBoundsToMonitor(position, targetMonitor, width, height);
        }

        var rawBounds = new Rect(position.Left, position.Top, width, height);
        var fallbackMonitorIndex = FindMonitorIndex(rawBounds, monitors);
        var fallbackMonitor = fallbackMonitorIndex > 0
            ? monitors[fallbackMonitorIndex - 1]
            : monitors[0];

        return ClampBounds(rawBounds, fallbackMonitor);
    }

    internal static Rect ComputeRestoreBounds(WindowPosition position, IReadOnlyList<DisplayInfo> displays)
    {
        if (string.IsNullOrEmpty(position.MonitorDeviceId))
            return ComputeRestoreBounds(position, displays.Select(display => display.WorkArea).ToArray());
        var target = MonitorHelper.ResolveDisplay(position.MonitorIndex, position.MonitorDeviceId, displays);
        return target == null ? new(position.Left, position.Top, Math.Max(1, position.Width), Math.Max(1, position.Height))
            : MapBoundsToMonitor(position, target.WorkArea, Math.Max(1, position.Width), Math.Max(1, position.Height));
    }

    private static Rect MapBoundsToMonitor(WindowPosition position, Rect targetMonitor, double width, double height)
    {
        if (position.SavedMonitorWidth <= 0 || position.SavedMonitorHeight <= 0)
            return ClampBounds(new Rect(position.Left, position.Top, width, height), targetMonitor);

        var savedWidthRange = Math.Max(0, position.SavedMonitorWidth - width);
        var savedHeightRange = Math.Max(0, position.SavedMonitorHeight - height);
        var targetWidth = Math.Min(width, targetMonitor.Width);
        var targetHeight = Math.Min(height, targetMonitor.Height);
        var targetWidthRange = Math.Max(0, targetMonitor.Width - targetWidth);
        var targetHeightRange = Math.Max(0, targetMonitor.Height - targetHeight);

        var xRatio = savedWidthRange == 0
            ? 0
            : Math.Clamp((position.Left - position.SavedMonitorLeft) / savedWidthRange, 0, 1);
        var yRatio = savedHeightRange == 0
            ? 0
            : Math.Clamp((position.Top - position.SavedMonitorTop) / savedHeightRange, 0, 1);

        var mappedBounds = new Rect(
            targetMonitor.Left + (xRatio * targetWidthRange),
            targetMonitor.Top + (yRatio * targetHeightRange),
            targetWidth,
            targetHeight);

        return ClampBounds(mappedBounds, targetMonitor);
    }

    private static Rect ClampBounds(Rect bounds, Rect monitor)
    {
        var width = Math.Min(bounds.Width, monitor.Width);
        var height = Math.Min(bounds.Height, monitor.Height);
        var left = Math.Clamp(bounds.Left, monitor.Left, monitor.Right - width);
        var top = Math.Clamp(bounds.Top, monitor.Top, monitor.Bottom - height);
        return new Rect(left, top, width, height);
    }
}
