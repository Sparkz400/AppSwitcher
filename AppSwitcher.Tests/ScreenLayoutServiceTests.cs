namespace AppSwitcher.Tests;

public class ScreenLayoutServiceTests
{
    [Fact]
    public void FindMonitorIndex_Returns_OneBased_Monitor_Index()
    {
        var monitors = new List<Rect>
        {
            new(0, 0, 1920, 1080),
            new(1920, 0, 1920, 1080)
        };

        var index = ScreenLayoutService.FindMonitorIndex(new Rect(2100, 100, 800, 600), monitors);

        Assert.Equal(2, index);
    }

    [Fact]
    public void ComputeRestoreBounds_Maps_Relative_Position_To_Current_Monitor()
    {
        var position = new WindowPosition
        {
            AppName = "Worker",
            Left = 400,
            Top = 200,
            Width = 800,
            Height = 600,
            MonitorIndex = 1,
            SavedMonitorLeft = 0,
            SavedMonitorTop = 0,
            SavedMonitorWidth = 1600,
            SavedMonitorHeight = 900
        };

        var monitors = new List<Rect>
        {
            new(1920, 0, 1920, 1080)
        };

        var restored = ScreenLayoutService.ComputeRestoreBounds(position, monitors);

        Assert.Equal(2480, restored.Left, precision: 0);
        Assert.Equal(320, restored.Top, precision: 0);
        Assert.Equal(800, restored.Width, precision: 0);
        Assert.Equal(600, restored.Height, precision: 0);
    }
}
