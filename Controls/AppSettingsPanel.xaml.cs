namespace AppSwitcher.Controls;

public partial class AppSettingsPanel : UserControl
{
    public AppSettingsPanel()
    {
        InitializeComponent();
        SizeChanged += (_, _) => ArrangeFields();
    }

    private void ArrangeFields()
    {
        var stacked = ActualWidth < 500;
        foreach (var grid in new[] { NameTypeRow, BrowserProfileRow })
        {
            grid.ColumnDefinitions[1].Width = new GridLength(stacked ? 0 : 1, GridUnitType.Star);
            var first = (FrameworkElement)grid.Children[0];
            var second = (FrameworkElement)grid.Children[1];
            first.Margin = new Thickness(0, 0, stacked ? 0 : 12, 0);
            Grid.SetColumn(second, stacked ? 0 : 1);
            Grid.SetRow(second, stacked ? 1 : 0);
        }
    }
}
