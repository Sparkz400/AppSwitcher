using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace AppSwitcher.Services;

public enum ProcessExitKind { Unknown, Normal, Unexpected }

/// <summary>Retains handles for matched processes so their exit codes survive process removal.</summary>
internal sealed class ProcessExitTracker : IDisposable
{
    private sealed class Target
    {
        public Dictionary<int, (SafeProcessHandle Handle, DateTime StartTime)> Processes { get; } = [];
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
                foreach (var process in target.Processes.Values) process.Handle.Dispose();
                target.Processes.Clear();
                return;
            }

            target.LastExit = ProcessExitKind.Unknown;
            var ids = matches.Select(process => process.Id).ToHashSet();
            foreach (var id in target.Processes.Keys.Where(id => !ids.Contains(id)).ToArray())
            {
                target.Processes[id].Handle.Dispose();
                target.Processes.Remove(id);
            }
            foreach (var match in matches)
            {
                SafeProcessHandle? owned = null;
                try
                {
                    var startTime = match.StartTime;
                    if (target.Processes.TryGetValue(match.Id, out var previous))
                    {
                        if (!HasExited(previous.Handle) && previous.StartTime == startTime) continue;
                        previous.Handle.Dispose();
                        target.Processes.Remove(match.Id);
                    }
                    // Open before exit; querying a vanished PID later is insufficient. Limited rights also work for elevated targets.
                    owned = OpenWindowCatalog.OpenProcess(QueryLimitedInformation | Synchronize, false, (uint)match.Id);
                    if (owned.IsInvalid || !OpenWindowCatalog.GetProcessTimes(owned, out var created, out _, out _, out _)
                        || DateTime.FromFileTime(created) != startTime) { owned.Dispose(); continue; }
                    target.Processes.Add(match.Id, (owned, startTime));
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

    private static ProcessExitKind ReadExit((SafeProcessHandle Handle, DateTime StartTime) process) =>
        Classify(HasExited(process.Handle) && GetExitCodeProcess(process.Handle, out var code) ? code : null);

    private static bool HasExited(SafeProcessHandle handle) => WaitForSingleObject(handle, 0) == 0;

    private const uint QueryLimitedInformation = 0x1000, Synchronize = 0x00100000;
    [DllImport("kernel32.dll")] private static extern uint WaitForSingleObject(SafeProcessHandle handle, uint milliseconds);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetExitCodeProcess(SafeProcessHandle process, out int exitCode);

    public void Retain(IReadOnlySet<string> keys)
    {
        lock (_gate)
            foreach (var key in _targets.Keys.Where(key => !keys.Contains(key)).ToArray())
            {
                foreach (var process in _targets[key].Processes.Values) process.Handle.Dispose();
                _targets.Remove(key);
            }
    }
    public void Dispose() => Retain(new HashSet<string>());
}
