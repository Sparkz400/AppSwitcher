using System.Net.Http;

namespace AppSwitcher.Services;

public class HealthCheckService : IDisposable
{
    private readonly ILogger<HealthCheckService> _logger;
    private readonly HttpClient _httpClient;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public HealthCheckService(ILogger<HealthCheckService> logger)
    {
        _logger = logger;
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
    }

    public void Start(Func<IReadOnlyList<AppItem>> getApps)
    {
        if (_cts != null) return;

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _loop = Task.Run(() => RunLoop(getApps, token));
        _logger.LogInformation("Health check service started");
    }

    private async Task RunLoop(Func<IReadOnlyList<AppItem>> getApps, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(ct))
                    break;

                var apps = getApps();
                var appsWithHealthCheck = apps
                    .Where(a => !string.IsNullOrWhiteSpace(a.HealthCheckUrl) && a.IsRunning)
                    .ToList();

                if (appsWithHealthCheck.Count == 0) continue;

                await Parallel.ForEachAsync(appsWithHealthCheck,
                    new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct },
                    async (app, token) => await CheckAppHealth(app, token));
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in health check loop, continuing...");
            }
        }
    }

    private async Task CheckAppHealth(AppItem app, CancellationToken ct)
    {
        try
        {
            using var response = await _httpClient.GetAsync(app.HealthCheckUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            var healthy = LocalizationService.T("Status_Healthy");
            var unhealthy = LocalizationService.T("Status_Unhealthy");
            var status = response.IsSuccessStatusCode ? healthy : unhealthy;
            var previousStatus = app.HealthStatus;

            UiDispatcher.TryBeginInvoke(() =>
            {
                app.HealthStatus = status;
                if (IsStatus(previousStatus: status, resourceValue: unhealthy, legacyValue: "Unhealthy") && app.State == AppState.Running)
                    app.State = AppState.Unhealthy;
                else if (IsStatus(previousStatus: status, resourceValue: healthy, legacyValue: "Healthy") && app.State == AppState.Unhealthy)
                    app.State = AppState.Running;
            });

            if (IsStatus(previousStatus, healthy, "Healthy") && IsStatus(status, unhealthy, "Unhealthy"))
                _logger.LogWarning("Health check failed for {Name}: HTTP {StatusCode}", app.Name, (int)response.StatusCode);
            else if (IsStatus(previousStatus, unhealthy, "Unhealthy") && IsStatus(status, healthy, "Healthy"))
                _logger.LogInformation("Health check recovered for {Name}", app.Name);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            UiDispatcher.TryBeginInvoke(() =>
            {
                app.HealthStatus = LocalizationService.T("Status_Unhealthy");
                if (app.State == AppState.Running)
                    app.State = AppState.Unhealthy;
            });

            _logger.LogWarning(ex, "Health check error for {Name} at {Url}", app.Name, app.HealthCheckUrl);
        }
    }

    public void Dispose()
    {
        StopSynchronously();
        _httpClient.Dispose();
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

    private static bool IsStatus(string previousStatus, string resourceValue, string legacyValue) =>
        string.Equals(previousStatus, resourceValue, StringComparison.CurrentCulture)
        || string.Equals(previousStatus, legacyValue, StringComparison.Ordinal);

    private void StopSynchronously() => StopAsync().GetAwaiter().GetResult();
}
