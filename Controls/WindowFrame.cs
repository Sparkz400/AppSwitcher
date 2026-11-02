namespace AppSwitcher.Controls;

/// <summary>Keep custom chrome inside the current monitor's work area when maximized.</summary>
public sealed class WindowFrame : Border
{
    private Window? _window;

    public WindowFrame()
    {
        Loaded += (_, _) =>
        {
            _window = Window.GetWindow(this);
            if (_window == null) return;
            _window.StateChanged += UpdateFrame;
            _window.LocationChanged += UpdateFrame;
            _window.SizeChanged += UpdateFrame;
            _window.DpiChanged += UpdateFrame;
            UpdateFrame(this, EventArgs.Empty);
        };
        Unloaded += (_, _) =>
        {
            if (_window == null) return;
            _window.StateChanged -= UpdateFrame;
            _window.LocationChanged -= UpdateFrame;
            _window.SizeChanged -= UpdateFrame;
            _window.DpiChanged -= UpdateFrame;
            _window = null;
        };
    }

    private void UpdateFrame(object? sender, EventArgs e)
    {
        if (_window == null || PresentationSource.FromVisual(_window) == null) return;
        var padding = new Thickness();
        if (_window.WindowState == WindowState.Maximized)
        {
            // Windows places its invisible resize frame beyond the work area.
            // Measure that actual overflow instead of assuming a fixed frame/DPI.
            var topLeft = _window.PointToScreen(new Point());
            var bottomRight = _window.PointToScreen(new Point(_window.ActualWidth, _window.ActualHeight));
            var center = new Point((topLeft.X + bottomRight.X) / 2, (topLeft.Y + bottomRight.Y) / 2);
            var area = MonitorHelper.GetMonitorWorkAreas().MinBy(area =>
                Math.Pow(Math.Clamp(center.X, area.Left, area.Right) - center.X, 2)
                + Math.Pow(Math.Clamp(center.Y, area.Top, area.Bottom) - center.Y, 2));
            var dpi = VisualTreeHelper.GetDpi(_window);
            padding = new Thickness(Math.Max(0, area.Left - topLeft.X) / dpi.DpiScaleX,
                Math.Max(0, area.Top - topLeft.Y) / dpi.DpiScaleY,
                Math.Max(0, bottomRight.X - area.Right) / dpi.DpiScaleX,
                Math.Max(0, bottomRight.Y - area.Bottom) / dpi.DpiScaleY);
        }
        Padding = padding;
    }
}
