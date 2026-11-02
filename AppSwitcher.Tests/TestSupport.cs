namespace AppSwitcher.Tests;

internal sealed class NoOpAppLifecycleService : IAppLifecycleService
{
    public Task LaunchAsync(AppItem app, CancellationToken ct = default) => Task.CompletedTask;

    public Task StopAsync(AppItem app, AppStopMode mode, CancellationToken ct = default) => Task.CompletedTask;
}
