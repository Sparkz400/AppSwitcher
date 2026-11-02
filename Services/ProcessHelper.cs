using System.Runtime.InteropServices;

namespace AppSwitcher.Services;

public partial class ProcessHelper : IWindowHandleResolver, IDisposable
{
    private readonly ProcessInventory _inventory = new();
    private readonly ProcessExitTracker _exits = new();
    private readonly AsyncLocal<bool> _observe = new();
    internal IDisposable ObserveProcesses()
    {
        var previous = _observe.Value;
        _observe.Value = true;
        return new ObservationScope(() => _observe.Value = previous);
    }
    internal Task WaitForProcessChangeAsync(TimeSpan timeout, CancellationToken ct) => _inventory.WaitForChangeAsync(timeout, ct);
    private sealed class ObservationScope(Action release) : IDisposable { public void Dispose() => release(); }
    public void Dispose() { _inventory.Dispose(); _exits.Dispose(); }
    internal void RetainObservedTargets(IReadOnlySet<string> keys) => _exits.Retain(keys);
    public virtual ProcessExitKind GetLastExitKind(AppProcessTarget target) => target.IsWebsite
        ? ProcessExitKind.Normal : _exits.GetLastExit(target.Key);
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsIconic(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsZoomed(IntPtr hWnd);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial IntPtr SendMessageTimeoutW(
        IntPtr hWnd,
        uint msg,
        IntPtr wParam,
        IntPtr lParam,
        uint fuFlags,
        uint uTimeout,
        out IntPtr lpdwResult);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int X,
        int Y,
        int cx,
        int cy,
        uint uFlags);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private const int SW_RESTORE = 9;
    private const uint SMTO_ABORTIFHUNG = 0x0002;
    private const uint WM_NULL = 0x0000;
    private const uint SWP_NOZORDER = 0x0004;

    private readonly ILogger<ProcessHelper> _logger;
    private readonly BrowserBridgeService? _browserBridge;

    public ProcessHelper(ILogger<ProcessHelper> logger, BrowserBridgeService? browserBridge = null)
    {
        _logger = logger;
        _browserBridge = browserBridge;
    }

    public IReadOnlyList<BrowserProfileChoice> BrowserProfiles => _browserBridge?.Profiles ?? [];
    public Task<string> InspectWebsiteAsync(AppItem app, CancellationToken ct = default) =>
        _browserBridge?.InspectAsync(app, ct) ?? Task.FromResult("ConnectionUnavailable");
    public virtual WebsitePresence GetWebsitePresence(AppProcessTarget target) => _browserBridge?.GetPresence(target) ?? WebsitePresence.Unknown;
    public virtual Task<AppLaunchResult> ActivateWebsiteAsync(AppItem app, CancellationToken ct = default) =>
        (_browserBridge ?? throw BrowserBridgeService.Failure("ConnectionUnavailable")).OpenAsync(app, true, null, ct, activateOnly: true);
    public virtual Task<AppLaunchResult> StartWebsiteAsync(AppItem app, Func<Task>? beforeCreate, CancellationToken ct = default) =>
        (_browserBridge ?? throw BrowserBridgeService.Failure("ConnectionUnavailable")).OpenAsync(app, true, beforeCreate, ct);
    public virtual Task StopWebsiteAsync(AppItem app, CancellationToken ct = default) =>
        (_browserBridge ?? throw BrowserBridgeService.Failure("ConnectionUnavailable")).CloseAsync(app, ct);

    public virtual async Task<AppLaunchResult> StartAppAsync(AppItem app, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(app);
        ct.ThrowIfCancellationRequested();

        if (app.IsWebsite) return await StartWebsiteAsync(app, null, ct);
        if (WebsiteConfiguration.IsLegacyWebsite(app)) throw BrowserBridgeService.Failure("ConvertRequired");

        if (string.IsNullOrWhiteSpace(app.Path))
            throw new InvalidOperationException($"App '{app.Name}' has no executable path configured.");

        using var targetProc = FindMatchingProcess(app.ProcessTarget);
        if (targetProc != null)
        {
            targetProc.Refresh();
            var hWnd = targetProc.MainWindowHandle;

            if (hWnd != IntPtr.Zero)
            {
                if (IsIconic(hWnd))
                    ShowWindow(hWnd, SW_RESTORE);

                SetForegroundWindow(hWnd);
            }

            return new AppLaunchResult(AppLaunchKind.ActivatedExisting);
        }

        var resolvedPath = TryResolveExecutablePath(app.Path) ?? app.Path;

        if (ProcessPathClassifier.IsNetworkPath(resolvedPath) && !File.Exists(resolvedPath))
        {
            _logger.LogWarning(
                "Network path not immediately available for {Name}: {Path}. Retrying...",
                app.Name,
                resolvedPath);

            var available = await WaitForNetworkPathAsync(
                resolvedPath,
                maxRetries: 3,
                delay: TimeSpan.FromSeconds(2),
                ct);

            if (!available)
                throw new InvalidOperationException($"Network path not available for '{app.Name}': {resolvedPath}");
        }

        ct.ThrowIfCancellationRequested();
        using var startedProcess = Process.Start(new ProcessStartInfo
        {
            FileName = resolvedPath,
            Arguments = app.Args,
            UseShellExecute = true,
            WorkingDirectory = !string.IsNullOrWhiteSpace(app.WorkingDirectory)
                ? app.WorkingDirectory
                : Path.GetDirectoryName(resolvedPath) ?? string.Empty
        });

        if (startedProcess == null)
            _logger.LogDebug("Process.Start returned null for '{Name}' — likely a Store/MSIX app", app.Name);

        return new AppLaunchResult(AppLaunchKind.StartedNewProcess);
    }

