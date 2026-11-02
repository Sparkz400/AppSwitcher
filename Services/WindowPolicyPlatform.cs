using System.Runtime.InteropServices;

namespace AppSwitcher.Services;

internal readonly record struct NativeRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Math.Max(0, Right - Left);
    public int Height => Math.Max(0, Bottom - Top);

    public static NativeRect FromPositionAndSize(int x, int y, int width, int height) =>
        new(x, y, x + Math.Max(0, width), y + Math.Max(0, height));
}

internal readonly record struct NativeMonitorInfo(int Index, NativeRect Bounds, NativeRect WorkArea, bool IsPrimary, string DeviceId = "");

internal readonly record struct WindowStyleState(nint Style, nint ExStyle);

public interface IWindowHandleResolver
{
    IntPtr GetMainWindowHandle(AppProcessTarget target);
}

internal interface IWindowPolicyPlatform
{
    bool IsWindow(IntPtr hwnd);
    bool IsWindowMinimized(IntPtr hwnd);
    bool RestoreWindow(IntPtr hwnd);
    bool MaximizeWindow(IntPtr hwnd);
    bool TryGetWindowRect(IntPtr hwnd, out NativeRect rect);
    bool SetWindowRect(IntPtr hwnd, NativeRect rect, bool activate);
    bool IsTopmost(IntPtr hwnd);
    bool SetTopmost(IntPtr hwnd, bool topmost, bool activate);
    bool TryGetWindowStyle(IntPtr hwnd, out WindowStyleState style);
    bool SetWindowStyle(IntPtr hwnd, WindowStyleState style);
    IReadOnlyList<NativeMonitorInfo> GetMonitors();
    int GetCurrentMonitorIndex(IntPtr hwnd);
}

internal sealed partial class WindowsWindowPolicyPlatform : IWindowPolicyPlatform
{
    private const int MonitorDefaultToNearest = 2;
    private const int SwRestore = 9;
    private const int SwMaximize = 3;
    private const int GwlStyle = -16;
    private const int GwlExStyle = -20;

    private const nint WsCaption = 0x00C00000;
    private const nint WsThickFrame = 0x00040000;
    private const nint WsSysMenu = 0x00080000;
    private const nint WsMinimizeBox = 0x00020000;
    private const nint WsMaximizeBox = 0x00010000;
    private static readonly nint WsPopup = unchecked((nint)0x80000000);

    private const nint WsExDlgModalFrame = 0x00000001;
    private const nint WsExWindowEdge = 0x00000100;
    private const nint WsExClientEdge = 0x00000200;
    private const nint WsExStaticEdge = 0x00020000;
    private const nint WsExTopmost = 0x00000008;

    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private const uint SwpShowWindow = 0x0040;

    private static readonly IntPtr HwndTopmost = new(-1);
    private static readonly IntPtr HwndNotTopmost = new(-2);

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindow(IntPtr hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsIconic(IntPtr hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(IntPtr hwnd, int nCmdShow);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(IntPtr hwnd, out RECT rect);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(
        IntPtr hwnd,
        IntPtr hwndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint flags);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static partial nint GetWindowLongPtr(IntPtr hwnd, int index);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static partial nint SetWindowLongPtr(IntPtr hwnd, int index, nint newLong);

    [LibraryImport("user32.dll")]
    private static partial IntPtr MonitorFromWindow(IntPtr hwnd, int flags);

    bool IWindowPolicyPlatform.IsWindow(IntPtr hwnd) => hwnd != IntPtr.Zero && IsWindow(hwnd);

    public bool IsWindowMinimized(IntPtr hwnd) => hwnd != IntPtr.Zero && IsIconic(hwnd);

    public bool RestoreWindow(IntPtr hwnd) => hwnd != IntPtr.Zero && ShowWindow(hwnd, SwRestore);

    public bool MaximizeWindow(IntPtr hwnd) => hwnd != IntPtr.Zero && ShowWindow(hwnd, SwMaximize);

    public bool TryGetWindowRect(IntPtr hwnd, out NativeRect rect)
    {
        rect = default;
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var rawRect))
            return false;

        rect = new NativeRect(rawRect.Left, rawRect.Top, rawRect.Right, rawRect.Bottom);
        return true;
    }

