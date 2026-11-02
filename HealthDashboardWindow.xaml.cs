namespace AppSwitcher;

public partial class HealthDashboardWindow : Window
{
    public HealthDashboardWindow()
    {
        InitializeComponent();
        Width = Math.Min(Width, SystemParameters.WorkArea.Width - 24);
        Height = Math.Min(Height, SystemParameters.WorkArea.Height - 24);
    }
}