    public virtual bool IsAppRunning(AppProcessTarget target)
    {
        if (target.IsWebsite) return GetWebsitePresence(target) == WebsitePresence.Present;
        if (string.IsNullOrWhiteSpace(target.Path))
            return false;

        var matches = GetMatchingProcesses(target);
        var isRunning = matches.Count > 0;
        _exits.Observe(target.Key, matches);
        DisposeProcesses(matches);
        return isRunning;
    }

    public virtual bool IsAppResponding(AppProcessTarget target)
    {
        if (target.IsWebsite) return GetWebsitePresence(target) == WebsitePresence.Present;
        if (string.IsNullOrWhiteSpace(target.Path))
            return true;

        try
        {
            using var proc = FindMatchingProcess(target);
            if (proc?.MainWindowHandle is null or 0)
                return true;

            var result = SendMessageTimeoutW(
                proc.MainWindowHandle,
                WM_NULL,
                IntPtr.Zero,
                IntPtr.Zero,
                SMTO_ABORTIFHUNG,
                2000,
                out _);

            return result != IntPtr.Zero;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error checking if app is responding: {Path}", target.Path);
            return true;
        }
    }

    public virtual void CloseApp(AppProcessTarget target)
    {
        if (target.IsWebsite) throw BrowserBridgeService.Failure("UseWebsiteControls");
        if (string.IsNullOrWhiteSpace(target.Path))
            return;

        var matches = GetMatchingProcesses(target);
        if (matches.Count == 0)
            return;

        var closedCount = 0;
        foreach (var proc in matches)
        {
            using (proc)
            {
                try
                {
                    if (proc.MainWindowHandle == IntPtr.Zero)
                        continue;

                    proc.CloseMainWindow();
                    closedCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error sending WM_CLOSE to {Path}", target.Path);
                }
            }
        }

        if (closedCount > 0)
            _logger.LogDebug("Sent WM_CLOSE to {Path} ({Count} process(es))", target.Path, closedCount);
    }

    public virtual int KillApp(AppProcessTarget target)
    {
        if (target.IsWebsite) throw BrowserBridgeService.Failure("UseWebsiteControls");
        if (string.IsNullOrWhiteSpace(target.Path))
            return 0;

        var matches = GetMatchingProcesses(target);
        if (matches.Count == 0)
            return 0;

        var killedCount = 0;
        foreach (var proc in matches)
        {
            using (proc)
            {
                try
                {
                    proc.Kill(entireProcessTree: true);
                    killedCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error killing process {Path}", target.Path);
                }
            }
        }

        if (killedCount > 0)
            _logger.LogWarning("Force-killed process(es) for {Path}: {Count}", target.Path, killedCount);

        return killedCount;
    }

