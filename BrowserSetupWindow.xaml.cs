namespace AppSwitcher;

public partial class BrowserSetupWindow : Window
{
    internal BrowserProfileChoice? Result { get; private set; }
    internal BrowserSetupWindow(BrowserSetupViewModel model, bool start = true)
    {
        InitializeComponent(); DataContext = model;
        Width = Math.Min(Width, SystemParameters.WorkArea.Width - 24);
        Height = Math.Min(Height, SystemParameters.WorkArea.Height - 24);
        model.Completed += Complete;
        Loaded += async (_, _) => { if (start) { await model.PrepareCommand.ExecuteAsync(null); await model.WatchAsync(); } };
        Closed += (_, _) => { model.Completed -= Complete; model.Dispose(); };
    }
    private void Complete(BrowserProfileChoice profile) { Result = profile; Close(); }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
