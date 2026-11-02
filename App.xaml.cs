using System.Runtime.InteropServices;
using System.Threading;

namespace AppSwitcher;

public partial class App : Application
{
    public App() => AppRendering.Initialize();

    private SingleInstanceCoordinator? _instance;
    private ServiceProvider? _serviceProvider;
    private bool _activationPending;
    private bool _ready;
    private bool _restartRequested;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;

        try
        {
            if (KioskMachineLockdownCommand.IsMachineCommand(e.Args))
            {
                var exitCode = await KioskMachineLockdownCommand.RunAsync(e.Args);
                Shutdown(exitCode);
                return;
            }

            var restarting = await RestartHandoff.WaitForPreviousAsync(e.Args);
            if (!await EnsureSingleInstanceAsync())
                return;

            _serviceProvider = BuildServiceProvider();
            _serviceProvider.GetRequiredService<ThemeService>().ApplyTheme("Dark");
            await StartApplicationAsync(e.Args, restarting);
            _ready = true;
            if (_activationPending) ActivateExistingWindow();
        }
        catch (ConfigurationLoadException ex) when (_serviceProvider != null)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var recovery = new ConfigurationRecoveryWindow(_serviceProvider.GetRequiredService<ConfigService>(),
                _serviceProvider.GetRequiredService<SecurityService>(), ex.InnerException?.Message ?? ex.Message);
            recovery.ShowDialog();
            Shutdown(recovery.Restored ? 0 : -1);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                LocalizationService.T("Msg_StartupFailed", Environment.NewLine, ex.Message),
                "AppSwitcher",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    private async Task<bool> EnsureSingleInstanceAsync()
    {
        var testScope = AppPaths.IsolatedDataDirectory == null ? null : "AppSwitcher.Test." + Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(AppPaths.IsolatedDataDirectory)))[..24];
        _instance = new SingleInstanceCoordinator(testScope);
        if (_instance.IsPrimary)
        {
            _instance.Listen(() => Dispatcher.BeginInvoke(ActivateExistingWindow));
            return true;
        }

        if (!await _instance.ActivateExistingAsync())
            MessageBox.Show(LocalizationService.T("Msg_ActivationFailed"), "AppSwitcher",
                MessageBoxButton.OK, MessageBoxImage.Information);
        Shutdown();
        return false;
    }

    private void ActivateExistingWindow()
    {
        if (!_ready) { _activationPending = true; return; }
        _activationPending = false;
        var target = Windows.OfType<Window>().LastOrDefault(window => window != MainWindow
            && window is not ToastWindow && window.IsVisible && window.IsEnabled) ?? MainWindow;
        if (target == null) return;
        if (target.WindowState == WindowState.Minimized) target.WindowState = WindowState.Normal;
        target.Show();
        target.Activate();
        target.Focus();
        if (target == MainWindow && target.DataContext is MainViewModel vm) vm.IsMenuOpen = true;
    }

    internal async Task RestartAsync()
    {
        if (_restartRequested || _serviceProvider?.GetRequiredService<MainViewModel>() is not { CanAdminister: true } vm) return;
        await RestartHandoff.StartReplacementAsync();
        _restartRequested = true;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await vm.ShutdownAsync(deadline.Token, stopManagedApps: false).WaitAsync(deadline.Token); }
        finally { Shutdown(); }
    }

    private static ServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();

        services.AddLogging(LoggingConfig.Configure);

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ConfigService>();
        services.AddSingleton<BrowserBridgeService>();
        services.AddSingleton<ProcessHelper>();
        services.AddSingleton<AppReadinessService>();
        services.AddSingleton<MaintenanceService>();
        services.AddSingleton<IAppHookRunner, ProcessHookRunner>();
        services.AddSingleton<IAppLifecycleService, AppLifecycleService>();
        services.AddSingleton<IWindowHandleResolver>(sp => sp.GetRequiredService<ProcessHelper>());
        services.AddSingleton<IWindowPolicyPlatform, WindowsWindowPolicyPlatform>();
        services.AddSingleton<IKioskMachineLockdownService, ElevatedKioskMachineLockdownService>();
        services.AddSingleton<KioskService>();
        services.AddSingleton<IWatchdogCircuitStore, FileWatchdogCircuitStore>();
        services.AddSingleton<WatchdogService>();
        services.AddSingleton<NotificationService>();
        services.AddSingleton<SecurityService>();
        services.AddSingleton<ConfigDiffService>();
        services.AddSingleton<AuditLogService>();
        services.AddSingleton<ThemeService>();
        services.AddSingleton<LocalizationService>();
        services.AddSingleton<SessionTimeoutService>();
        services.AddSingleton<HealthCheckService>();
        services.AddSingleton<ResourceMonitorService>();
        services.AddSingleton<ScreenLayoutService>();
        services.AddSingleton<WindowPolicyEnforcementService>();
        services.AddSingleton<ConfigValidationService>();
        services.AddSingleton<CrashHistoryService>();
        services.AddSingleton<AppVersionService>();
        services.AddSingleton<SchedulerService>();
        services.AddSingleton(sp => new MainViewModel(
            sp.GetRequiredService<ConfigService>(),
            sp.GetRequiredService<ProcessHelper>(),
            sp.GetRequiredService<IAppLifecycleService>(),
            sp.GetRequiredService<KioskService>(),
            sp.GetRequiredService<WatchdogService>(),
            sp.GetRequiredService<NotificationService>(),
            sp.GetRequiredService<SecurityService>(),
            sp.GetRequiredService<ThemeService>(),
            sp.GetRequiredService<LocalizationService>(),
            sp.GetRequiredService<SchedulerService>(),
            sp.GetRequiredService<ConfigDiffService>(),
            sp.GetRequiredService<AuditLogService>(),
            sp.GetRequiredService<SessionTimeoutService>(),
            sp.GetRequiredService<HealthCheckService>(),
            sp.GetRequiredService<ResourceMonitorService>(),
            sp.GetRequiredService<ScreenLayoutService>(),
            sp.GetRequiredService<WindowPolicyEnforcementService>(),
            sp.GetRequiredService<ConfigValidationService>(),
            sp.GetRequiredService<CrashHistoryService>(),
            sp.GetRequiredService<AppVersionService>(),
            sp.GetRequiredService<ILogger<MainViewModel>>(),
            sp.GetRequiredService<MaintenanceService>(),
            sp.GetRequiredService<AppReadinessService>()));
        services.AddSingleton<MainWindow>();

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
    }

    private async Task StartApplicationAsync(string[] args, bool restarting = false)
    {
        ArgumentNullException.ThrowIfNull(_serviceProvider);

        var mainVm = _serviceProvider.GetRequiredService<MainViewModel>();
        await mainVm.InitializeAsync();

        if (args.Contains("--reset-pin", StringComparer.OrdinalIgnoreCase))
        {
            mainVm.Settings.AdminPasswordHash = "";
            await mainVm.SaveConfigAsync();
            MessageBox.Show(LocalizationService.T("Msg_PinReset"), "AppSwitcher",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.DataContext = mainVm;
        var positionCorrected = PositionWindow(mainWindow, mainVm.Settings);

        MainWindow = mainWindow;
        mainWindow.Show();
        if (!restarting) mainVm.StartAutoLaunch();

        if (positionCorrected)
            await mainVm.SaveConfigAsync();

        if (!restarting && _serviceProvider.GetRequiredService<ConfigService>().IsFirstRun)
            await mainVm.OpenSettingsCommand.ExecuteAsync(null);
    }

    /// <summary>
    /// Positions the window based on PreferredMonitor setting or saved PosX/PosY.
    /// Falls back to primary monitor if the target is invalid.
    /// </summary>
    private static bool PositionWindow(Window window, AppSettings settings)
    {
        var launcherContent = (FrameworkElement)window.Content;
        launcherContent.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var launcherSize = launcherContent.DesiredSize;
        if (settings.PreferredMonitor > 0 || !string.IsNullOrEmpty(settings.PreferredMonitorId))
        {
            var display = MonitorHelper.ResolveDisplay(settings.PreferredMonitor, settings.PreferredMonitorId, MonitorHelper.GetDisplays());
            if (display != null)
            {
                var workArea = display.WpfWorkArea;
                window.Left = workArea.Left + (workArea.Width - launcherSize.Width) / 2;
                window.Top = workArea.Top + Math.Max(0, Math.Min(50, workArea.Height - launcherSize.Height));
                return false;
            }
        }

        // Use saved position, validate it's on a visible monitor
        var posX = settings.PosX;
        var posY = settings.PosY;
        var positionCorrected = false;

        if (!IsPositionOnScreen(posX, posY))
        {
            var workArea = MonitorHelper.GetCursorWorkArea();
            posX = workArea.Left + 100;
            posY = workArea.Top + 100;
            settings.PosX = posX;
            settings.PosY = posY;
            positionCorrected = true;
        }

        var area = MonitorHelper.GetMonitorWorkAreas().FirstOrDefault(rect => rect.Contains(new Point(posX, posY)));
        if (area.Width > 0)
        {
            posX = Math.Clamp(posX, area.Left, Math.Max(area.Left, area.Right - launcherSize.Width));
            posY = Math.Clamp(posY, area.Top, Math.Max(area.Top, area.Bottom - launcherSize.Height));
            positionCorrected |= posX != settings.PosX || posY != settings.PosY;
        }
        window.Left = settings.PosX = posX;
        window.Top = settings.PosY = posY;
        return positionCorrected;
    }

    /// <summary>
    /// Checks whether the given WPF position lands on an actual monitor
    /// by using MonitorFromPoint with MONITOR_DEFAULTTONULL.
    /// </summary>
    private static bool IsPositionOnScreen(double wpfX, double wpfY)
    {
        var hdc = GetDC(IntPtr.Zero);
        double dpiScale = GetDeviceCaps(hdc, 88 /* LOGPIXELSX */) / 96.0;
        ReleaseDC(IntPtr.Zero, hdc);

        int px = (int)(wpfX * dpiScale);
        int py = (int)(wpfY * dpiScale);

        const int MONITOR_DEFAULTTONULL = 0;
        var hMonitor = MonitorFromPoint(new POINT(px, py), MONITOR_DEFAULTTONULL);
        return hMonitor != IntPtr.Zero;
    }

    [LibraryImport("user32.dll")]
    private static partial IntPtr MonitorFromPoint(POINT pt, int flags);

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetDC(IntPtr hwnd);

    [LibraryImport("user32.dll")]
    private static partial int ReleaseDC(IntPtr hwnd, IntPtr hdc);

    [LibraryImport("gdi32.dll")]
    private static partial int GetDeviceCaps(IntPtr hdc, int index);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT(int x, int y)
    {
        public int X = x;
        public int Y = y;
    }

    private void OnSystemParametersChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SystemParameters.HighContrast)) return;
        _ = Dispatcher.BeginInvoke(() =>
        {
            if (_serviceProvider != null)
                _serviceProvider.GetRequiredService<ThemeService>().ApplyTheme(_serviceProvider.GetRequiredService<MainViewModel>().Settings.Theme);
        });
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
        if (_serviceProvider != null)
        {
            try
            {
                var mainVm = _serviceProvider.GetRequiredService<MainViewModel>();
                using var shutdownCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                mainVm.ShutdownAsync(shutdownCts.Token, stopManagedApps: !_restartRequested).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("Shutdown timed out before all managed apps stopped.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Shutdown error: {ex.Message}");
            }
        }

        _serviceProvider?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }
}