    public virtual void BringToFront(AppProcessTarget target)
    {
        if (target.IsWebsite) throw BrowserBridgeService.Failure("UseWebsiteControls");
        try
        {
            var hWnd = GetMainWindowHandle(target);
            if (hWnd == IntPtr.Zero)
                return;

            if (IsIconic(hWnd))
                ShowWindow(hWnd, SW_RESTORE);

            SetForegroundWindow(hWnd);
            _logger.LogDebug("Brought to front: {Path}", target.Path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error bringing to front: {Path}", target.Path);
        }
    }

    public virtual IntPtr GetMainWindowHandle(AppProcessTarget target)
    {
        if (target.IsWebsite) return IntPtr.Zero;
        try
        {
            using var proc = FindMatchingProcess(target);
            if (proc == null)
                return IntPtr.Zero;

            proc.Refresh();
            return proc.MainWindowHandle;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not resolve main window handle for {Path}", target.Path);
            return IntPtr.Zero;
        }
    }

    public bool IsWindowOnScreen(AppProcessTarget target)
    {
        try
        {
            var hWnd = GetMainWindowHandle(target);
            if (hWnd == IntPtr.Zero)
                return false;

            if (!IsWindowVisible(hWnd))
                return false;

            if (!GetWindowRect(hWnd, out var rect))
                return false;

            return rect.Left > -30000 && rect.Top > -30000;
        }
        catch
        {
            return false;
        }
    }

    public RECT? GetWindowPosition(AppProcessTarget target)
    {
        try
        {
            var hWnd = GetMainWindowHandle(target);
            if (hWnd == IntPtr.Zero)
                return null;

            if (GetWindowRect(hWnd, out var rect))
                return rect;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not get window position for {Path}", target.Path);
        }

        return null;
    }

    public void SetWindowPosition(AppProcessTarget target, int x, int y, int width, int height)
    {
        try
        {
            var hWnd = GetMainWindowHandle(target);
            if (hWnd == IntPtr.Zero)
                return;

            if (IsIconic(hWnd) || IsZoomed(hWnd))
                ShowWindow(hWnd, SW_RESTORE);

            SetWindowPos(hWnd, IntPtr.Zero, x, y, width, height, SWP_NOZORDER);
            _logger.LogDebug(
                "Set window position for {Path}: ({X},{Y}) {W}x{H}",
                target.Path,
                x,
                y,
                width,
                height);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error setting window position for {Path}", target.Path);
        }
    }

    public List<Process> GetMatchingProcesses(AppProcessTarget target)
    {
        if (target.IsWebsite) return [];
        if (string.IsNullOrWhiteSpace(target.Path))
            return [];

        var resolvedPath = TryResolveExecutablePath(target.Path);
        var exeName = Path.GetFileNameWithoutExtension(target.Path);
        var windowPattern = target.WinPattern?.Trim();

        // A target always requires its executable name. A title filter never requires enumerating every process.
        var candidates = _observe.Value ? _inventory.Candidates(exeName) : Process.GetProcessesByName(exeName);

        var exactMatches = new List<Process>();
        var windowsAppsMatches = new List<Process>();
        var unverifiableMatches = new List<Process>();

        foreach (var process in candidates)
        {
            if (!ProcessNameMatches(process, exeName) ||
                (!string.IsNullOrWhiteSpace(windowPattern) && !ProcessTitleMatches(process, windowPattern)))
            {
                process.Dispose();
                continue;
            }

            var classification = ClassifyProcess(process, resolvedPath, exeName);
            switch (classification)
            {
                case ProcessMatchClass.ExactPath:
                    exactMatches.Add(process);
                    break;
                case ProcessMatchClass.WindowsApps:
                    windowsAppsMatches.Add(process);
                    break;
                case ProcessMatchClass.Unverifiable:
                    unverifiableMatches.Add(process);
                    break;
                default:
                    process.Dispose();
                    break;
            }
        }

        if (exactMatches.Count > 0)
        {
            DisposeProcesses(windowsAppsMatches);
            DisposeProcesses(unverifiableMatches);
            return exactMatches;
        }

        if (!string.IsNullOrWhiteSpace(windowPattern))
        {
            if (windowsAppsMatches.Count > 0)
            {
                DisposeProcesses(unverifiableMatches);
                return windowsAppsMatches;
            }

            return unverifiableMatches;
        }

        if (windowsAppsMatches.Count == 1)
        {
            DisposeProcesses(unverifiableMatches);
            return windowsAppsMatches;
        }

        if (unverifiableMatches.Count == 1)
        {
            DisposeProcesses(windowsAppsMatches);
            return unverifiableMatches;
        }

        DisposeProcesses(windowsAppsMatches);
        DisposeProcesses(unverifiableMatches);
        return [];
    }

    internal bool IsWindowMatchedByAnyTarget(KioskWindowSnapshot snapshot, IEnumerable<AppProcessTarget> targets)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(targets);

        foreach (var target in targets)
        {
            if (MatchesWindowSnapshot(snapshot, target))
                return true;
        }

        return false;
    }

    private Process? FindMatchingProcess(AppProcessTarget target)
    {
        var matches = GetMatchingProcesses(target);
        if (matches.Count == 0)
            return null;

        var selected = SelectPreferredProcess(matches);
        foreach (var match in matches)
        {
            if (!ReferenceEquals(match, selected))
                match.Dispose();
        }

        return selected;
    }

