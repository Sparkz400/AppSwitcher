using System.Text.Json;

namespace AppSwitcher.Services;

public record WatchdogCircuitSnapshot(string Path, string? WinPattern, int AttemptCount, DateTime OpenedAtUtc,
    string? WebsiteId = null, string? BrowserProfileId = null, BrowserKind Browser = BrowserKind.Edge);

public interface IWatchdogCircuitStore
{
    IReadOnlyCollection<WatchdogCircuitSnapshot> LoadOpenCircuits();
    void SaveOpenCircuits(IReadOnlyCollection<WatchdogCircuitSnapshot> circuits);
}

public class MemoryWatchdogCircuitStore : IWatchdogCircuitStore
{
    private readonly object _sync = new();
    private List<WatchdogCircuitSnapshot> _circuits = [];

    public IReadOnlyCollection<WatchdogCircuitSnapshot> LoadOpenCircuits()
    {
        lock (_sync)
        {
            return _circuits.ToList();
        }
    }

    public void SaveOpenCircuits(IReadOnlyCollection<WatchdogCircuitSnapshot> circuits)
    {
        lock (_sync)
        {
            _circuits = [.. circuits];
        }
    }
}

public class FileWatchdogCircuitStore : IWatchdogCircuitStore
{
    private readonly ILogger<FileWatchdogCircuitStore> _logger;

    public FileWatchdogCircuitStore(ILogger<FileWatchdogCircuitStore> logger)
    {
        _logger = logger;
    }

    public IReadOnlyCollection<WatchdogCircuitSnapshot> LoadOpenCircuits()
    {
        try
        {
            var path = AppPaths.WatchdogCircuitFilePath;
            if (!File.Exists(path))
                return [];

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<List<WatchdogCircuitSnapshot>>(json) ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load watchdog circuit state");
            return [];
        }
    }

    public void SaveOpenCircuits(IReadOnlyCollection<WatchdogCircuitSnapshot> circuits)
    {
        try
        {
            var path = AppPaths.WatchdogCircuitFilePath;
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            if (circuits.Count == 0)
            {
                if (File.Exists(path))
                    File.Delete(path);
                return;
            }

            var ordered = circuits
                .OrderBy(c => c.Path, StringComparer.OrdinalIgnoreCase)
                .ThenBy(c => c.WinPattern, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var json = JsonSerializer.Serialize(ordered, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save watchdog circuit state");
        }
    }
}
