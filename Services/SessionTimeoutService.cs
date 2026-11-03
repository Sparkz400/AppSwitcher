namespace AppSwitcher.Services;

public class SessionTimeoutService : IDisposable
{
    private readonly ILogger<SessionTimeoutService> _logger;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private readonly TimeProvider _clock;
    private long _lastActivity;
    private int _generation;
    private int _timeoutMinutes;
    private Action? _onTimeout;

    public SessionTimeoutService(ILogger<SessionTimeoutService> logger) : this(logger, TimeProvider.System) { }

    internal SessionTimeoutService(ILogger<SessionTimeoutService> logger, TimeProvider clock)
    {
        _logger = logger;
        _clock = clock;
        _lastActivity = clock.GetTimestamp();
    }

    internal bool IsActive => _cts != null;
    internal int ConfiguredMinutes => _timeoutMinutes;

    public void Start(int timeoutMinutes, Action onTimeout)
    {
        StopSynchronously();

        _timeoutMinutes = Math.Max(0, timeoutMinutes);
        _onTimeout = onTimeout;
        _lastActivity = _clock.GetTimestamp();
        if (_timeoutMinutes == 0) return;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _loop = Task.Run(() => RunLoop(token));
        _logger.LogInformation("Session timeout started: {Minutes} minutes", timeoutMinutes);
    }

    public void ResetTimer()
    {
        Interlocked.Exchange(ref _lastActivity, _clock.GetTimestamp());
    }

    public async Task StopAsync()
    {
        Interlocked.Increment(ref _generation);
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

    public void UpdateTimeout(int newTimeoutMinutes, Action onTimeout)
    {
        _onTimeout = onTimeout;
        if (newTimeoutMinutes != _timeoutMinutes || (newTimeoutMinutes > 0 && !IsActive))
        {
            Start(newTimeoutMinutes, onTimeout);
        }
    }

    private async Task RunLoop(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1), _clock);
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                if (IsExpired())
                {
                    _logger.LogInformation("Session timed out after {Minutes} minutes of inactivity", _timeoutMinutes);
                    var generation = _generation;
                    UiDispatcher.TryBeginInvoke(() =>
                    {
                        if (!ct.IsCancellationRequested && generation == _generation && IsExpired())
                        {
                            ResetTimer();
                            _onTimeout?.Invoke();
                        }
                    });
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    internal bool IsExpired() => IsActive && _timeoutMinutes > 0
        && _clock.GetElapsedTime(Interlocked.Read(ref _lastActivity)).TotalMinutes >= _timeoutMinutes;

    public void Dispose()
    {
        StopSynchronously();
    }

    private void StopSynchronously() => StopAsync().GetAwaiter().GetResult();
}
