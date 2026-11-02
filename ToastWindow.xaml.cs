namespace AppSwitcher;

public partial class ToastWindow : Window
{
    private readonly DispatcherTimer _dismissTimer;

    public ToastWindow(string message, NotificationType type)
    {
        InitializeComponent();
        MessageText.Text = message;
        IconText.Text = LocalizationService.T(type switch
        {
            NotificationType.Error => "UI_Error",
            NotificationType.Warning => "UI_Warning",
            _ => "UI_Notification"
        });
        if (type != NotificationType.Info)
            ToastBorder.BorderBrush = (Brush)FindResource(type == NotificationType.Error ? "DangerFg" : "WarningFg");

        Loaded += (_, _) =>
        {
            var workArea = SystemParameters.WorkArea;
            Left = workArea.Right - ActualWidth - 16;
            Top = workArea.Bottom - ActualHeight - 16;
        };
        _dismissTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(type == NotificationType.Info ? 8 : 15) };
        _dismissTimer.Tick += (_, _) => Close();
        Closed += (_, _) => _dismissTimer.Stop();
        MouseEnter += (_, _) => _dismissTimer.Stop();
        MouseLeave += (_, _) => _dismissTimer.Start();
        GotKeyboardFocus += (_, _) => _dismissTimer.Stop();
        LostKeyboardFocus += (_, _) => { if (!IsKeyboardFocusWithin) _dismissTimer.Start(); };
        _dismissTimer.Start();
    }

    private void Dismiss_Click(object sender, RoutedEventArgs e) => Close();
}
