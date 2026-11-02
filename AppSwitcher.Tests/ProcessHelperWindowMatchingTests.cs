namespace AppSwitcher.Tests;

public class ProcessHelperWindowMatchingTests
{
    [Fact]
    public void MatchesWindowSnapshot_Returns_True_For_Matching_Process_And_Title()
    {
        var snapshot = new KioskWindowSnapshot(
            (IntPtr)10,
            "Chrome_WidgetWin_1",
            "Operations Dashboard",
            "dashboard",
            @"C:\Apps\dashboard.exe");

        var target = new AppProcessTarget(@"C:\Apps\dashboard.exe", "Dashboard");

        Assert.True(ProcessHelper.MatchesWindowSnapshot(snapshot, target));
    }

    [Fact]
    public void MatchesWindowSnapshot_Returns_False_When_Window_Title_Does_Not_Match()
    {
        var snapshot = new KioskWindowSnapshot(
            (IntPtr)10,
            "Chrome_WidgetWin_1",
            "Operations Console",
            "dashboard",
            @"C:\Apps\dashboard.exe");

        var target = new AppProcessTarget(@"C:\Apps\dashboard.exe", "Dashboard");

        Assert.False(ProcessHelper.MatchesWindowSnapshot(snapshot, target));
    }
}
