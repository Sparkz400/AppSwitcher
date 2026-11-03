using System.Collections.Concurrent;

namespace AppSwitcher.Services;

public enum AppStopMode
{
    ForceKill,
    Restart,
    Shutdown
}

public enum AppLaunchKind
{
    ActivatedExisting,
    StartedNewProcess
}

public readonly record struct AppLaunchResult(AppLaunchKind Kind, bool FocusSucceeded = true)
{
    public bool StartedNewProcess => Kind == AppLaunchKind.StartedNewProcess;
}

internal enum AppLifecycleHookStage
{
    PreLaunch,
    PostLaunch,
    PreStop,
    PostStop
}

internal readonly record struct AppHookDefinition(string Path, string Args)
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Path);
}

internal interface IAppHookRunner
{
    Task RunAsync(AppItem app, AppLifecycleHookStage stage, CancellationToken ct = default);
}

public interface IAppLifecycleService
{
    Task LaunchAsync(AppItem app, CancellationToken ct = default);
    Task StopAsync(AppItem app, AppStopMode mode, CancellationToken ct = default);
}

internal abstract class AppLifecycleException : InvalidOperationException
{
    protected AppLifecycleException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

internal sealed class AppHookExecutionException : AppLifecycleException
{
    public AppHookExecutionException(AppItem app, AppLifecycleHookStage stage, string reason, Exception? innerException = null)
        : base(
            LocalizationService.T("Msg_LifecycleHookFailed", app.Name, LocalizationService.T(stage.ToResourceKey()), reason),
            innerException)
    {
        App = app;
        Stage = stage;
        Reason = reason;
    }

    public AppItem App { get; }
    public AppLifecycleHookStage Stage { get; }
    public string Reason { get; }
}

internal sealed class AppStopFailedException : AppLifecycleException
{
    public AppStopFailedException(AppItem app, string reason, Exception? innerException = null)
        : base(LocalizationService.T("Msg_StopFailed", app.Name, reason), innerException)
    {
        App = app;
        Reason = reason;
    }

    public AppItem App { get; }
    public string Reason { get; }
}

internal sealed class ProcessHookRunner : IAppHookRunner
{
    private static readonly TimeSpan HookTimeout = TimeSpan.FromSeconds(30);

    public async Task RunAsync(AppItem app, AppLifecycleHookStage stage, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(app);

        var hook = stage.GetHook(app);
        if (!hook.IsConfigured)
            return;

        Process? process = null;
        try
        {
            var startInfo = BuildStartInfo(app, hook);
            process = Process.Start(startInfo);
            if (process == null)
            {
                throw new AppHookExecutionException(
                    app,
                    stage,
                    LocalizationService.T("Msg_LifecycleHookNoProcess"));
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(HookTimeout);

            try
            {
                await process.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                TryKill(process);
                throw new AppHookExecutionException(
                    app,
                    stage,
                    LocalizationService.T("Msg_LifecycleHookTimedOut", (int)HookTimeout.TotalSeconds));
            }

            if (process.ExitCode != 0)
            {
                throw new AppHookExecutionException(
                    app,
                    stage,
                    LocalizationService.T("Msg_LifecycleHookExitCode", process.ExitCode));
            }
        }
        catch (OperationCanceledException)
        {
            if (process != null)
            {
                TryKill(process);
                try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3)); }
                catch (Exception ex) when (ex is TimeoutException or InvalidOperationException) { }
            }
            throw;
        }
        catch (AppHookExecutionException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new AppHookExecutionException(app, stage, ex.Message, ex);
        }
        finally
        {
            process?.Dispose();
        }
    }

    private static ProcessStartInfo BuildStartInfo(AppItem app, AppHookDefinition hook)
    {
        var configuredHookPath = hook.Path.Trim();
        var resolvedHookPath = ResolveConfiguredPath(configuredHookPath);
        var workingDirectory = ResolveWorkingDirectory(app, resolvedHookPath, configuredHookPath);
        var extension = Path.GetExtension(resolvedHookPath ?? configuredHookPath);

        var startInfo = new ProcessStartInfo
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = workingDirectory
        };

        if (extension.Equals(".ps1", StringComparison.OrdinalIgnoreCase))
        {
            startInfo.FileName = "powershell.exe";
            startInfo.Arguments = string.Join(
                ' ',
                "-NoProfile",
                "-ExecutionPolicy",
                "Bypass",
                "-File",
                QuoteArgument(resolvedHookPath ?? configuredHookPath),
                hook.Args ?? string.Empty).Trim();
            return startInfo;
        }

        if (extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".bat", StringComparison.OrdinalIgnoreCase))
        {
            startInfo.FileName = "cmd.exe";
            startInfo.Arguments = $"/c {QuoteArgument(resolvedHookPath ?? configuredHookPath)} {hook.Args}".Trim();
            return startInfo;
        }

        startInfo.FileName = resolvedHookPath ?? configuredHookPath;
        startInfo.Arguments = hook.Args ?? string.Empty;
        return startInfo;
    }

    private static string ResolveWorkingDirectory(AppItem app, string? resolvedHookPath, string configuredHookPath)
    {
        if (!string.IsNullOrWhiteSpace(app.WorkingDirectory))
            return app.WorkingDirectory;

        var hookDirectory = TryGetPathDirectory(resolvedHookPath)
                            ?? TryGetPathDirectory(configuredHookPath);
        if (!string.IsNullOrWhiteSpace(hookDirectory))
            return hookDirectory;

        var appDirectory = TryGetPathDirectory(app.Path);
        return appDirectory ?? Environment.CurrentDirectory;
    }

    private static string? TryGetPathDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        try
        {
            return Path.GetDirectoryName(Path.GetFullPath(path));
        }
        catch
        {
            return null;
        }
    }

    private static string? ResolveConfiguredPath(string configuredPath)
    {
        try
        {
            if (Path.IsPathRooted(configuredPath))
                return Path.GetFullPath(configuredPath);

            if (configuredHookPathHasDirectory(configuredPath))
                return Path.GetFullPath(configuredPath);

            var localCandidate = Path.GetFullPath(configuredPath);
            return File.Exists(localCandidate) ? localCandidate : null;
        }
        catch
        {
            return null;
        }

        static bool configuredHookPathHasDirectory(string path) =>
            path.Contains(Path.DirectorySeparatorChar) || path.Contains(Path.AltDirectorySeparatorChar);
    }

    private static string QuoteArgument(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "\"\"";

        return $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
        }
    }
}

