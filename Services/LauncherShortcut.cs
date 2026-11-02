using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace AppSwitcher.Services;

/// <summary>One optional Windows hotkey, owned by the launcher HWND and released on shutdown.</summary>
internal sealed partial class LauncherShortcut : IDisposable
{
    private const int HotkeyId = 0x4153;
    private readonly HwndSource _source;
    private readonly Action _show;
    private bool _registered;
    public LauncherShortcut(Window window, Action show)
    {
        _source = HwndSource.FromHwnd(new WindowInteropHelper(window).EnsureHandle());
        _show = show;
        _source.AddHook(Handle);
    }
    public bool Configure(bool enabled)
    {
        if (_source.IsDisposed) return !enabled;
        if (_registered) UnregisterHotKey(_source.Handle, HotkeyId);
        _registered = enabled && RegisterHotKey(_source.Handle, HotkeyId, 0x0001 | 0x0002 | 0x4000, 0x20); // Ctrl+Alt+Space, no repeat
        return !enabled || _registered;
    }
    private IntPtr Handle(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x0312 && wParam.ToInt32() == HotkeyId) { handled = true; _show(); }
        return IntPtr.Zero;
    }
    public void Dispose()
    {
        if (_source.IsDisposed) return;
        if (_registered) UnregisterHotKey(_source.Handle, HotkeyId);
        _registered = false;
        _source.RemoveHook(Handle);
    }
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterHotKey(IntPtr hwnd, int id);
}
