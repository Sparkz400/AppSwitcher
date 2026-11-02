namespace AppSwitcher;

public partial class ConfigurationRecoveryWindow : Window
{
    private readonly ConfigService _config;
    private readonly SecurityService _security;
    private bool _closed;
    private bool _busy;
    private bool _writing;
    internal bool Restored { get; private set; }

    public ConfigurationRecoveryWindow(ConfigService config, SecurityService security, string failure)
    {
        InitializeComponent();
        _config = config;
        _security = security;
        FailureText.Text = failure;
        var backups = config.GetBackups();
        BackupList.ItemsSource = backups;
        StatusText.Text = LocalizationService.T(backups.Count == 0 ? "Recovery_NoBackups" : "Recovery_SelectBackup");
        Closing += (_, e) => { if (_writing) e.Cancel = true; };
        Closed += (_, _) => _closed = true;
    }

    private void Selection_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e) =>
        RestoreButton.IsEnabled = !_busy && !Restored && BackupList.SelectedItem != null;

    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || Restored || BackupList.SelectedItem is not ConfigService.ConfigBackupInfo selected) return;
        _busy = true;
        BackupList.IsEnabled = RestoreButton.IsEnabled = false;
        try
        {
            var preview = await _config.ReadBackupPreviewAsync(selected.FilePath);
            if (_closed) return;
            if (preview == null) { StatusText.Text = LocalizationService.T("Recovery_InvalidBackup"); return; }
            if (!string.IsNullOrEmpty(preview.Config.Settings.AdminPasswordHash))
            {
                if (_security.RetryAfter > TimeSpan.Zero) { StatusText.Text = _security.AuthenticationFailureMessage; return; }
                var pin = new PasswordDialog { Owner = this };
                if (pin.ShowDialog() != true || _closed) return;
                if (_security.GetAccessLevel(pin.Password, preview.Config.Settings.AdminPasswordHash, "") != AccessLevel.Admin)
                { StatusText.Text = _security.AuthenticationFailureMessage; return; }
            }
            var confirm = new ConfirmationDialog(LocalizationService.T("Recovery_Confirm"),
                selected.FileName + Environment.NewLine + LocalizationService.T("UI_ImportConfirm",
                    preview.Config.Apps.Count, preview.Config.Groups.Count, preview.Config.Layouts.Count), destructive: true) { Owner = this };
            if (confirm.ShowDialog() != true || _closed) return;
            _writing = true;
            CloseButton.IsEnabled = false;
            var restored = await _config.RestoreBackupAsync(selected.FilePath, expectedFingerprint: preview.Fingerprint);
            Restored = restored != null;
            StatusText.Text = LocalizationService.T(Restored ? "Recovery_Complete" : "Recovery_RestoreFailed");
        }
        catch (Exception ex) { if (!_closed) StatusText.Text = LocalizationService.T("Recovery_RestoreFailed") + Environment.NewLine + ex.Message; }
        finally
        {
            _writing = _busy = false;
            if (!_closed)
            {
                CloseButton.IsEnabled = true;
                BackupList.IsEnabled = !Restored;
                RestoreButton.IsEnabled = !Restored && BackupList.SelectedItem != null;
            }
        }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var start = new System.Diagnostics.ProcessStartInfo("explorer.exe");
            start.ArgumentList.Add(Path.GetDirectoryName(_config.ConfigFilePath)!);
            using var process = System.Diagnostics.Process.Start(start);
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
