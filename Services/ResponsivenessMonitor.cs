using System.Collections.Concurrent;

namespace AppSwitcher.Services;

/// <summary>Slow WM_NULL probes never delay process exits or automatic recovery. At most four run at once.</summary>
internal sealed class ResponsivenessMonitor
{
    private readonly SemaphoreSlim _slots = new(4, 4);
    private readonly ConcurrentDictionary<string, Task> _pending = new();
    private readonly ConcurrentDictionary<string, DateTime> _last = new();
    private readonly ConcurrentDictionary<string, bool> _values = new();
    private readonly object _sync = new();

    internal void Queue(string key, TimeSpan interval, Func<bool> probe, Action<bool> publish, CancellationToken ct)
    {
        lock (_sync)
        {
            // Multiple entries can refer to one process; share the last completed probe with every observer.
            if (_values.TryGetValue(key, out var observed)) publish(observed);
            if (_pending.TryGetValue(key, out var previous) && !previous.IsCompleted) return;
            if (_last.TryGetValue(key, out var last) && DateTime.UtcNow - last < interval) return;
            _last[key] = DateTime.UtcNow;
            _pending[key] = Task.Run(async () =>
            {
                var acquired = false;
                try
                {
                    await _slots.WaitAsync(ct).ConfigureAwait(false); acquired = true;
                    ct.ThrowIfCancellationRequested();
                    var responding = probe();
                    if (!ct.IsCancellationRequested) { _values[key] = responding; publish(responding); }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
                finally { if (acquired) _slots.Release(); }
            }, CancellationToken.None);
        }
    }
    internal void Retain(IReadOnlySet<string> keys)
    {
        foreach (var key in _pending.Keys)
            if (!keys.Contains(key) && _pending.TryGetValue(key, out var task) && task.IsCompleted)
            { _pending.TryRemove(key, out _); _last.TryRemove(key, out _); _values.TryRemove(key, out _); }
    }
    internal Task DrainAsync() => Task.WhenAll(_pending.Values);
}
