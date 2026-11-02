using System.Diagnostics;

namespace AppSwitcher.Tests;

public class ProcessExitTrackerTests
{
    [Theory]
    [InlineData(0, ProcessExitKind.Normal)]
    [InlineData(1, ProcessExitKind.Unexpected)]
    [InlineData(-1073741819, ProcessExitKind.Unexpected)]
    [InlineData(null, ProcessExitKind.Unknown)]
    public void OnlyConfirmedFailuresAreCrashes(int? code, ProcessExitKind expected) =>
        Assert.Equal(expected, ProcessExitTracker.Classify(code));

    [Theory]
    [InlineData(0, ProcessExitKind.Normal)]
    [InlineData(7, ProcessExitKind.Unexpected)]
    public async Task RetainedHandlePreservesExitResult(int code, ProcessExitKind expected)
    {
        using var child = Process.Start(new ProcessStartInfo("cmd.exe", $"/d /q /c set /p ready= & exit {code}")
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true })!;
        using var tracker = new ProcessExitTracker();
        tracker.Observe("test", [child]);
        await child.StandardInput.WriteLineAsync("go");
        await child.StandardInput.FlushAsync();
        await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        tracker.Observe("test", []);
        Assert.Equal(expected, tracker.GetLastExit("test"));
        tracker.Retain(new HashSet<string>());
        Assert.Equal(ProcessExitKind.Unknown, tracker.GetLastExit("test"));
    }
}