    public bool SetWindowRect(IntPtr hwnd, NativeRect rect, bool activate)
    {
        if (hwnd == IntPtr.Zero)
            return false;

        var flags = SwpNoZOrder | SwpShowWindow;
        if (!activate)
            flags |= SwpNoActivate;

        return SetWindowPos(hwnd, IntPtr.Zero, rect.Left, rect.Top, rect.Width, rect.Height, flags);
    }

    public bool IsTopmost(IntPtr hwnd) =>
        TryGetWindowStyle(hwnd, out var style) && (style.ExStyle & WsExTopmost) == WsExTopmost;

    public bool SetTopmost(IntPtr hwnd, bool topmost, bool activate)
    {
        if (hwnd == IntPtr.Zero)
            return false;

        var flags = SwpNoMove | SwpNoSize | SwpShowWindow;
        if (!activate)
            flags |= SwpNoActivate;

        return SetWindowPos(hwnd, topmost ? HwndTopmost : HwndNotTopmost, 0, 0, 0, 0, flags);
    }

    public bool TryGetWindowStyle(IntPtr hwnd, out WindowStyleState style)
    {
        style = default;
        if (hwnd == IntPtr.Zero)
            return false;

        style = new WindowStyleState(
            GetWindowLongPtr(hwnd, GwlStyle),
            GetWindowLongPtr(hwnd, GwlExStyle));
        return true;
    }

    public bool SetWindowStyle(IntPtr hwnd, WindowStyleState style)
    {
        if (hwnd == IntPtr.Zero)
            return false;

        SetWindowLongPtr(hwnd, GwlStyle, style.Style);
        SetWindowLongPtr(hwnd, GwlExStyle, style.ExStyle);

        return TryGetWindowRect(hwnd, out var rect)
            && SetWindowPos(
                hwnd,
                IntPtr.Zero,
                rect.Left,
                rect.Top,
                rect.Width,
                rect.Height,
                SwpNoZOrder | SwpNoActivate | SwpShowWindow | SwpFrameChanged);
    }

    public IReadOnlyList<NativeMonitorInfo> GetMonitors()
        => MonitorHelper.GetDisplays().Select(display => new NativeMonitorInfo(display.Index,
            NativeRect.FromPositionAndSize((int)display.Bounds.X, (int)display.Bounds.Y, (int)display.Bounds.Width, (int)display.Bounds.Height),
            NativeRect.FromPositionAndSize((int)display.WorkArea.X, (int)display.WorkArea.Y, (int)display.WorkArea.Width, (int)display.WorkArea.Height),
            display.IsPrimary, display.DeviceId)).ToArray();

    public int GetCurrentMonitorIndex(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return 0;

        var targetMonitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        if (targetMonitor == IntPtr.Zero)
            return 0;

        var monitors = new List<(IntPtr Handle, int Index)>();
        var index = 1;

        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr hdcMonitor, ref RECT monitorRect, IntPtr userData) =>
        {
            monitors.Add((hMonitor, index++));
            return true;
        }, IntPtr.Zero);

        return monitors.FirstOrDefault(item => item.Handle == targetMonitor).Index;
    }

    public static WindowStyleState BuildBorderlessFullscreenStyle(WindowStyleState originalStyle)
    {
        var style = originalStyle.Style;
        style &= ~((nint)(WsCaption | WsThickFrame | WsSysMenu | WsMinimizeBox | WsMaximizeBox));
        style |= (nint)WsPopup;

        var exStyle = originalStyle.ExStyle;
        exStyle &= ~((nint)(WsExDlgModalFrame | WsExWindowEdge | WsExClientEdge | WsExStaticEdge));

        return new WindowStyleState(style, exStyle);
    }

    private static NativeRect ToNativeRect(RECT rect) =>
        new(rect.Left, rect.Top, rect.Right, rect.Bottom);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

}
