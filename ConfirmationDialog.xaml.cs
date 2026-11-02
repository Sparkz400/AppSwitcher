namespace AppSwitcher;

public partial class ConfirmationDialog : Window
{
    public ConfirmationDialog(string appName)
    {
        InitializeComponent();
        MaxHeight = Math.Max(240, Math.Min(620, SystemParameters.WorkArea.Height - 24));
        MessageText.Text = LocalizationService.T("Msg_ConfirmKillApp", appName);
        Loaded += (_, _) => CancelButton.Focus();
    }

    public ConfirmationDialog(string message, string details, string? heading = null, bool destructive = false) : this("")
    {
        MessageText.Text = message;
        DetailText.Text = details;
        HeadingText.Text = heading ?? LocalizationService.T("Confirm_Title");
        ConfirmButton.Content = LocalizationService.T("UI_Confirm");
        ConfirmButton.Style = (Style)FindResource(destructive ? "DangerButton" : "PrimaryButton");
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
