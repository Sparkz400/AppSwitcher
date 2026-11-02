namespace AppSwitcher.Tests;

public class ProcessPathClassifierTests
{
    [Fact]
    public void IsNetworkPath_Returns_True_For_Unc_Path()
    {
        Assert.True(ProcessPathClassifier.IsNetworkPath(@"\\server\share\app.exe"));
    }

    [Fact]
    public void IsNetworkPath_Returns_True_For_Mapped_Network_Drive_When_Resolver_Says_Network()
    {
        var result = ProcessPathClassifier.IsNetworkPath(
            @"Z:\apps\tool.exe",
            root => root.Equals(@"Z:\", StringComparison.OrdinalIgnoreCase)
                ? DriveType.Network
                : DriveType.Fixed);

        Assert.True(result);
    }

    [Fact]
    public void IsNetworkPath_Returns_False_For_Local_Path()
    {
        var result = ProcessPathClassifier.IsNetworkPath(
            @"C:\apps\tool.exe",
            _ => DriveType.Fixed);

        Assert.False(result);
    }
}
