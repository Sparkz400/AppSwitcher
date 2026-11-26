namespace AppSwitcher.Tests;

public class AuditProcessFixTests
{
    [Fact]
    public void Offline_Rooted_Path_Does_Not_Match_A_Same_Name_Process_Elsewhere()
    {
        // The share does not exist, so the path cannot be resolved on disk; it must still pin the full path.
        var target = new AppProcessTarget(@"\\offline.invalid\share\dashboard.exe");
        var elsewhere = new KioskWindowSnapshot((IntPtr)10, "Window", "Dashboard", "dashboard", @"C:\Other\dashboard.exe");
        var same = elsewhere with { ProcessPath = @"\\offline.invalid\share\dashboard.exe" };

        Assert.False(ProcessHelper.MatchesWindowSnapshot(elsewhere, target));
        Assert.True(ProcessHelper.MatchesWindowSnapshot(same, target));
    }

    [Theory]
    [InlineData("08:29", false)]
    [InlineData("08:30", true)]
    [InlineData("08:35", true)]
    [InlineData("08:36", false)]
    public void Scheduled_Time_Is_Due_For_Five_Minutes(string now, bool expected) =>
        Assert.Equal(expected, SchedulerService.IsDue(new TimeOnly(8, 30),
            new DateTime(2026, 6, 1) + TimeOnly.Parse(now).ToTimeSpan(), TimeZoneInfo.Utc));

    [Theory]
    [InlineData("01:59", false)]
    [InlineData("03:00", true)]
    [InlineData("03:05", true)]
    [InlineData("03:06", false)]
    public void Time_Skipped_By_Spring_Forward_Fires_Right_After_The_Gap(string now, bool expected)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time");
        var day = new DateTime(2026, 3, 29);
        Assert.True(zone.IsInvalidTime(day.AddHours(2.5)));

        Assert.Equal(expected, SchedulerService.IsDue(new TimeOnly(2, 30), day + TimeOnly.Parse(now).ToTimeSpan(), zone));
    }
}
