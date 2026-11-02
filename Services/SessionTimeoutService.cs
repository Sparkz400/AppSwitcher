namespace AppSwitcher.Services;

public class SessionTimeoutService : IDisposable
{
    private readonly ILogger<SessionTimeoutService> _logger;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private DateTime _lastActivity = DateTime.UtcNow;
    private int _timeoutMinutes;
    private Action? _onTimeout;

    public SessionTimeoutService(ILogger<SessionTimeoutService> logger)
    {
        _logger = logger;
    }

    public void Start(int timeoutMinutes, Action onTimeout)
    {
        StopSynchronously();

        if (timeoutMinutes <= 0) return;

        _timeoutMinutes = timeoutMinutes;
        _onTimeout = onTimeout;
        _lastActivity = DateTime.UtcNow;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _loop = Task.Run(() => RunLoop(token));
        _logger.LogInformation("Session timeout started: {Minutes} minutes", timeoutMinutes);
    }

    public void ResetTimer()
    {
        _lastActivity = DateTime.UtcNow;
    }

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

    public void UpdateTimeout(int newTimeoutMinutes, Action onTimeout)
    {
        _onTimeout = onTimeout;
        if (newTimeoutMinutes != _timeoutMinutes)
        {
            Start(newTimeoutMinutes, onTimeout);
        }
    }

    private async Task RunLoop(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                if (_timeoutMinutes <= 0) continue;

                var idle = DateTime.UtcNow - _lastActivity;
                if (idle.TotalMinutes >= _timeoutMinutes)
                {
                    _logger.LogInformation("Session timed out after {Minutes} minutes of inactivity", _timeoutMinutes);
                    UiDispatcher.TryBeginInvoke(() => _onTimeout?.Invoke());
                    _lastActivity = DateTime.UtcNow; // Prevent repeated firing
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    public void Dispose()
    {
        StopSynchronously();
    }

    private void StopSynchronously() => StopAsync().GetAwaiter().GetResult();
}