    private static bool ProcessTitleMatches(Process process, string windowPattern)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(process.MainWindowTitle)
                && process.MainWindowTitle.Contains(windowPattern, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool ProcessNameMatches(Process process, string expectedProcessName)
    {
        try
        {
            return process.ProcessName.Equals(expectedProcessName, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static ProcessMatchClass ClassifyProcess(Process process, string? resolvedPath, string expectedProcessName)
    {
        if (resolvedPath == null)
            return ProcessMatchClass.Unverifiable;

        try
        {
            var processPath = process.MainModule?.FileName;
            if (processPath == null)
                return ProcessMatchClass.Unverifiable;

            if (PathsEqual(processPath, resolvedPath))
                return ProcessMatchClass.ExactPath;

            if (processPath.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase))
            {
                return string.Equals(
                    Path.GetFileNameWithoutExtension(processPath),
                    expectedProcessName,
                    StringComparison.OrdinalIgnoreCase)
                    ? ProcessMatchClass.WindowsApps
                    : ProcessMatchClass.None;
            }

            return ProcessMatchClass.None;
        }
        catch (Exception ex) when (
            ex is InvalidOperationException
            or NotSupportedException
            or System.ComponentModel.Win32Exception)
        {
            return ProcessMatchClass.Unverifiable;
        }
    }

    internal static bool MatchesWindowSnapshot(KioskWindowSnapshot snapshot, AppProcessTarget target)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (target.IsWebsite) return false;

        if (string.IsNullOrWhiteSpace(target.Path))
            return false;

        var expectedProcessName = Path.GetFileNameWithoutExtension(target.Path);
        if (!snapshot.ProcessName.Equals(expectedProcessName, StringComparison.OrdinalIgnoreCase))
            return false;

        var windowPattern = target.WinPattern?.Trim();
        if (!string.IsNullOrWhiteSpace(windowPattern)
            && !snapshot.WindowTitle.Contains(windowPattern, StringComparison.OrdinalIgnoreCase))
            return false;

        var resolvedPath = TryResolveExecutablePath(target.Path);
        if (resolvedPath == null || string.IsNullOrWhiteSpace(snapshot.ProcessPath))
            return true;

        if (PathsEqual(snapshot.ProcessPath, resolvedPath))
            return true;

        if (snapshot.ProcessPath.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private async Task<bool> WaitForNetworkPathAsync(
        string path,
        int maxRetries,
        TimeSpan delay,
        CancellationToken ct)
    {
        for (var attempt = 0; attempt < maxRetries; attempt++)
        {
            await Task.Delay(delay, ct);
            if (File.Exists(path))
            {
                _logger.LogInformation("Network path available after {Attempts} retries: {Path}", attempt + 1, path);
                return true;
            }

            _logger.LogDebug("Network path retry {Attempt}/{Max}: {Path}", attempt + 1, maxRetries, path);
        }

        return false;
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    private static string? TryResolveExecutablePath(string configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
            return null;

        if (Path.IsPathRooted(configuredPath) && File.Exists(configuredPath))
            return Path.GetFullPath(configuredPath);

        if (File.Exists(configuredPath))
            return Path.GetFullPath(configuredPath);

        var fileName = Path.HasExtension(configuredPath)
            ? configuredPath
            : $"{configuredPath}.exe";

        var pathEntries = Environment.GetEnvironmentVariable("PATH")?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (pathEntries == null)
            return null;

        foreach (var directory in pathEntries)
        {
            try
            {
                var candidate = Path.Combine(directory, fileName);
                if (File.Exists(candidate))
                    return Path.GetFullPath(candidate);
            }
            catch
            {
                // Ignore malformed PATH entries while resolving executables.
            }
        }

        return null;
    }

    private static Process SelectPreferredProcess(IEnumerable<Process> matches) =>
        matches
            .OrderByDescending(process => HasMainWindow(process))
            .ThenByDescending(GetSafeStartTime)
            .First();

    private static bool HasMainWindow(Process process)
    {
        try
        {
            return process.MainWindowHandle != IntPtr.Zero;
        }
        catch
        {
            return false;
        }
    }

    private static DateTime GetSafeStartTime(Process process)
    {
        try
        {
            return process.StartTime.ToUniversalTime();
        }
        catch
        {
            return DateTime.MinValue;
        }
    }

    private static void DisposeProcesses(IEnumerable<Process> processes)
    {
        foreach (var process in processes)
            process.Dispose();
    }

    private enum ProcessMatchClass
    {
        None,
        ExactPath,
        WindowsApps,
        Unverifiable
    }
}
