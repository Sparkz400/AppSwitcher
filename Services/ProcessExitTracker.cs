namespace AppSwitcher.Services;

public enum ProcessExitKind { Unknown, Normal, Unexpected }

/// <summary>Retains handles for matched processes so their exit codes survive process removal.</summary>
internal sealed class ProcessExitTracker : IDisposable
{
    private sealed class Target
    {
        public Dictionary<int, Process> Processes { get; } = [];
        public ProcessExitKind LastExit { get; set; }
    }
    private readonly Dictionary<string, Target> _targets = [];
    private readonly object _gate = new();

    public void Observe(string key, IReadOnlyCollection<Process> matches)
    {
        lock (_gate)
        {
            if (!_targets.TryGetValue(key, out var target)) _targets[key] = target = new();
            if (matches.Count == 0)
            {
                if (target.Processes.Count == 0) return;
                var outcomes = target.Processes.Values.Select(ReadExit).ToArray();
                target.LastExit = outcomes.Contains(ProcessExitKind.Unexpected) ? ProcessExitKind.Unexpected
                    : outcomes.All(result => result == ProcessExitKind.Normal) ? ProcessExitKind.Normal : ProcessExitKind.Unknown;
                foreach (var process in target.Processes.Values) process.Dispose();
                target.Processes.Clear();
                return;
            }

            target.LastExit = ProcessExitKind.Unknown;
            var ids = matches.Select(process => process.Id).ToHashSet();
            foreach (var id in target.Processes.Keys.Where(id => !ids.Contains(id)).ToArray())
            {
                target.Processes[id].Dispose();
                target.Processes.Remove(id);
            }
            foreach (var match in matches)
            {
                Process? owned = null;
                try
                {
                    if (target.Processes.TryGetValue(match.Id, out var previous))
                    {
                        if (!previous.HasExited && previous.StartTime == match.StartTime) continue;
                        previous.Dispose();
                        target.Processes.Remove(match.Id);
                    }
                    owned = Process.GetProcessById(match.Id);
                    _ = owned.Handle; // Open before exit; querying a vanished PID later is insufficient.
                    if (owned.StartTime != match.StartTime) { owned.Dispose(); continue; }
                    target.Processes.Add(match.Id, owned);
                    owned = null;
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
                { owned?.Dispose(); }
            }
        }
    }

    public ProcessExitKind GetLastExit(string key)
    {
        lock (_gate) return _targets.TryGetValue(key, out var target) ? target.LastExit : ProcessExitKind.Unknown;
    }

    internal static ProcessExitKind Classify(int? exitCode) => exitCode switch
    { null => ProcessExitKind.Unknown, 0 => ProcessExitKind.Normal, _ => ProcessExitKind.Unexpected };

    private static ProcessExitKind ReadExit(Process process)
    {
        try { return Classify(process.HasExited ? process.ExitCode : null); }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        { return ProcessExitKind.Unknown; }
    }

    public void Retain(IReadOnlySet<string> keys)
    {
        lock (_gate)
            foreach (var key in _targets.Keys.Where(key => !keys.Contains(key)).ToArray())
            {
                foreach (var process in _targets[key].Processes.Values) process.Dispose();
                _targets.Remove(key);
            }
    }
    public void Dispose() => Retain(new HashSet<string>());
}
