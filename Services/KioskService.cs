using System.Runtime.InteropServices;
using System.Windows.Interop;
using Microsoft.Win32;

namespace AppSwitcher.Services;

/// <summary>
/// Manages app-level kiosk hardening and optional machine-level Keyboard Filter lockdown.
/// </summary>
internal sealed partial class KioskService : IDisposable
{
    private const int EnforcementIntervalMilliseconds = 200;

    private enum EdgeSwipePolicyScope
    {
        None,
        Machine,
        User
    }

    private readonly record struct EdgeSwipePolicyState(bool Exists, object? Value, RegistryValueKind? Kind)
    {
        public bool IsDisabled => Value is int intValue && intValue == 0;
    }

    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;

    private const int VK_TAB = 0x09;
    private const int VK_ESCAPE = 0x1B;
    private const int VK_SPACE = 0x20;
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;
    private const int VK_F4 = 0x73;

    private const string EdgeSwipeRegistryPath = @"SOFTWARE\Policies\Microsoft\Windows\EdgeUI";
    private const string EdgeSwipeValueName = "AllowEdgeSwipe";

    private readonly ILogger<KioskService> _logger;
    private readonly IKioskPlatform _platform;
    private readonly IKioskConfiguredAppMatcher _configuredAppMatcher;
    private readonly KioskForegroundPolicy _foregroundPolicy;
    private readonly IKioskMachineLockdownService _machineLockdownService;
    private readonly DispatcherTimer _enforcementTimer;
    private readonly HashSet<Window> _ownedWindows = [];
    private readonly HashSet<IntPtr> _hiddenTaskbarHandles = [];

    private Func<IReadOnlyCollection<AppProcessTarget>>? _configuredAppTargetsProvider;
    private IntPtr _hookId = IntPtr.Zero;
    private KioskKeyboardProc? _hookProc;
    private bool _isActive;
    private bool _edgeSwipeWasDisabled;
    private bool _lastKeyboardHookInstalled;
    private bool _lastEdgeSwipePolicyApplied;
    private bool _edgeSwipeValuePreviouslyExisted;
    private object? _previousEdgeSwipeValue;
    private RegistryValueKind? _previousEdgeSwipeValueKind;
    private EdgeSwipePolicyScope _edgeSwipePolicyScope;
    private IntPtr _lastBlockedForegroundHandle = IntPtr.Zero;
    private int _immediateEnforcementQueued;
    private KioskMachineLockdownResult _lastMachineLockdownResult =
        KioskMachineLockdownResult.NotConfigured("Keyboard Filter status has not been checked yet.");

    [LibraryImport("user32.dll")]
    private static partial short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    public bool IsActive => _isActive;
    public bool LastKeyboardHookInstalled => _lastKeyboardHookInstalled;
    public bool LastEdgeSwipePolicyApplied => _lastEdgeSwipePolicyApplied;
    public KioskMachineLockdownResult LastMachineLockdownResult => _lastMachineLockdownResult;
    public KioskStatus Status { get; } = new()
    {
        DeploymentNotes = "App-level kiosk hardening with optional Windows Keyboard Filter lockdown."
    };

    public KioskService(
        ILogger<KioskService> logger,
        ProcessHelper processHelper,
        IKioskMachineLockdownService machineLockdownService)
        : this(
            logger,
            new WindowsKioskPlatform(),
            new ProcessHelperKioskConfiguredAppMatcher(processHelper),
            new KioskForegroundPolicy(),
            machineLockdownService)
    {
    }

