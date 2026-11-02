namespace AppSwitcher;

public partial class MainWindow : Window
{
    private MainViewModel _vm = null!;
    private LauncherShortcut? _shortcut;
    private AppSettings? _shortcutSettings;

    private long _lastCloseTicks;

    // Drag state
    private bool _isDragging;
    private Point _dragStartScreen;
    private double _windowStartX, _windowStartY;
    private bool _dragThresholdExceeded;
    private const double DragThreshold = 5.0;

    public MainWindow(SessionTimeoutService sessionTimeoutService)
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            _vm = (MainViewModel)DataContext;
            _vm.RegisterMainWindow(this);
            _shortcut = new LauncherShortcut(this, () =>
            {
                if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
                Activate();
                _vm.IsMenuOpen = true;
                Dispatcher.BeginInvoke(() => QuickFilterBox.Focus(), DispatcherPriority.Input);
            });
            _vm.PropertyChanged += VmPropertyChanged;
            ObserveShortcutSettings();
        };
        Closed += (_, _) =>
        {
            _shortcut?.Dispose();
            if (_shortcutSettings != null) _shortcutSettings.PropertyChanged -= ShortcutSettingsChanged;
            if (_vm != null) _vm.PropertyChanged -= VmPropertyChanged;
        };

        PreviewMouseDown += (_, _) => sessionTimeoutService.ResetTimer();
        PreviewKeyDown += (_, e) =>
        {
            sessionTimeoutService.ResetTimer();
            if (e.Key is Key.Enter or Key.Space) _dragThresholdExceeded = false;
            if (e.Key == Key.Escape)
            {
                if (_vm.QuickFilter.Length > 0) _vm.QuickFilter = "";
                else { _vm.IsMenuOpen = false; MainGrid.Focus(); }
                e.Handled = true;
            }
        };
        PreviewTouchDown += (_, _) => sessionTimeoutService.ResetTimer();
        Closing += OnClosing;
        StateChanged += OnStateChanged;
    }

    private void OnPopupClosed(object sender, EventArgs e)
    {
        _lastCloseTicks = DateTime.Now.Ticks;
        if (_vm != null) _vm.QuickFilter = "";
    }

    private void VmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.Settings)) ObserveShortcutSettings();
    }
    private void ObserveShortcutSettings()
    {
        if (_shortcutSettings != null) _shortcutSettings.PropertyChanged -= ShortcutSettingsChanged;
        _shortcutSettings = _vm.Settings;
        _shortcutSettings.PropertyChanged += ShortcutSettingsChanged;
        ConfigureShortcut();
    }
    private void ShortcutSettingsChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AppSettings.GlobalShortcutEnabled) or nameof(AppSettings.Language)) ConfigureShortcut();
    }
    private void ConfigureShortcut() => _vm.ShortcutStatus = _shortcut?.Configure(_vm.Settings.GlobalShortcutEnabled) == false
        ? LocalizationService.T("Shortcut_Unavailable") : "";

    private void MainBtn_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;

        _isDragging = true;
        _dragThresholdExceeded = false;
        _dragStartScreen = PointToScreen(e.GetPosition(this));
        _windowStartX = Left;
        _windowStartY = Top;
    }

    private void MainBtn_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging || _vm.Settings.PositionLocked) return;

        var currentPos = PointToScreen(e.GetPosition(this));
        var delta = currentPos - _dragStartScreen;
        var transform = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        delta = transform.Transform(delta);
        var dx = delta.X;
        var dy = delta.Y;

        if (!_dragThresholdExceeded && Math.Sqrt(dx * dx + dy * dy) < DragThreshold)
            return;

        _dragThresholdExceeded = true;
        Left = _windowStartX + dx;
        Top = _windowStartY + dy;
    }

    private void MainBtn_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging) return;
        _isDragging = false;

        if (_dragThresholdExceeded)
        {
            _vm.Settings.PosX = Left;
            _vm.Settings.PosY = Top;
            _vm.SaveConfigInBackground();
        }
    }

    private void Launcher_Click(object sender, RoutedEventArgs e)
    {
        if (_dragThresholdExceeded || TimeSpan.FromTicks(DateTime.Now.Ticks - _lastCloseTicks).TotalMilliseconds < 300) return;
        _vm.ToggleMenuCommand.Execute(null);
    }

    private void OnPopupOpened(object sender, EventArgs e)
    {
        var area = MonitorHelper.GetMonitorWorkAreas().FirstOrDefault(rect => rect.Contains(new Point(Left, Top)));
        if (area.IsEmpty || area.Width == 0) area = SystemParameters.WorkArea;
        MenuSurface.MaxHeight = Math.Max(240, area.Height - 32);
        MenuSurface.MaxWidth = Math.Max(280, Math.Min(600, area.Width - 32));
        MenuSurface.MinWidth = Math.Min(360, MenuSurface.MaxWidth);
        MenuSurface.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
    }

    private void QuickFilter_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        var first = _vm.PinnedApps.FirstOrDefault() ?? _vm.GroupedApps.SelectMany(g => g.Apps).FirstOrDefault();
        if (first != null) _vm.LaunchAppCommand.Execute(first);
        e.Handled = true;
    }

    private void Actions_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { ContextMenu: { } menu } button) return;
        menu.PlacementTarget = button;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void ActionsMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu) return;
        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            if (item.Tag as string == "SignIn") item.Visibility = _vm.CanManageApps ? Visibility.Collapsed : Visibility.Visible;
            else if (item.Tag as string == "Manage") item.IsEnabled = _vm.CanManageApps;
            else if (item.Tag as string == "ManageRunning") item.IsEnabled = _vm.CanManageApps && menu.DataContext is AppItem { IsRunning: true };
        }
    }

    private void SignIn_Click(object sender, RoutedEventArgs e) => _vm.OpenSettingsCommand.Execute(null);

    private void LaunchApp_Click(object sender, RoutedEventArgs e) => _vm.LaunchAppCommand.Execute(((FrameworkElement)sender).DataContext);
    private void BringToFront_Click(object sender, RoutedEventArgs e) => _vm.BringToFrontCommand.Execute(((FrameworkElement)sender).DataContext);
    private void Pin_Click(object sender, RoutedEventArgs e) => _vm.TogglePinCommand.Execute(((FrameworkElement)sender).DataContext);
    private void Acknowledge_Click(object sender, RoutedEventArgs e) => _vm.AcknowledgeWatchdogCommand.Execute(((FrameworkElement)sender).DataContext);
    private void KillApp_Click(object sender, RoutedEventArgs e) => _vm.KillAppCommand.Execute(((FrameworkElement)sender).DataContext);
    private void StartGroup_Click(object sender, RoutedEventArgs e) => _vm.StartGroupCommand.Execute(((FrameworkElement)sender).DataContext);
    private void StopGroup_Click(object sender, RoutedEventArgs e) => _vm.StopGroupCommand.Execute(((FrameworkElement)sender).DataContext);

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_vm != null && _vm.ShouldPreventLauncherClose())
            e.Cancel = true;
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (_vm == null || !_vm.Settings.KioskMode) return;

        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
    }
}
