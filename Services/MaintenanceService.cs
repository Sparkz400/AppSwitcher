namespace AppSwitcher.Services;

/// <summary>A bounded, persisted pause for automatic lifecycle actions. Kiosk and monitoring stay active.</summary>
public sealed class MaintenanceService : IDisposable
{
    private readonly object _sync = new();
    private readonly string _path;
    private readonly TimeProvider _clock;
    private readonly ITimer _timer;
    private CancellationTokenSource _automatic = new();
    private DateTimeOffset? _until;
    private List<PauseRecord> _history = [];
    private bool _disposed;
    public event Action? Changed;
    public DateTimeOffset? LastStartedAt { get; private set; }
    public DateTimeOffset? LastEndedAt { get; private set; }
    public string? LoadError { get; private set; }

    public MaintenanceService(TimeProvider clock) : this(Path.Combine(AppPaths.DataDirectory, "maintenance.json"), clock) { }
    internal MaintenanceService(string path, TimeProvider clock)
    {
        _path = path; _clock = clock;
        try
        {
            if (File.Exists(path))
            {
                _history = JsonSerializer.Deserialize<List<PauseRecord>>(File.ReadAllText(path)) ?? throw new JsonException();
                if (_history.Any(p => p == null || p.Until < p.Started || p.Until - p.Started > TimeSpan.FromHours(4))) throw new JsonException();
                var saved = _history.LastOrDefault();
                LastStartedAt = saved?.Started;
                LastEndedAt = saved?.Until;
                if (saved?.Until > clock.GetUtcNow())
                {
                    _until = saved.Until;
                    _automatic.Cancel();
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            LoadError = ex.GetType().Name;
            // An unreadable pause file cannot authorize automatic operations. Keep the fallback bounded and visible.
            LastStartedAt = clock.GetUtcNow(); LastEndedAt = _until = clock.GetUtcNow().AddMinutes(30);
            _history = [new(LastStartedAt.Value, _until.Value)];
            _automatic.Cancel();
        }
        _timer = clock.CreateTimer(_ => Tick(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        if (_until != null) _timer.Change(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    public bool IsPaused { get { lock (_sync) return _until > _clock.GetUtcNow(); } }
    public DateTimeOffset? PausedUntil { get { lock (_sync) return _until; } }
    public CancellationToken AutomaticToken { get { Tick(); lock (_sync) return _automatic.Token; } }
    public string Status
    {
        get
        {
            lock (_sync)
            {
                var status = _until > _clock.GetUtcNow()
                    ? LocalizationService.T("Maintenance_Active", Math.Ceiling((_until.Value - _clock.GetUtcNow()).TotalMinutes))
                    : LocalizationService.T("Maintenance_Inactive");
                return LoadError == null ? status : status + " · " + LocalizationService.T("Maintenance_ReadError");
            }
        }
    }

    public void Pause(TimeSpan duration)
    {
        if (duration < TimeSpan.FromMinutes(1) || duration > TimeSpan.FromHours(4)) throw new ArgumentOutOfRangeException(nameof(duration));
        lock (_sync)
        {
            var now = _clock.GetUtcNow();
            var until = now + duration;
            var history = _history.Where(p => p.Until >= now.AddDays(-2)).ToList();
            if (_until > now && history.Count > 0) history[^1] = history[^1] with { Until = now };
            history.Add(new(now, until));
            Save(history); // A failed save must not silently lose the pause on restart.
            _history = history;
            LastStartedAt = now; LastEndedAt = until; _until = until;
            _automatic.Cancel();
            _timer.Change(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        }
        Changed?.Invoke();
    }

    public void Resume()
    {
        lock (_sync)
        {
            var now = _clock.GetUtcNow();
            var history = _history.ToList();
            if (_until > now && history.Count > 0) history[^1] = history[^1] with { Until = now };
            Save(history);
            _history = history;
            LastEndedAt = now;
            EndPause();
        }
        Changed?.Invoke();
    }

    // A scheduled event due during a pause is skipped, including after restart. It is never caught up on resume.
    internal bool Covers(DateTimeOffset due)
    { lock (_sync) return _history.Any(p => due >= p.Started && due <= p.Until); }

    private void Tick()
    {
        lock (_sync)
        {
            if (_disposed || _until == null) return;
            if (_until <= _clock.GetUtcNow()) EndPause();
        }
        Changed?.Invoke();
    }
    private void EndPause()
    {
        _until = null;
        _automatic.Dispose(); _automatic = new();
        _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }
    private void Save(List<PauseRecord> history)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temp = _path + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, history);
            stream.Flush(true);
        }
        File.Move(temp, _path, true);
        LoadError = null;
    }
    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true; _timer.Dispose(); _automatic.Cancel(); _automatic.Dispose();
        }
    }
    private sealed record PauseRecord(DateTimeOffset Started, DateTimeOffset Until);
}
