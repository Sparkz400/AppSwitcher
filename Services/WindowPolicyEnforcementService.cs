namespace AppSwitcher.Services;

internal class WindowPolicyEnforcementService : IDisposable
{
    private readonly ILogger<WindowPolicyEnforcementService> _logger;
    private readonly IWindowHandleResolver _windowHandleResolver;
    private readonly IWindowPolicyPlatform _platform;
    private readonly object _syncRoot = new();
    private readonly Dictionary<IntPtr, TrackedWindowState> _windowStates = [];
    private readonly HashSet<string> _pendingImmediateAppKeys = new(StringComparer.Ordinal);

    private CancellationTokenSource? _cts;
    private Task? _loop;

    public WindowPolicyEnforcementService(
        ILogger<WindowPolicyEnforcementService> logger,
        IWindowHandleResolver windowHandleResolver,
        IWindowPolicyPlatform platform)
    {
        _logger = logger;
        _windowHandleResolver = windowHandleResolver;
        _platform = platform;
    }

    public void Start(Func<IReadOnlyList<AppItem>> getApps)
    {
        ArgumentNullException.ThrowIfNull(getApps);

        if (_cts != null)
            return;

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _loop = Task.Run(() => RunLoop(getApps, token));
        _logger.LogInformation("Window policy enforcement service started");
    }

    public void RequestImmediateApply(AppItem? app)
    {
        if (app == null || string.IsNullOrWhiteSpace(app.Path))
            return;

        lock (_syncRoot)
        {
            _pendingImmediateAppKeys.Add(app.ProcessTarget.Key);
        }

        if (!HasWindowPolicy(app))
            return;

        try
        {
            EnforceNow([app]);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Immediate window policy enforcement failed for {Name}", app.Name);
        }
    }

    internal void EnforceNow(IReadOnlyList<AppItem> apps)
    {
        ArgumentNullException.ThrowIfNull(apps);

        lock (_syncRoot)
        {
            EnforceCore(apps);
        }
    }

