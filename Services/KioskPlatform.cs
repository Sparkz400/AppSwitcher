using System.Runtime.InteropServices;
using System.Text;

namespace AppSwitcher.Services;

internal delegate IntPtr KioskKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

internal sealed record KioskWindowSnapshot(
    IntPtr Handle,
    string WindowClassName,
    string WindowTitle,
    string ProcessName,
    string? ProcessPath);

internal interface IKioskPlatform
{
    bool TryInstallKeyboardHook(KioskKeyboardProc callback, out IntPtr hookHandle);
    void RemoveKeyboardHook(IntPtr hookHandle);
    IReadOnlyList<IntPtr> GetTaskbarHandles();
    bool HideWindow(IntPtr hwnd);
    bool ShowWindow(IntPtr hwnd);
    bool IsWindowVisible(IntPtr hwnd);
    KioskWindowSnapshot? TryGetForegroundWindow();
    bool RestoreWindow(IntPtr hwnd);
    bool SetForegroundWindow(IntPtr hwnd);
    bool BringWindowToTop(IntPtr hwnd);
    bool SetWindowTopmost(IntPtr hwnd, bool activate);
}

internal sealed partial class WindowsKioskPlatform : IKioskPlatform
{
    private const int WH_KEYBOARD_LL = 13;
    private const int SW_HIDE = 0;
    private const int SW_SHOW = 5;
    private const int SW_RESTORE = 9;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private static readonly HashSet<string> TaskbarClasses = new(StringComparer.Ordinal)
    {
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd"
    };

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, KioskKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetModuleHandleW")]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetClassNameW", SetLastError = true)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetWindowTextW", SetLastError = true)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [LibraryImport("user32.dll", EntryPoint = "ShowWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool NativeShowWindow(IntPtr hWnd, int nCmdShow);

    [LibraryImport("user32.dll", EntryPoint = "IsWindowVisible")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool NativeIsWindowVisible(IntPtr hWnd);

    [LibraryImport("user32.dll", EntryPoint = "GetForegroundWindow")]
    private static partial IntPtr NativeGetForegroundWindow();

    [LibraryImport("user32.dll", EntryPoint = "SetForegroundWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool NativeSetForegroundWindow(IntPtr hWnd);

    [LibraryImport("user32.dll", EntryPoint = "BringWindowToTop")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool NativeBringWindowToTop(IntPtr hWnd);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowPos")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool NativeSetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    public bool TryInstallKeyboardHook(KioskKeyboardProc callback, out IntPtr hookHandle)
    {
        hookHandle = IntPtr.Zero;

        using var currentProcess = Process.GetCurrentProcess();
        using var currentModule = currentProcess.MainModule;
        if (currentModule?.ModuleName == null)
            return false;

        hookHandle = SetWindowsHookEx(WH_KEYBOARD_LL, callback, GetModuleHandle(currentModule.ModuleName), 0);
        return hookHandle != IntPtr.Zero;
    }

    public void RemoveKeyboardHook(IntPtr hookHandle)
    {
        if (hookHandle != IntPtr.Zero)
            UnhookWindowsHookEx(hookHandle);
    }

    public IReadOnlyList<IntPtr> GetTaskbarHandles()
    {
        var handles = new List<IntPtr>();

        EnumWindows((hWnd, _) =>
        {
            var className = GetWindowClassName(hWnd);
            if (TaskbarClasses.Contains(className))
                handles.Add(hWnd);

            return true;
        }, IntPtr.Zero);

        return handles;
    }

    public bool HideWindow(IntPtr hwnd) => hwnd != IntPtr.Zero && NativeShowWindow(hwnd, SW_HIDE);

    public bool ShowWindow(IntPtr hwnd) => hwnd != IntPtr.Zero && NativeShowWindow(hwnd, SW_SHOW);

    public bool IsWindowVisible(IntPtr hwnd) => hwnd != IntPtr.Zero && NativeIsWindowVisible(hwnd);

    public KioskWindowSnapshot? TryGetForegroundWindow()
    {
        var handle = NativeGetForegroundWindow();
        if (handle == IntPtr.Zero)
            return null;

        var className = GetWindowClassName(handle);
        var title = GetWindowTitle(handle);
        GetWindowThreadProcessId(handle, out var processId);

        string processName = string.Empty;
        string? processPath = null;

        if (processId != 0)
        {
            try
            {
                using var process = Process.GetProcessById((int)processId);
                processName = process.ProcessName;

                try
                {
                    processPath = process.MainModule?.FileName;
                }
                catch (Exception)
                {
                    processPath = null;
                }
            }
            catch
            {
                processName = string.Empty;
                processPath = null;
            }
        }

        return new KioskWindowSnapshot(handle, className, title, processName, processPath);
    }

    public bool RestoreWindow(IntPtr hwnd) => hwnd != IntPtr.Zero && NativeShowWindow(hwnd, SW_RESTORE);

    public bool SetForegroundWindow(IntPtr hwnd) => hwnd != IntPtr.Zero && NativeSetForegroundWindow(hwnd);

    public bool BringWindowToTop(IntPtr hwnd) => hwnd != IntPtr.Zero && NativeBringWindowToTop(hwnd);

    public bool SetWindowTopmost(IntPtr hwnd, bool activate)
    {
        if (hwnd == IntPtr.Zero)
            return false;

        var flags = SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW;
        if (!activate)
            flags |= SWP_NOACTIVATE;

        return NativeSetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, flags);
    }

    private static string GetWindowClassName(IntPtr hwnd)
    {
        var builder = new StringBuilder(256);
        return GetClassName(hwnd, builder, builder.Capacity) > 0
            ? builder.ToString()
            : string.Empty;
    }

    private static string GetWindowTitle(IntPtr hwnd)
    {
        var builder = new StringBuilder(512);
        return GetWindowText(hwnd, builder, builder.Capacity) > 0
            ? builder.ToString()
            : string.Empty;
    }
}
