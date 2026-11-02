namespace AppSwitcher.Services;

internal static class RestartHandoff
{
    private const string Argument = "--restart-after";

    public static async Task StartReplacementAsync()
    {
        using var current = Process.GetCurrentProcess();
        var signalName = "Local\\AppSwitcher.Restart." + Guid.NewGuid().ToString("N");
        using var ready = new EventWaitHandle(false, EventResetMode.ManualReset, signalName);
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add(Argument);
        start.ArgumentList.Add(current.Id.ToString(CultureInfo.InvariantCulture));
        start.ArgumentList.Add(current.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture));
        start.ArgumentList.Add(signalName);
        using var replacement = Process.Start(start) ?? throw new IOException("Could not start replacement process.");
        if (!await Task.Run(() => ready.WaitOne(TimeSpan.FromSeconds(10))))
            throw new IOException("Replacement process did not acknowledge startup.");
    }

    public static async Task<bool> WaitForPreviousAsync(string[] args)
    {
        if (args.Length == 0 || args[0] != Argument) return false;
        if (args.Length != 4 || !int.TryParse(args[1], out var pid) || !long.TryParse(args[2], out var ticks)
            || !args[3].StartsWith("Local\\AppSwitcher.Restart.", StringComparison.Ordinal))
            throw new ArgumentException("Invalid restart handoff.");
        using var previous = Process.GetProcessById(pid);
        if (previous.StartTime.ToUniversalTime().Ticks != ticks
            || !string.Equals(previous.MainModule?.FileName, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Restart target does not match this application.");
        using var signal = EventWaitHandle.OpenExisting(args[3]);
        signal.Set();
        await previous.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60));
        return true;
    }
}