    private async Task RunLoop(Func<IReadOnlyList<AppItem>> getApps, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));

        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(ct))
                    break;

                using var observation = (_windowHandleResolver as ProcessHelper)?.ObserveProcesses();
                EnforceNow(getApps());
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in window policy enforcement loop, continuing...");
            }
        }
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

    private void EnforceCore(IReadOnlyList<AppItem> apps)
    {
        var appsByKey = apps
            .Where(app => !app.IsWebsite && !string.IsNullOrWhiteSpace(app.Path))
            .GroupBy(app => app.ProcessTarget.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        if (appsByKey.Count == 0 && _windowStates.Count == 0 && _pendingImmediateAppKeys.Count == 0)
            return;

        var relevantApps = appsByKey.Values
            .Where(app => HasWindowPolicy(app)
                || _pendingImmediateAppKeys.Contains(app.ProcessTarget.Key)
                || HasTrackedStateForAppKey(app.ProcessTarget.Key))
            .ToList();

        if (relevantApps.Count == 0 && _windowStates.Count == 0)
            return;

        var currentHandlesByAppKey = new Dictionary<string, IntPtr>(StringComparer.Ordinal);

        foreach (var app in relevantApps)
        {
            var appKey = app.ProcessTarget.Key;
            var immediateRequested = _pendingImmediateAppKeys.Contains(appKey);
            var hwnd = _windowHandleResolver.GetMainWindowHandle(app.ProcessTarget);
            if (hwnd == IntPtr.Zero)
                continue;

            currentHandlesByAppKey[appKey] = hwnd;
            ProcessAppWindow(app, hwnd, immediateRequested);
            _pendingImmediateAppKeys.Remove(appKey);
        }

        CleanupStaleStates(appsByKey, currentHandlesByAppKey);
    }

    private void ProcessAppWindow(AppItem app, IntPtr hwnd, bool immediateRequested)
    {
        if (!_platform.IsWindow(hwnd))
        {
            _windowStates.Remove(hwnd);
            return;
        }

        var appKey = app.ProcessTarget.Key;
        if (_windowStates.TryGetValue(hwnd, out var reusedState)
            && !string.Equals(reusedState.AppKey, appKey, StringComparison.Ordinal))
        {
            RevertState(hwnd, reusedState);
            _windowStates.Remove(hwnd);
        }

        var firstObservation = !_windowStates.TryGetValue(hwnd, out var state);
        if (state == null)
        {
            state = new TrackedWindowState { AppKey = appKey };
            _windowStates[hwnd] = state;
        }

        if (_platform.IsWindowMinimized(hwnd))
        {
            // A periodic observation is not a request to activate a manually minimized app.
            if (!firstObservation && !immediateRequested && !app.ReapplyPlacementContinuously) return;
            _platform.RestoreWindow(hwnd);
        }

        var monitors = _platform.GetMonitors();
        if (monitors.Count == 0)
            return;

        var targetMonitor = ResolveTargetMonitor(app, hwnd, monitors);

        if (state.FullscreenManaged && app.PlacementMode != WindowPlacementMode.Fullscreen)
            RestoreFullscreen(hwnd, state);

        if (state.TopmostManaged && !app.AlwaysOnTop)
            RestoreTopmost(hwnd, state);

        var fixedDisplay = app.ForcedMonitor > 0 || !string.IsNullOrEmpty(app.ForcedMonitorId);
        var geometrySignature = HasGeometryPolicy(app)
            ? new GeometryPolicySignature(app.PlacementMode, app.ForcedMonitor, app.ForcedMonitorId,
                fixedDisplay ? targetMonitor.Bounds : default, fixedDisplay ? targetMonitor.WorkArea : default)
            : (GeometryPolicySignature?)null;

        var shouldApplyGeometry = geometrySignature != null
            && (immediateRequested
                || app.ReapplyPlacementContinuously
                || state.LastGeometrySignature == null
                || state.LastGeometrySignature != geometrySignature);

        if (app.PlacementMode == WindowPlacementMode.Fullscreen)
        {
            if (shouldApplyGeometry || !state.FullscreenManaged)
                ApplyFullscreen(hwnd, state, targetMonitor);
        }
        else if (shouldApplyGeometry)
        {
            if (app.ForcedMonitor > 0 || !string.IsNullOrEmpty(app.ForcedMonitorId) || app.PlacementMode == WindowPlacementMode.Maximize)
                MoveWindowToMonitorWorkArea(hwnd, targetMonitor);

            if (app.PlacementMode == WindowPlacementMode.Maximize)
                _platform.MaximizeWindow(hwnd);
        }

        state.LastGeometrySignature = geometrySignature;

        if (app.AlwaysOnTop)
            EnsureTopmost(hwnd, state);

        if (!HasWindowPolicy(app) && !state.FullscreenManaged && !state.TopmostManaged)
            _windowStates.Remove(hwnd);
    }

    private void CleanupStaleStates(
        IReadOnlyDictionary<string, AppItem> appsByKey,
        IReadOnlyDictionary<string, IntPtr> currentHandlesByAppKey)
    {
        foreach (var entry in _windowStates.ToArray())
        {
            var hwnd = entry.Key;
            var state = entry.Value;

            if (!_platform.IsWindow(hwnd))
            {
                _windowStates.Remove(hwnd);
                continue;
            }

            if (!appsByKey.ContainsKey(state.AppKey))
            {
                RevertState(hwnd, state);
                _windowStates.Remove(hwnd);
                continue;
            }

            if (currentHandlesByAppKey.TryGetValue(state.AppKey, out var currentHandle) && currentHandle != hwnd)
            {
                RevertState(hwnd, state);
                _windowStates.Remove(hwnd);
            }
        }
    }

    private void ApplyFullscreen(IntPtr hwnd, TrackedWindowState state, NativeMonitorInfo targetMonitor)
    {
        if (!state.FullscreenManaged)
        {
            if (!_platform.TryGetWindowStyle(hwnd, out var originalStyle)
                || !_platform.TryGetWindowRect(hwnd, out var originalRect))
            {
                return;
            }

            state.OriginalStyle = originalStyle;
            state.OriginalRect = originalRect;
        }

        var fullscreenStyle = WindowsWindowPolicyPlatform.BuildBorderlessFullscreenStyle(state.OriginalStyle);
        if (!_platform.SetWindowStyle(hwnd, fullscreenStyle))
            return;

        if (_platform.SetWindowRect(hwnd, targetMonitor.Bounds, activate: false))
            state.FullscreenManaged = true;
    }

    private void RestoreFullscreen(IntPtr hwnd, TrackedWindowState state)
    {
        if (!state.FullscreenManaged)
            return;

        _platform.SetWindowStyle(hwnd, state.OriginalStyle);
        _platform.SetWindowRect(hwnd, state.OriginalRect, activate: false);
        state.FullscreenManaged = false;
    }

    private void MoveWindowToMonitorWorkArea(IntPtr hwnd, NativeMonitorInfo targetMonitor)
    {
        if (!_platform.TryGetWindowRect(hwnd, out var currentRect))
            return;

        var width = currentRect.Width > 0
            ? Math.Min(currentRect.Width, targetMonitor.WorkArea.Width)
            : targetMonitor.WorkArea.Width;
        var height = currentRect.Height > 0
            ? Math.Min(currentRect.Height, targetMonitor.WorkArea.Height)
            : targetMonitor.WorkArea.Height;

        var left = targetMonitor.WorkArea.Left + Math.Max(0, (targetMonitor.WorkArea.Width - width) / 2);
        var top = targetMonitor.WorkArea.Top + Math.Max(0, (targetMonitor.WorkArea.Height - height) / 2);

        _platform.SetWindowRect(hwnd, NativeRect.FromPositionAndSize(left, top, width, height), activate: false);
    }

    private void EnsureTopmost(IntPtr hwnd, TrackedWindowState state)
    {
        if (!state.TopmostManaged)
        {
            state.OriginalTopmost = _platform.IsTopmost(hwnd);
            state.TopmostManaged = true;
        }

        _platform.SetTopmost(hwnd, topmost: true, activate: false);
    }

    private void RestoreTopmost(IntPtr hwnd, TrackedWindowState state)
    {
        if (!state.TopmostManaged)
            return;

        if (!state.OriginalTopmost)
            _platform.SetTopmost(hwnd, topmost: false, activate: false);

        state.TopmostManaged = false;
        state.OriginalTopmost = false;
    }

    private void RevertState(IntPtr hwnd, TrackedWindowState state)
    {
        if (!_platform.IsWindow(hwnd))
            return;

        RestoreFullscreen(hwnd, state);
        RestoreTopmost(hwnd, state);
    }

    private bool HasTrackedStateForAppKey(string appKey) =>
        _windowStates.Values.Any(state => string.Equals(state.AppKey, appKey, StringComparison.Ordinal));

    private NativeMonitorInfo ResolveTargetMonitor(AppItem app, IntPtr hwnd, IReadOnlyList<NativeMonitorInfo> monitors)
    {
        var primaryMonitor = monitors.FirstOrDefault(monitor => monitor.IsPrimary);
        if (primaryMonitor == default)
            primaryMonitor = monitors[0];

        if (!string.IsNullOrEmpty(app.ForcedMonitorId))
        {
            var matches = monitors.Where(monitor => string.Equals(monitor.DeviceId, app.ForcedMonitorId, StringComparison.OrdinalIgnoreCase)).ToArray();
            return matches.Length == 1 ? matches[0] : primaryMonitor;
        }

        if (app.ForcedMonitor > 0)
            return monitors.FirstOrDefault(monitor => monitor.Index == app.ForcedMonitor) is { Index: > 0 } forcedMonitor
                ? forcedMonitor
                : primaryMonitor;

        var currentMonitorIndex = _platform.GetCurrentMonitorIndex(hwnd);
        return monitors.FirstOrDefault(monitor => monitor.Index == currentMonitorIndex) is { Index: > 0 } currentMonitor
            ? currentMonitor
            : primaryMonitor;
    }

    private static bool HasWindowPolicy(AppItem app) =>
        app.AlwaysOnTop || HasGeometryPolicy(app);

    private static bool HasGeometryPolicy(AppItem app) =>
        app.PlacementMode != WindowPlacementMode.None || app.ForcedMonitor > 0 || !string.IsNullOrEmpty(app.ForcedMonitorId);

    public void Dispose() => StopAsync().GetAwaiter().GetResult();

    private sealed class TrackedWindowState
    {
        public string AppKey { get; set; } = string.Empty;
        public GeometryPolicySignature? LastGeometrySignature { get; set; }
        public bool FullscreenManaged { get; set; }
        public WindowStyleState OriginalStyle { get; set; }
        public NativeRect OriginalRect { get; set; }
        public bool TopmostManaged { get; set; }
        public bool OriginalTopmost { get; set; }
    }

    private readonly record struct GeometryPolicySignature(WindowPlacementMode PlacementMode, int ForcedMonitor, string DeviceId, NativeRect Bounds, NativeRect WorkArea);
}