internal sealed class AppLifecycleService : IAppLifecycleService
{
    private static readonly TimeSpan ForceKillWaitTimeout = TimeSpan.FromSeconds(3);
    private readonly ProcessHelper _processHelper;
    private readonly IAppHookRunner _hookRunner;
    private readonly IServiceProvider _services;
    private readonly NotificationService _notifications;
    private readonly ILogger<AppLifecycleService> _logger;
    private readonly BoundedOperationGates _appLocks = new();

    public AppLifecycleService(
        ProcessHelper processHelper,
        IAppHookRunner hookRunner,
        IServiceProvider services,
        NotificationService notifications,
        ILogger<AppLifecycleService> logger)
    {
        _processHelper = processHelper;
        _hookRunner = hookRunner;
        _services = services;
        _notifications = notifications;
        _logger = logger;
    }

    public async Task LaunchAsync(AppItem app, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(app);

        var gate = _appLocks.For(app.ProcessTarget.Key);
        await gate.WaitAsync(ct);
        try
        {
            if (app.IsWebsite)
            {
                var result = await _processHelper.StartWebsiteAsync(app,
                    () => RunPreHookAsync(app, AppLifecycleHookStage.PreLaunch, ct), ct);
                if (result.StartedNewProcess)
                    await RunPostHookBestEffortAsync(app, AppLifecycleHookStage.PostLaunch, ct);
                await WaitForReadinessAsync(app, ct);
                if (!result.FocusSucceeded) throw BrowserBridgeService.Failure("FocusDenied");
                return;
            }
            if (_processHelper.IsAppRunning(app.ProcessTarget))
            {
                await _processHelper.StartAppAsync(app, ct);
                await WaitForReadinessAsync(app, ct);
                return;
            }

            await RunPreHookAsync(app, AppLifecycleHookStage.PreLaunch, ct);

            var launchResult = await _processHelper.StartAppAsync(app, ct);
            if (launchResult.StartedNewProcess)
                await RunPostHookBestEffortAsync(app, AppLifecycleHookStage.PostLaunch, ct);
            await WaitForReadinessAsync(app, ct);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task WaitForReadinessAsync(AppItem app, CancellationToken ct)
    {
        if (app.Readiness == ReadinessMode.None) return;
        var readiness = _services.GetService<AppReadinessService>();
        if (readiness != null) await readiness.WaitAsync(app, ct);
        else { using var fallback = new AppReadinessService(_processHelper); await fallback.WaitAsync(app, ct); }
    }

    public async Task StopAsync(AppItem app, AppStopMode mode, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(app);

        var gate = _appLocks.For(app.ProcessTarget.Key);
        await gate.WaitAsync(ct);
        try
        {
            if (app.IsWebsite)
            {
                await RunPreHookAsync(app, AppLifecycleHookStage.PreStop, ct);
                ct.ThrowIfCancellationRequested();
                MarkExpectedStop(app);
                await _processHelper.StopWebsiteAsync(app, ct);
                await RunPostHookBestEffortAsync(app, AppLifecycleHookStage.PostStop, ct);
                return;
            }
            if (!_processHelper.IsAppRunning(app.ProcessTarget))
                return;

            await RunPreHookAsync(app, AppLifecycleHookStage.PreStop, ct);
            ct.ThrowIfCancellationRequested();
            MarkExpectedStop(app);

            switch (mode)
            {
                case AppStopMode.ForceKill:
                    _processHelper.KillApp(app.ProcessTarget);
                    await WaitForStoppedAsync(app, ForceKillWaitTimeout, ct);
                    break;
                case AppStopMode.Restart:
                    await StopGracefullyAsync(app, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(1), ct);
                    break;
                case AppStopMode.Shutdown:
                    await StopGracefullyAsync(app, TimeSpan.FromSeconds(5), TimeSpan.Zero, ct);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
            }

            await RunPostHookBestEffortAsync(app, AppLifecycleHookStage.PostStop, ct);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task StopGracefullyAsync(AppItem app, TimeSpan gracefulWait, TimeSpan waitAfterKill, CancellationToken ct)
    {
        _processHelper.CloseApp(app.ProcessTarget);
        await WaitUntilAsync(() => !_processHelper.IsAppRunning(app.ProcessTarget), gracefulWait, ct);

        if (_processHelper.IsAppRunning(app.ProcessTarget))
        {
            _logger.LogWarning("App {Name} did not exit gracefully, killing", app.Name);
            _processHelper.KillApp(app.ProcessTarget);

            if (waitAfterKill > TimeSpan.Zero)
                await Task.Delay(waitAfterKill, ct);

            await WaitForStoppedAsync(app, ForceKillWaitTimeout, ct);
        }
    }

    private async Task WaitForStoppedAsync(AppItem app, TimeSpan timeout, CancellationToken ct)
    {
        await WaitUntilAsync(() => !_processHelper.IsAppRunning(app.ProcessTarget), timeout, ct);

        if (_processHelper.IsAppRunning(app.ProcessTarget))
            throw new AppStopFailedException(app, LocalizationService.T("Msg_StopStillRunning"));
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            if (condition())
                return;

            await Task.Delay(250, ct);
        }
    }

    private async Task RunPreHookAsync(AppItem app, AppLifecycleHookStage stage, CancellationToken ct)
    {
        try
        {
            await _hookRunner.RunAsync(app, stage, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (AppHookExecutionException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new AppHookExecutionException(app, stage, ex.Message, ex);
        }
    }

    private async Task RunPostHookBestEffortAsync(AppItem app, AppLifecycleHookStage stage, CancellationToken ct)
    {
        try
        {
            await _hookRunner.RunAsync(app, stage, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var hookException = ex as AppHookExecutionException
                                ?? new AppHookExecutionException(app, stage, ex.Message, ex);
            _logger.LogWarning(ex, "Lifecycle hook {Stage} failed for {Name}", stage, app.Name);
            _notifications.Show(hookException.Message, NotificationType.Error);
        }
    }

    private void MarkExpectedStop(AppItem app)
    {
        if (string.IsNullOrWhiteSpace(app.ProcessTarget.Path))
            return;

        if (_services.GetService(typeof(WatchdogService)) is WatchdogService watchdogService)
            watchdogService.ExpectStop(app.ProcessTarget);
    }
}

internal static class AppLifecycleHookExtensions
{
    public static AppHookDefinition GetHook(this AppLifecycleHookStage stage, AppItem app) =>
        stage switch
        {
            AppLifecycleHookStage.PreLaunch => new AppHookDefinition(app.PreLaunchHookPath, app.PreLaunchHookArgs),
            AppLifecycleHookStage.PostLaunch => new AppHookDefinition(app.PostLaunchHookPath, app.PostLaunchHookArgs),
            AppLifecycleHookStage.PreStop => new AppHookDefinition(app.PreStopHookPath, app.PreStopHookArgs),
            AppLifecycleHookStage.PostStop => new AppHookDefinition(app.PostStopHookPath, app.PostStopHookArgs),
            _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, null)
        };

    public static string ToResourceKey(this AppLifecycleHookStage stage) =>
        stage switch
        {
            AppLifecycleHookStage.PreLaunch => "Settings_PreLaunchHook",
            AppLifecycleHookStage.PostLaunch => "Settings_PostLaunchHook",
            AppLifecycleHookStage.PreStop => "Settings_PreStopHook",
            AppLifecycleHookStage.PostStop => "Settings_PostStopHook",
            _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, null)
        };
}
