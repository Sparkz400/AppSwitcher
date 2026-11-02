namespace AppSwitcher;

public partial class SupportReportWindow : Window
{
    private readonly Func<bool> _canExport;
    public SupportReportWindow(string report, Func<bool> canExport)
    {
        InitializeComponent();
        ReportText.Text = report;
        _canExport = canExport;
    }
    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!_canExport()) return;
        var dialog = new Microsoft.Win32.SaveFileDialog { FileName = "AppSwitcher-support.json", Filter = "JSON (*.json)|*.json", DefaultExt = ".json" };
        if (dialog.ShowDialog(this) != true || !_canExport()) return;
        try { await File.WriteAllTextAsync(dialog.FileName, ReportText.Text); Close(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { MessageBox.Show(this, ex.Message, LocalizationService.T("Support_Title"), MessageBoxButton.OK, MessageBoxImage.Error); }
    }
}
