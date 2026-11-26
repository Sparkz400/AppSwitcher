namespace AppSwitcher.Services;

// One gate per key, kept only while an operation holds or waits for it. Same-key operations stay exclusive;
// unrelated apps never queue behind each other (a launch holds its gate through a readiness wait of up to minutes),
// and memory stays bounded by the number of operations in flight, not by every app/profile ever seen.
internal sealed class BoundedOperationGates
{
    internal sealed class Entry
    {
        public readonly SemaphoreSlim Gate = new(1, 1);
        public int Users;
    }

    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    internal int ActiveKeys { get { lock (_entries) return _entries.Count; } }

    internal async Task<Lease> EnterAsync(string key, CancellationToken ct)
    {
        Entry entry;
        lock (_entries)
        {
            if (!_entries.TryGetValue(key, out entry!)) _entries[key] = entry = new Entry();
            entry.Users++;
        }

        try
        {
            await entry.Gate.WaitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            Leave(key, entry);
            throw;
        }

        return new Lease(this, key, entry);
    }

    private void Leave(string key, Entry entry)
    {
        lock (_entries)
        {
            if (--entry.Users > 0) return;
            _entries.Remove(key);
            entry.Gate.Dispose();
        }
    }

    internal sealed class Lease(BoundedOperationGates owner, string key, Entry entry)
    {
        private int _released;

        public void Release()
        {
            if (Interlocked.Exchange(ref _released, 1) != 0) return;
            entry.Gate.Release();
            owner.Leave(key, entry);
        }
    }
}