    internal KioskService(
        ILogger<KioskService> logger,
        IKioskPlatform platform,
        IKioskConfiguredAppMatcher configuredAppMatcher,
        KioskForegroundPolicy foregroundPolicy,
        IKioskMachineLockdownService machineLockdownService,
        DispatcherTimer? enforcementTimer = null)
    {
        _logger = logger;
        _platform = platform;
        _configuredAppMatcher = configuredAppMatcher;
        _foregroundPolicy = foregroundPolicy;
        _machineLockdownService = machineLockdownService;
        _enforcementTimer = enforcementTimer ?? new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(EnforcementIntervalMilliseconds)
        };
        _enforcementTimer.Tick += (_, _) => EnforcementTick();
        UpdateStatus();
    }

    public void SetConfiguredAppTargetProvider(Func<IReadOnlyCollection<AppProcessTarget>> provider)
    {
        _configuredAppTargetsProvider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    public void RegisterOwnedWindow(Window window)
    {
        if (_ownedWindows.Add(window))
        {
            window.Closed += OwnedWindow_Closed;
            EnsureWindowTopmost(window, activate: false);
        }
    }

    public void UnregisterOwnedWindow(Window window)
    {
        if (_ownedWindows.Remove(window))
            window.Closed -= OwnedWindow_Closed;
    }

    public async Task<KioskMachineLockdownResult> ActivateAsync(CancellationToken cancellationToken = default)
    {
        if (_isActive)
        {
            UpdateStatus();
            return _lastMachineLockdownResult;
        }

        ResetForegroundDiagnostics();
        _lastKeyboardHookInstalled = InstallKeyboardHook();
        HideTaskbars();
        _lastEdgeSwipePolicyApplied = DisableEdgeSwipes();
        _isActive = true;
        _enforcementTimer.Start();

        _lastMachineLockdownResult = await _machineLockdownService.ApplyAsync(cancellationToken);
        RecordStatus("Activation", BuildActivationResult());
        _logger.LogInformation(
            "Kiosk mode activated (KeyboardFilterState={State})",
            _lastMachineLockdownResult.State);
        UpdateStatus();
        return _lastMachineLockdownResult;
    }

    public async Task<KioskMachineLockdownResult> DeactivateAsync(CancellationToken cancellationToken = default)
    {
        if (!_isActive)
        {
            UpdateStatus();
            return _lastMachineLockdownResult;
        }

        var previousMachineResult = _lastMachineLockdownResult;
        var machineResult = await _machineLockdownService.RemoveAsync(cancellationToken);
        if (machineResult.BlocksDeactivation)
        {
            _lastMachineLockdownResult = PreservePreviousMachineState(machineResult, previousMachineResult);
            RecordStatus("Deactivation", _lastMachineLockdownResult.Message);
            UpdateStatus();
            return _lastMachineLockdownResult;
        }

        _lastMachineLockdownResult = machineResult;

        DeactivateAppLevelOnly();

        RecordStatus("Deactivation", "Kiosk protection disabled");
        _logger.LogInformation(
            "Kiosk mode deactivated (KeyboardFilterState={State})",
            _lastMachineLockdownResult.State);
        UpdateStatus();
        return _lastMachineLockdownResult;
    }

    public void RefreshStatus() => UpdateStatus();

    private void OwnedWindow_Closed(object? sender, EventArgs e)
    {
        if (sender is Window window)
            UnregisterOwnedWindow(window);
    }

    private bool InstallKeyboardHook()
    {
        if (_hookId != IntPtr.Zero)
            return true;

        try
        {
            _hookProc = HookCallback;
            if (!_platform.TryInstallKeyboardHook(_hookProc, out _hookId) || _hookId == IntPtr.Zero)
            {
                _hookId = IntPtr.Zero;
                _hookProc = null;
                _logger.LogWarning("Failed to install keyboard hook");
                return false;
            }
        }
        catch (Exception ex)
        {
            _hookId = IntPtr.Zero;
            _hookProc = null;
            _logger.LogWarning(ex, "Failed to install keyboard hook");
            return false;
        }

        _logger.LogDebug("Keyboard hook installed");
        return true;
    }

    private void RemoveKeyboardHook()
    {
        if (_hookId == IntPtr.Zero)
            return;

        _platform.RemoveKeyboardHook(_hookId);
        _hookId = IntPtr.Zero;
        _hookProc = null;
        _logger.LogDebug("Keyboard hook removed");
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == WM_KEYDOWN || wParam == WM_SYSKEYDOWN))
        {
            var vkCode = Marshal.ReadInt32(lParam);

            var altPressed = (GetAsyncKeyState(0x12) & 0x8000) != 0;
            var ctrlPressed = (GetAsyncKeyState(0x11) & 0x8000) != 0;
            var shiftPressed = (GetAsyncKeyState(0x10) & 0x8000) != 0;

            if (ShouldBlockShortcut(vkCode, altPressed, ctrlPressed, shiftPressed))
            {
                QueueImmediateEnforcement();
                return (IntPtr)1;
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    internal static bool ShouldBlockShortcut(int vkCode, bool altPressed, bool ctrlPressed, bool shiftPressed)
    {
        if (vkCode is VK_LWIN or VK_RWIN)
            return true;

        if (altPressed && vkCode is VK_TAB or VK_F4 or VK_ESCAPE or VK_SPACE)
            return true;

        if (ctrlPressed && vkCode == VK_ESCAPE)
            return true;

        if (ctrlPressed && shiftPressed && vkCode == VK_ESCAPE)
            return true;

        return false;
    }

    private void QueueImmediateEnforcement()
    {
        if (!_isActive)
            return;

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null)
            return;

        if (Interlocked.Exchange(ref _immediateEnforcementQueued, 1) != 0)
            return;

        dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(() =>
        {
            try
            {
                if (_isActive)
                    RunImmediateEnforcement();
            }
            finally
            {
                Interlocked.Exchange(ref _immediateEnforcementQueued, 0);
            }
        }));
    }

    private void RunImmediateEnforcement()
    {
        string? action = null;
        string? result = null;

        if (AreAnyTaskbarsVisible())
        {
            HideTaskbars();
            action = "Taskbar";
            result = "Hidden after blocked shortcut";
        }

        if (TryHandleForegroundWindow(out var foregroundResult))
        {
            action = "Shell surface";
            result = foregroundResult;
        }

        if (action == null)
            return;

        RecordStatus("Shortcut block", result ?? "Recovered");
        UpdateStatus();
    }

    private void HideTaskbars()
    {
        var taskbarHandles = _platform.GetTaskbarHandles();
        var hiddenCount = 0;

        foreach (var handle in taskbarHandles)
        {
            if (handle == IntPtr.Zero)
                continue;

            _platform.HideWindow(handle);
            _hiddenTaskbarHandles.Add(handle);
            hiddenCount++;
        }

        if (hiddenCount > 0)
            _logger.LogDebug("Taskbars hidden ({Count})", hiddenCount);
    }

    private void ShowTaskbars()
    {
        var taskbarHandles = _hiddenTaskbarHandles
            .Concat(_platform.GetTaskbarHandles())
            .Where(handle => handle != IntPtr.Zero)
            .Distinct()
            .ToList();

        foreach (var handle in taskbarHandles)
            _platform.ShowWindow(handle);

        _hiddenTaskbarHandles.Clear();

        if (taskbarHandles.Count > 0)
            _logger.LogDebug("Taskbars restored ({Count})", taskbarHandles.Count);
    }

    private bool DisableEdgeSwipes()
    {
        if (_edgeSwipeWasDisabled)
        {
            _logger.LogDebug("Edge-swipe policy is already being tracked by the current session");
            return true;
        }

        ResetEdgeSwipeTracking();

        var machinePolicyReadable = TryReadEdgeSwipePolicy(Registry.LocalMachine, EdgeSwipePolicyScope.Machine, out var machineState);
        if (machinePolicyReadable && machineState.IsDisabled)
        {
            RememberEdgeSwipeState(EdgeSwipePolicyScope.Machine, machineState, wasModified: false);
            _logger.LogDebug("Edge swipes were already disabled by machine-wide policy");
            return true;
        }

        if (TryApplyEdgeSwipePolicy(Registry.LocalMachine, EdgeSwipePolicyScope.Machine, machineState))
            return true;

        if (machineState.Exists && !machineState.IsDisabled)
        {
            _logger.LogWarning("Could not disable edge swipes because a machine-wide policy is configured and could not be changed");
            return false;
        }

        _logger.LogInformation("Could not apply machine-wide edge-swipe policy, trying current-user policy");

        var userPolicyReadable = TryReadEdgeSwipePolicy(Registry.CurrentUser, EdgeSwipePolicyScope.User, out var userState);
        if (userPolicyReadable && userState.IsDisabled)
        {
            RememberEdgeSwipeState(EdgeSwipePolicyScope.User, userState, wasModified: false);
            _logger.LogDebug("Edge swipes were already disabled by current-user policy");
            return true;
        }

        if (TryApplyEdgeSwipePolicy(Registry.CurrentUser, EdgeSwipePolicyScope.User, userState))
            return true;

        _logger.LogWarning("Could not disable edge swipes using machine-wide or current-user policy");
        return false;
    }

    private void EnableEdgeSwipes()
    {
        if (!_edgeSwipeWasDisabled)
            return;

        try
        {
            var rootKey = GetEdgeSwipePolicyRootKey(_edgeSwipePolicyScope);
            if (rootKey == null)
                return;

            using var key = rootKey.CreateSubKey(EdgeSwipeRegistryPath, true);
            if (key == null)
                return;

            if (_edgeSwipeValuePreviouslyExisted && _previousEdgeSwipeValue != null && _previousEdgeSwipeValueKind.HasValue)
            {
                key.SetValue(EdgeSwipeValueName, _previousEdgeSwipeValue, _previousEdgeSwipeValueKind.Value);
            }
            else
            {
                key.DeleteValue(EdgeSwipeValueName, false);
            }

            var scopeLabel = GetEdgeSwipePolicyScopeLabel(_edgeSwipePolicyScope);
            ResetEdgeSwipeTracking();
            _logger.LogDebug("Edge-swipe policy restored for {Scope}", scopeLabel);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Could not re-enable edge swipes: {Message}", ex.Message);
        }
    }

    private bool TryReadEdgeSwipePolicy(RegistryKey rootKey, EdgeSwipePolicyScope scope, out EdgeSwipePolicyState state)
    {
        state = default;

        try
        {
            using var key = rootKey.OpenSubKey(EdgeSwipeRegistryPath, writable: false);
            if (key == null)
                return true;

            var value = key.GetValue(EdgeSwipeValueName);
            state = new EdgeSwipePolicyState(
                Exists: value != null,
                Value: value,
                Kind: value != null ? key.GetValueKind(EdgeSwipeValueName) : null);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Could not read {Scope} edge-swipe policy: {Message}", GetEdgeSwipePolicyScopeLabel(scope), ex.Message);
            return false;
        }
    }

    private bool TryApplyEdgeSwipePolicy(RegistryKey rootKey, EdgeSwipePolicyScope scope, EdgeSwipePolicyState previousState)
    {
        try
        {
            using var key = rootKey.CreateSubKey(EdgeSwipeRegistryPath, true);
            if (key == null)
                return false;

            var currentValue = key.GetValue(EdgeSwipeValueName);
            var currentState = currentValue == null
                ? previousState
                : new EdgeSwipePolicyState(true, currentValue, key.GetValueKind(EdgeSwipeValueName));

            if (currentState.IsDisabled)
            {
                RememberEdgeSwipeState(scope, currentState, wasModified: false);
                _logger.LogDebug("Edge swipes were already disabled by {Scope} policy", GetEdgeSwipePolicyScopeLabel(scope));
                return true;
            }

            key.SetValue(EdgeSwipeValueName, 0, RegistryValueKind.DWord);
            RememberEdgeSwipeState(scope, currentState, wasModified: true);
            _logger.LogDebug("Edge swipes disabled via {Scope} registry policy", GetEdgeSwipePolicyScopeLabel(scope));
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Could not apply {Scope} edge-swipe policy: {Message}", GetEdgeSwipePolicyScopeLabel(scope), ex.Message);
            return false;
        }
    }

    private void RememberEdgeSwipeState(EdgeSwipePolicyScope scope, EdgeSwipePolicyState state, bool wasModified)
    {
        _edgeSwipePolicyScope = scope;
        _edgeSwipeWasDisabled = wasModified;
        _edgeSwipeValuePreviouslyExisted = state.Exists;
        _previousEdgeSwipeValue = state.Value;
        _previousEdgeSwipeValueKind = state.Kind;
    }

    private void ResetEdgeSwipeTracking()
    {
        _edgeSwipeWasDisabled = false;
        _edgeSwipeValuePreviouslyExisted = false;
        _previousEdgeSwipeValue = null;
        _previousEdgeSwipeValueKind = null;
        _edgeSwipePolicyScope = EdgeSwipePolicyScope.None;
    }

    private static RegistryKey? GetEdgeSwipePolicyRootKey(EdgeSwipePolicyScope scope)
    {
        return scope switch
        {
            EdgeSwipePolicyScope.Machine => Registry.LocalMachine,
            EdgeSwipePolicyScope.User => Registry.CurrentUser,
            _ => null
        };
    }

    private static string GetEdgeSwipePolicyScopeLabel(EdgeSwipePolicyScope scope)
    {
        return scope switch
        {
            EdgeSwipePolicyScope.Machine => "machine-wide",
            EdgeSwipePolicyScope.User => "current-user",
            _ => "unknown"
        };
    }

    internal void EnforceNow() => EnforcementTick();

    private void EnforcementTick()
    {
        if (!_isActive)
            return;

        string? action = null;
        string? result = null;

        if (_hookId == IntPtr.Zero)
        {
            _lastKeyboardHookInstalled = InstallKeyboardHook();
            action = "Keyboard hook";
            result = _lastKeyboardHookInstalled ? "Reinstalled" : "Reinstall failed";
        }

        if (AreAnyTaskbarsVisible())
        {
            HideTaskbars();
            action = "Taskbar";
            result = "Hidden again";
        }

        if (TryHandleForegroundWindow(out var foregroundResult))
        {
            action = "Shell surface";
            result = foregroundResult;
        }

        foreach (var window in _ownedWindows.ToArray())
        {
            if (!window.IsLoaded)
                continue;

            EnsureWindowTopmost(window, activate: false);
            if (window.WindowState == WindowState.Minimized)
            {
                window.WindowState = WindowState.Normal;
                action = "Window state";
                result = $"Restored {window.Title}";
            }
        }

        if (action == null)
        {
            Status.LastSelfHealAction = "Enforcement";
            Status.LastSelfHealResult = "Healthy";
        }
        else
        {
            RecordStatus(action, result ?? "Recovered");
        }

        UpdateStatus();
    }

    private bool TryHandleForegroundWindow(out string? result)
    {
        result = null;

        var snapshot = _platform.TryGetForegroundWindow();
        if (snapshot == null)
            return false;

        var isOwnedWindow = IsOwnedWindowHandle(snapshot.Handle);
        var isConfiguredAppWindow = !isOwnedWindow && IsConfiguredAppWindow(snapshot);
        var decision = _foregroundPolicy.Evaluate(snapshot, isOwnedWindow, isConfiguredAppWindow);

        if (decision.Disposition == KioskForegroundDisposition.Allow)
        {
            _lastBlockedForegroundHandle = IntPtr.Zero;
            RememberAllowedForeground(decision.Description);
            return false;
        }

        if (decision.Disposition != KioskForegroundDisposition.Block)
        {
            _lastBlockedForegroundHandle = IntPtr.Zero;
            return false;
        }

        Status.LastBlockedSurface = $"{decision.SurfaceName}: {decision.Description}";

        var preferredWindow = GetPreferredForegroundWindow();
        if (preferredWindow == null)
        {
            _lastBlockedForegroundHandle = snapshot.Handle;
            result = $"Detected {decision.SurfaceName}: {decision.Description}";
            return true;
        }

        var preferredHandle = new WindowInteropHelper(preferredWindow).Handle;
        if (preferredHandle == IntPtr.Zero)
        {
            _lastBlockedForegroundHandle = snapshot.Handle;
            result = $"Detected {decision.SurfaceName}: {decision.Description}";
            return true;
        }

        _platform.RestoreWindow(preferredHandle);
        EnsureWindowTopmost(preferredWindow, activate: true);
        _platform.SetForegroundWindow(preferredHandle);
        _platform.BringWindowToTop(preferredHandle);

        if (_lastBlockedForegroundHandle != snapshot.Handle)
            Status.ShellInterventionCount++;

        _lastBlockedForegroundHandle = snapshot.Handle;
        result = $"Blocked {decision.SurfaceName}: {decision.Description}";
        return true;
    }

    private bool IsConfiguredAppWindow(KioskWindowSnapshot snapshot)
    {
        var targets = GetConfiguredAppTargets();
        return targets.Count > 0 && _configuredAppMatcher.IsConfiguredAppWindow(snapshot, targets);
    }

    private IReadOnlyCollection<AppProcessTarget> GetConfiguredAppTargets() =>
        _configuredAppTargetsProvider?.Invoke() ?? Array.Empty<AppProcessTarget>();

    private void RememberAllowedForeground(string description)
    {
        if (string.IsNullOrWhiteSpace(description) || Status.LastAllowedForeground == description)
            return;

        Status.LastAllowedForeground = description;
    }

    private Window? GetPreferredForegroundWindow()
    {
        return _ownedWindows
            .Where(window => window.IsLoaded && window.Visibility == Visibility.Visible)
            .OrderByDescending(window => window.IsActive)
            .ThenByDescending(window => window.Topmost)
            .FirstOrDefault();
    }

    private bool IsOwnedWindowHandle(IntPtr hwnd)
    {
        foreach (var window in _ownedWindows)
        {
            if (!window.IsLoaded)
                continue;

            if (new WindowInteropHelper(window).Handle == hwnd)
                return true;
        }

        return false;
    }

    private void EnsureWindowTopmost(Window window, bool activate)
    {
        if (!window.Dispatcher.CheckAccess())
        {
            window.Dispatcher.Invoke(() => EnsureWindowTopmost(window, activate));
            return;
        }

        if (!window.IsLoaded)
            return;

        window.Topmost = true;
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
            return;

        _platform.SetWindowTopmost(handle, activate);
    }

    private bool AreAnyTaskbarsVisible() =>
        _platform.GetTaskbarHandles().Any(handle => _platform.IsWindowVisible(handle));

    private static KioskMachineLockdownResult PreservePreviousMachineState(
        KioskMachineLockdownResult failedResult,
        KioskMachineLockdownResult previousResult) =>
        new()
        {
            State = failedResult.State,
            Supported = previousResult.Supported || failedResult.Supported,
            Configured = previousResult.Configured,
            RestartRequired = previousResult.RestartRequired || failedResult.RestartRequired,
            Message = failedResult.Message,
            TechnicalDetails = failedResult.TechnicalDetails
        };

    private string BuildActivationResult()
    {
        var warnings = new List<string>();

        if (!_lastKeyboardHookInstalled)
            warnings.Add(LocalizationService.T("Msg_KioskWarnKeyboardHook"));

        if (!_lastEdgeSwipePolicyApplied)
            warnings.Add(LocalizationService.T("Msg_KioskWarnEdgeSwipe"));

        var keyboardFilterWarning = BuildKeyboardFilterLimitationMessage();
        if (!string.IsNullOrWhiteSpace(keyboardFilterWarning))
            warnings.Add(keyboardFilterWarning);

        return warnings.Count == 0 ? "Kiosk protection fully active" : string.Join(" ", warnings);
    }

    private string BuildKeyboardFilterStatusMessage()
    {
        return _lastMachineLockdownResult.State switch
        {
            KioskMachineLockdownState.Applied or KioskMachineLockdownState.AlreadyApplied =>
                LocalizationService.T("Msg_KioskKeyboardFilterConfigured"),
            KioskMachineLockdownState.ExternallyManaged when _lastMachineLockdownResult.Configured =>
                LocalizationService.T("Msg_KioskKeyboardFilterExternal"),
            KioskMachineLockdownState.NotConfigured or KioskMachineLockdownState.AlreadyRemoved =>
                LocalizationService.T("Msg_KioskKeyboardFilterNotConfigured"),
            KioskMachineLockdownState.Removed =>
                LocalizationService.T("Msg_KioskKeyboardFilterRemoved"),
            KioskMachineLockdownState.Unsupported =>
                LocalizationService.T("Msg_KioskWarnKeyboardFilterUnsupported"),
            KioskMachineLockdownState.RestartRequired =>
                LocalizationService.T("Msg_KioskWarnKeyboardFilterRestartRequired"),
            KioskMachineLockdownState.Cancelled =>
                LocalizationService.T("Msg_KioskWarnKeyboardFilterCancelled"),
            KioskMachineLockdownState.Failed =>
                LocalizationService.T("Msg_KioskWarnKeyboardFilterFailed"),
            _ => string.Empty
        };
    }

    private string? BuildKeyboardFilterLimitationMessage()
    {
        if (!_lastMachineLockdownResult.HasActivationLimitation)
            return null;

        return _lastMachineLockdownResult.State switch
        {
            KioskMachineLockdownState.Unsupported =>
                LocalizationService.T("Msg_KioskWarnKeyboardFilterUnsupported"),
            KioskMachineLockdownState.RestartRequired =>
                LocalizationService.T("Msg_KioskWarnKeyboardFilterRestartRequired"),
            KioskMachineLockdownState.Cancelled =>
                LocalizationService.T("Msg_KioskWarnKeyboardFilterCancelled"),
            KioskMachineLockdownState.Failed =>
                LocalizationService.T("Msg_KioskWarnKeyboardFilterFailed"),
            _ => null
        };
    }

    private void RecordStatus(string action, string result)
    {
        Status.LastSelfHealAction = action;
        Status.LastSelfHealResult = result;
        _logger.LogDebug("Kiosk status update: {Action} - {Result}", action, result);
    }

    private void UpdateStatus()
    {
        Status.IsActive = _isActive;
        Status.KeyboardHookInstalled = _hookId != IntPtr.Zero && _lastKeyboardHookInstalled;
        Status.TaskbarHidden = _isActive && !AreAnyTaskbarsVisible();
        Status.EdgeSwipePolicyApplied = _lastEdgeSwipePolicyApplied;
        Status.KeyboardFilterSupported = _lastMachineLockdownResult.Supported;
        Status.KeyboardFilterConfigured = _lastMachineLockdownResult.Configured;
        Status.KeyboardFilterRestartRequired = _lastMachineLockdownResult.RestartRequired;
        Status.KeyboardFilterStatusMessage = BuildKeyboardFilterStatusMessage();
        Status.EnforcementLoopRunning = _enforcementTimer.IsEnabled;

        var limitations = new List<string>();
        if (_isActive && !Status.KeyboardHookInstalled)
            limitations.Add(LocalizationService.T("Msg_KioskWarnKeyboardHook"));
        if (_isActive && !Status.EdgeSwipePolicyApplied)
            limitations.Add(LocalizationService.T("Msg_KioskWarnEdgeSwipe"));

        if (_isActive)
        {
            var keyboardFilterWarning = BuildKeyboardFilterLimitationMessage();
            if (!string.IsNullOrWhiteSpace(keyboardFilterWarning))
                limitations.Add(keyboardFilterWarning);
        }

        Status.LimitedProtectionMessage = limitations.Count == 0
            ? string.Empty
            : LocalizationService.T("Msg_KioskPartial", string.Join(" ", limitations));
    }

    private void ResetForegroundDiagnostics()
    {
        _lastBlockedForegroundHandle = IntPtr.Zero;
        Status.LastBlockedSurface = string.Empty;
        Status.LastAllowedForeground = string.Empty;
        Status.ShellInterventionCount = 0;
    }

    private void DeactivateAppLevelOnly()
    {
        _enforcementTimer.Stop();
        RemoveKeyboardHook();
        ShowTaskbars();
        EnableEdgeSwipes();
        _isActive = false;
        _lastBlockedForegroundHandle = IntPtr.Zero;
    }

    public void Dispose()
    {
        DeactivateAppLevelOnly();
        UpdateStatus();
    }
}