/// <summary>
/// Helper for enumerating monitor work areas via Win32 EnumDisplayMonitors.
/// Returns rectangles in physical pixels (suitable for WPF positioning after DPI conversion).
/// </summary>
public static partial class MonitorHelper
{
    private const int MonitorDefaultToNearest = 2;

    // EnumDisplayMonitors uses a delegate callback and must remain DllImport
    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [LibraryImport("user32.dll")]
    private static partial IntPtr MonitorFromPoint(POINT pt, int flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out POINT lpPoint);

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetDC(IntPtr hwnd);

    [LibraryImport("user32.dll")]
    private static partial int ReleaseDC(IntPtr hwnd, IntPtr hdc);

    [LibraryImport("gdi32.dll")]
    private static partial int GetDeviceCaps(IntPtr hdc, int index);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    /// <summary>
    /// Returns WPF-coordinate work areas for all connected monitors.
    /// </summary>
    public static List<Rect> GetMonitorWorkAreas()
        => GetDisplays().Select(display => display.WpfWorkArea).ToList();

    public static Rect GetCursorWorkArea()
    {
        if (!GetCursorPos(out var cursor))
            return GetMonitorWorkAreas().FirstOrDefault();

        var hMonitor = MonitorFromPoint(cursor, MonitorDefaultToNearest);
        if (hMonitor == IntPtr.Zero)
            return GetMonitorWorkAreas().FirstOrDefault();

        var info = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(hMonitor, ref info))
            return GetMonitorWorkAreas().FirstOrDefault();

        return ToWpfRect(info.rcWork, GetDpiScale());
    }

    private static Rect ToWpfRect(RECT rect, double dpiScale) =>
        new(
            rect.Left / dpiScale,
            rect.Top / dpiScale,
            (rect.Right - rect.Left) / dpiScale,
            (rect.Bottom - rect.Top) / dpiScale);

    private static double GetDpiScale()
    {
        var hdc = GetDC(IntPtr.Zero);
        try
        {
            return GetDeviceCaps(hdc, 88) / 96.0;
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, hdc);
        }
    }
}

public class BoolToThicknessConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return (value is bool b && b) ? new Thickness(3) : new Thickness(0);
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
