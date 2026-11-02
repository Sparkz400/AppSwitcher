using AppSwitcher.BrowserIntegration;

namespace AppSwitcher.Services;

public sealed record BrowserOwnership(string AppId, string ProfileId, BrowserKind Browser, string Generation, string Phase);

/// <summary>Desktop-side write-ahead journal. Loss of extension storage cannot authorize a second creation.</summary>
public sealed class BrowserOwnershipStore
{
    private readonly string _path;
    private readonly object _gate = new();
    private readonly Dictionary<string, BrowserOwnership> _entries;
    public BrowserOwnershipStore(string path)
    {
        _path = path;
        _entries = File.Exists(path)
            ? JsonSerializer.Deserialize<Dictionary<string, BrowserOwnership>>(File.ReadAllText(path), BrowserWire.JsonOptions)
                ?? throw new InvalidDataException("Browser ownership journal is invalid.")
            : new(StringComparer.Ordinal);
        if (_entries.Any(pair => pair.Value == null || pair.Key != pair.Value.AppId ||
            !Guid.TryParse(pair.Value.AppId, out _) || !Guid.TryParse(pair.Value.ProfileId, out _) ||
            !Guid.TryParse(pair.Value.Generation, out _) || !Enum.IsDefined(pair.Value.Browser) ||
            pair.Value.Phase is not ("Reserved" or "Issued" or "Active" or "Closing" or "Closed")))
            throw new InvalidDataException("Browser ownership journal is invalid.");
    }
    public BrowserOwnership? Get(string appId) { lock (_gate) return _entries.GetValueOrDefault(appId); }
    public BrowserOwnership Reserve(AppItem app)
    {
        var entry = new BrowserOwnership(app.AppId, app.BrowserProfileId, app.Browser, Guid.NewGuid().ToString("N"), "Reserved");
        Put(entry);
        return entry;
    }
    public void Put(BrowserOwnership entry)
    {
        lock (_gate)
        {
            var next = new Dictionary<string, BrowserOwnership>(_entries, StringComparer.Ordinal) { [entry.AppId] = entry };
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
            var temp = _path + ".tmp";
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, next, BrowserWire.JsonOptions);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temp, _path, overwrite: true);
            _entries[entry.AppId] = entry;
        }
    }
}
