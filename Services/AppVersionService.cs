using System.Collections.Concurrent;

namespace AppSwitcher.Services;

public class AppVersionService : IDisposable
{
    private readonly ILogger<AppVersionService> _logger;
    private readonly NotificationService _notifications;
    private readonly ConcurrentDictionary<string, FileSnapshot> _snapshots = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _cts;

    public AppVersionService(ILogger<AppVersionService> logger, NotificationService notifications)
    {
        _logger = logger;
        _notifications = notifications;
    }

    public void Start(Func<List<AppItem>> getApps)
    {
        if (_cts != null)
            return;

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _loop = Task.Run(() => MonitorLoop(getApps, token));
    }

    private async Task MonitorLoop(Func<List<AppItem>> getApps, CancellationToken ct)
    {
        // Take initial snapshots
        foreach (var app in getApps())
            TakeSnapshot(app.Path);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(1), ct);

                foreach (var app in getApps())
                {
                    if (string.IsNullOrWhiteSpace(app.Path)) continue;

                    var key = app.Path.ToLowerInvariant();
                    if (!_snapshots.TryGetValue(key, out var previous))
                    {
                        TakeSnapshot(app.Path);
                        continue;
                    }

                    try
                    {
                        var resolved = ResolveExecutablePath(app.Path);
                        if (resolved == null || !File.Exists(resolved)) continue;

                        var info = new FileInfo(resolved);
                        if (info.LastWriteTimeUtc != previous.LastModified || info.Length != previous.Size)
                        {
                            _logger.LogInformation("Executable updated: {App} ({Path})", app.Name, resolved);
                            _notifications.Show(
                                LocalizationService.T("Msg_AppUpdated", app.Name),
                                NotificationType.Info);

                            _snapshots[key] = new FileSnapshot(info.LastWriteTimeUtc, info.Length);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Failed to check version for {Path}", app.Path);
                    }
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in version monitor loop, continuing...");
            }
        }
    }

    private void TakeSnapshot(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            var resolved = ResolveExecutablePath(path);
            if (resolved == null || !File.Exists(resolved)) return;
            var info = new FileInfo(resolved);
            _snapshots[path.ToLowerInvariant()] = new FileSnapshot(info.LastWriteTimeUtc, info.Length);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to snapshot {Path}", path);
        }
    }

    private static string? ResolveExecutablePath(string configuredPath)
    {
        if (Path.IsPathRooted(configuredPath) && File.Exists(configuredPath))
            return Path.GetFullPath(configuredPath);
        if (File.Exists(configuredPath))
            return Path.GetFullPath(configuredPath);

        var fileName = Path.HasExtension(configuredPath) ? configuredPath : $"{configuredPath}.exe";
        var pathEntries = Environment.GetEnvironmentVariable("PATH")?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (pathEntries == null) return null;
        foreach (var dir in pathEntries)
        {
            try
            {
                var candidate = Path.Combine(dir, fileName);
                if (File.Exists(candidate)) return candidate;
            }
            catch { }
        }
        return null;
    }

    public void Dispose()
    {
        StopSynchronously();
    }

    private record FileSnapshot(DateTime LastModified, long Size);

    private Task? _loop;

    public async Task StopAsync()
    {
        var cts = _cts;
        var loop = _loop;

        _cts = null;
        _loop = null;

        if (cts == null)
            return;

        cts.Cancel();
        try
        {
            if (loop != null)
                await loop;
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            cts.Dispose();
        }
    }

    private void StopSynchronously() => StopAsync().GetAwaiter().GetResult();
}
