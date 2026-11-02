namespace AppSwitcher.Services;

/// <summary>Shares candidate discovery for observation only. Paths, titles and PID birth times are revalidated.</summary>
internal sealed class ProcessInventory : IDisposable
{
    private sealed record Entry(DateTime ReadAt, Process[] Processes);
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();
    private readonly SemaphoreSlim _changed = new(0, 1);
    private bool _disposed;
    internal long EnumerationCount { get; private set; }

    public async Task WaitForChangeAsync(TimeSpan timeout, CancellationToken ct) => await _changed.WaitAsync(timeout, ct).ConfigureAwait(false);

    public Process[] Candidates(string name)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var now = DateTime.UtcNow;
            foreach (var stale in _entries.Where(e => now - e.Value.ReadAt > TimeSpan.FromMinutes(1)).Select(e => e.Key).ToArray())
                Remove(stale);
            if (!_entries.TryGetValue(name, out var entry) || now - entry.ReadAt >= TimeSpan.FromSeconds(1))
            {
                Remove(name);
                var processes = Process.GetProcessesByName(name);
                EnumerationCount++;
                foreach (var process in processes)
                {
                    try { process.Exited += OnExited; process.EnableRaisingEvents = true; }
                    catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { }
                }
                _entries[name] = entry = new(now, processes);
            }
            var copies = new List<Process>();
            foreach (var watched in entry.Processes)
            {
                Process? copy = null;
                try
                {
                    if (watched.HasExited) continue;
                    copy = Process.GetProcessById(watched.Id);
                    if (copy.StartTime != watched.StartTime) { copy.Dispose(); continue; }
                    copies.Add(copy);
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
                {
                    copy?.Dispose();
                    // Access-denied birth-time reads must not turn a protected running process into cached absence.
                    if (ex is System.ComponentModel.Win32Exception or NotSupportedException)
                    {
                        foreach (var opened in copies) opened.Dispose();
                        EnumerationCount++;
                        return Process.GetProcessesByName(name);
                    }
                }
            }
            return copies.ToArray();
        }
    }
    private void OnExited(object? sender, EventArgs e)
    {
        // Do not acquire the inventory lock in an exit callback: disposal can wait for this callback.
        try { _changed.Release(); } catch (SemaphoreFullException) { } catch (ObjectDisposedException) { }
    }
    private void Remove(string name)
    {
        if (!_entries.Remove(name, out var entry)) return;
        foreach (var process in entry.Processes) { process.Exited -= OnExited; process.Dispose(); }
    }
    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var name in _entries.Keys.ToArray()) Remove(name);
        }
        _changed.Dispose();
    }
}
