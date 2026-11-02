using System.Net.Http;

namespace AppSwitcher.Services;

/// <summary>Readiness is checked for new and already-running dependencies, without launching them again.</summary>
public sealed class AppReadinessService(ProcessHelper processes) : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(5) };

    public Task WaitAsync(AppItem app, CancellationToken ct = default)
    {
        var snapshot = new AppItem { Name = app.Name, Path = app.Path, WinPattern = app.WinPattern,
            AppId = app.AppId, TargetKind = app.TargetKind, WebsiteUrl = app.WebsiteUrl, Browser = app.Browser,
            BrowserProfileId = app.BrowserProfileId, Readiness = app.Readiness, ReadinessTimeoutSeconds = app.ReadinessTimeoutSeconds,
            HealthCheckUrl = app.HealthCheckUrl };
        return WaitUntilAsync(snapshot, token => ProbeAsync(snapshot, token), ct);
    }

    internal static async Task WaitUntilAsync(AppItem app, Func<CancellationToken, Task<bool>> probe, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (app.Readiness == ReadinessMode.None) return;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(app.ReadinessTimeoutSeconds, 1, 600)));
        try
        {
            while (!await probe(deadline.Token).ConfigureAwait(false))
                await Task.Delay(200, deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException(LocalizationService.T("Readiness_Timeout", app.Name, app.ReadinessTimeoutSeconds));
        }
    }

    private async Task<bool> ProbeAsync(AppItem app, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // Control decisions always use fresh process discovery; the telemetry cache cannot authorize a launch.
        if (!processes.IsAppRunning(app.ProcessTarget)) return false;
        if (app.Readiness == ReadinessMode.Process) return true;
        if (app.Readiness == ReadinessMode.Window)
            return app.IsNative && processes.GetMainWindowHandle(app.ProcessTarget) != IntPtr.Zero;
        if (app.Readiness != ReadinessMode.Http || !IsValidHealthUrl(app.HealthCheckUrl))
            throw new InvalidOperationException(LocalizationService.T("Readiness_Invalid"));
        try
        {
            using var response = await _http.GetAsync(app.HealthCheckUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException) { return false; }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return false; }
    }

    internal static bool IsValidHealthUrl(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https" && string.IsNullOrEmpty(uri.UserInfo);

    public void Dispose() => _http.Dispose();
}
