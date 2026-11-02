namespace AppSwitcher;

public partial class PasswordDialog : Window
{
    private string _pin = "";
    private const int MaxPinLength = 8;

    public string Password => _pin;

    /// <summary>
    /// If true, dialog is in "set new PIN" mode and shows confirmation.
    /// </summary>
    public bool IsSetMode { get; set; }

    /// <summary>
    /// The access level determined after PIN verification (set by caller).
    /// </summary>
    public AccessLevel ResultAccessLevel { get; set; } = AccessLevel.None;

    private string? _firstPin;

    public PasswordDialog()
    {
        InitializeComponent();
        MaxHeight = Math.Max(280, SystemParameters.WorkArea.Height - 24);
        Loaded += (_, _) =>
        {
            if (IsSetMode) HeadingText.Text = LocalizationService.T("UI_SetPIN");
        };
    }

    private void Pin_KeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.None) return;
        var digit = e.Key >= Key.D0 && e.Key <= Key.D9 ? e.Key - Key.D0
            : e.Key >= Key.NumPad0 && e.Key <= Key.NumPad9 ? e.Key - Key.NumPad0 : -1;
        if (digit >= 0)
        {
            if (_pin.Length < MaxPinLength) _pin += digit.ToString(CultureInfo.InvariantCulture);
            UpdateDisplay();
            HideError();
            e.Handled = true;
        }
        else if (e.Key == Key.Back)
        {
            Backspace_Click(sender, e);
            e.Handled = true;
        }
    }

    private void UpdateDisplay()
    {
        PinDisplay.Text = new string('●', _pin.Length);
    }

    private void NumPad_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && _pin.Length < MaxPinLength)
        {
            _pin += btn.Content.ToString();
            UpdateDisplay();
            HideError();
        }
    }

    private void Backspace_Click(object sender, RoutedEventArgs e)
    {
        if (_pin.Length > 0)
        {
            _pin = _pin[..^1];
            UpdateDisplay();
        }
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        _pin = "";
        UpdateDisplay();
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (_pin.Length < 4)
        {
            ShowError(LocalizationService.T("Password_MinDigits"));
            return;
        }

        if (IsSetMode && _firstPin == null)
        {
            // First entry — ask to confirm
            _firstPin = _pin;
            _pin = "";
            UpdateDisplay();
            PinDisplay.Text = "";
            ShowInfo(LocalizationService.T("Password_RepeatPin"));
            return;
        }

        if (IsSetMode && _firstPin != _pin)
        {
            ShowError(LocalizationService.T("Password_Mismatch"));
            _firstPin = null;
            _pin = "";
            UpdateDisplay();
            return;
        }

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void ShowError(string msg)
    {
        ErrorText.Text = msg;
        ErrorText.Foreground = FindResource("DangerFg") as Brush ?? Brushes.Red;
        ErrorText.Visibility = Visibility.Visible;
    }

    private void ShowInfo(string msg)
    {
        ErrorText.Text = msg;
        ErrorText.Foreground = FindResource("AccentBrush") as Brush ?? Brushes.Cyan;
        ErrorText.Visibility = Visibility.Visible;
    }

    private void HideError()
    {
        ErrorText.Visibility = Visibility.Collapsed;
    }
}
