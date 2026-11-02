using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Microsoft.Win32.SafeHandles;

namespace AppSwitcher.Services;

internal enum OpenWindowKind { Native, Website, Unsupported, Unavailable }

internal sealed record OpenWindowCandidate(nint Handle, int ProcessId, long ProcessCreated,
    string ExecutablePath, string Title, string AppName, OpenWindowKind Kind, BrowserKind? Browser = null)
{
    public ImageSource? Icon { get; init; }
}

internal interface IOpenWindowCatalog
{
    Task<IReadOnlyList<OpenWindowCandidate>> ScanAsync(CancellationToken cancellationToken);
    Task<OpenWindowCandidate?> RevalidateAsync(OpenWindowCandidate candidate, CancellationToken cancellationToken);
}

/// <summary>On-demand desktop snapshots only. No process hooks, polling, WMI or command-line capture.</summary>
internal sealed class OpenWindowCatalog : IOpenWindowCatalog
{
    private const int GwlStyle = -16, GwlExStyle = -20;
    private const uint WsChild = 0x40000000, WsExToolWindow = 0x80, WsExAppWindow = 0x40000, WsExNoActivate = 0x8000000;
    private static readonly HashSet<string> ShellClasses = new(StringComparer.OrdinalIgnoreCase)
        { "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "#32770" };
    private static readonly HashSet<string> UnsupportedExecutables = new(StringComparer.OrdinalIgnoreCase)
        { "applicationframehost", "dllhost", "rundll32", "wscript", "cscript", "conhost", "openconsole",
          "dotnet", "java", "javaw", "python", "pythonw", "node", "electron", "brave", "opera", "vivaldi", "chromium", "iexplore" };

    public Task<IReadOnlyList<OpenWindowCandidate>> ScanAsync(CancellationToken cancellationToken) => Task.Run<IReadOnlyList<OpenWindowCandidate>>(() =>
    {
        var candidates = new List<OpenWindowCandidate>();
        var succeeded = EnumWindows((handle, _) =>
        {
            if (cancellationToken.IsCancellationRequested) return false;
            var candidate = Read(handle, includeIcon: true);
            if (candidate != null) candidates.Add(candidate);
            return true;
        }, 0);
        cancellationToken.ThrowIfCancellationRequested();
        if (!succeeded) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        return candidates.OrderBy(candidate => candidate.AppName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(candidate => candidate.Title, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }, cancellationToken);

    public Task<OpenWindowCandidate?> RevalidateAsync(OpenWindowCandidate candidate, CancellationToken cancellationToken) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        var current = Read(candidate.Handle, includeIcon: false);
        return current != null && SameIdentity(candidate, current) ? current : null;
    }, cancellationToken);

    internal static bool SameIdentity(OpenWindowCandidate expected, OpenWindowCandidate current) =>
        expected.Handle != 0 && expected.ProcessCreated > 0 && expected.Handle == current.Handle
        && expected.ProcessId == current.ProcessId && expected.ProcessCreated == current.ProcessCreated
        && expected.Kind == current.Kind && current.Kind is OpenWindowKind.Native or OpenWindowKind.Website
        && SameExecutable(expected.ExecutablePath, current.ExecutablePath);

    internal static bool SameExecutable(string left, string right)
    {
        // Do not resolve aliases via PATH or a filename fallback: two copies of an app may differ.
        if (!Path.IsPathFullyQualified(left) || !Path.IsPathFullyQualified(right)) return false;
        try { return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }

    internal static (OpenWindowKind Kind, BrowserKind? Browser) Classify(string path)
    {
        if (!Path.IsPathFullyQualified(path) || !string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase))
            return (OpenWindowKind.Unavailable, null);
        var name = Path.GetFileNameWithoutExtension(path);
        var browser = name.ToLowerInvariant() switch { "chrome" => (BrowserKind?)BrowserKind.Chrome, "msedge" => BrowserKind.Edge, "firefox" => BrowserKind.Firefox, _ => null };
        if (browser != null) return (OpenWindowKind.Website, browser);
        if (UnsupportedExecutables.Contains(name) || path.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase))
            return (OpenWindowKind.Unsupported, null);
        return (OpenWindowKind.Native, null);
    }

    private static OpenWindowCandidate? Read(nint handle, bool includeIcon)
    {
        GetWindowThreadProcessId(handle, out var pid);
        if (pid == 0 || pid == Environment.ProcessId || !IsWindowVisible(handle)) return null;
        var style = unchecked((uint)GetWindowLong(handle, GwlStyle));
        var extendedStyle = unchecked((uint)GetWindowLong(handle, GwlExStyle));
        if ((style & WsChild) != 0 || (extendedStyle & (WsExToolWindow | WsExNoActivate)) != 0
            || (GetWindow(handle, 4) != 0 && (extendedStyle & WsExAppWindow) == 0)) return null;
        if (DwmGetWindowAttribute(handle, 14, out var cloaked, sizeof(int)) == 0 && cloaked != 0) return null;
        var className = new StringBuilder(256);
        GetClassName(handle, className, className.Capacity);
        if (ShellClasses.Contains(className.ToString())) return null;
        // Foreign-process caption reads do not send blocking WM_GETTEXT requests to the app.
        var title = new StringBuilder(2048);
        if (GetWindowText(handle, title, title.Capacity) == 0 || string.IsNullOrWhiteSpace(title.ToString())) return null;

        string path = "";
        long created = 0;
        using (var process = OpenProcess(0x1000 /* QUERY_LIMITED_INFORMATION */, false, pid))
        {
            if (!process.IsInvalid && GetProcessTimes(process, out created, out _, out _, out _))
            {
                var imagePath = new StringBuilder(32768);
                var length = imagePath.Capacity;
                if (QueryFullProcessImageName(process, 0, imagePath, ref length)) path = imagePath.ToString();
            }
            GetWindowThreadProcessId(handle, out var ownerNow);
            if (ownerNow != pid || !IsWindowVisible(handle)) return null;
        }
        if (string.Equals(Path.GetFileNameWithoutExtension(path), "AppSwitcher", StringComparison.OrdinalIgnoreCase)) return null;
        var (kind, browser) = created > 0 ? Classify(path) : (OpenWindowKind.Unavailable, (BrowserKind?)null);
        return new(handle, (int)pid, created, path, title.ToString(),
            string.IsNullOrEmpty(path) ? "" : Path.GetFileNameWithoutExtension(path), kind, browser)
        { Icon = includeIcon ? ReadClassIcon(handle) : null };
    }

    private static ImageSource? ReadClassIcon(nint handle)
    {
        // Copy a class-owned icon; never send WM_GETICON or read executable files on a network share.
        var borrowed = IntPtr.Size == 8 ? GetClassLongPtr(handle, -34) : (nint)GetClassLong(handle, -34);
        if (borrowed == 0) borrowed = IntPtr.Size == 8 ? GetClassLongPtr(handle, -14) : (nint)GetClassLong(handle, -14);
        if (borrowed == 0) return null;
        var owned = CopyIcon(borrowed);
        if (owned == 0) return null;
        try
        {
            var image = Imaging.CreateBitmapSourceFromHIcon(owned, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(32, 32));
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is ArgumentException or System.ComponentModel.Win32Exception or COMException) { return null; }
        finally { DestroyIcon(owned); }
    }

    private delegate bool EnumWindowProc(nint handle, nint parameter);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnumWindows(EnumWindowProc callback, nint parameter);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(nint handle);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint handle, out uint processId);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(nint handle, int index);
    [DllImport("user32.dll")] private static extern nint GetWindow(nint handle, uint command);
    [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint handle, StringBuilder name, int count);
    [DllImport("user32.dll", EntryPoint = "GetWindowTextW", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint handle, StringBuilder title, int count);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint handle, int attribute, out int value, int size);
    [DllImport("kernel32.dll")] private static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint processId);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetProcessTimes(SafeProcessHandle process, out long created, out long exited, out long kernel, out long user);
    [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, StringBuilder path, ref int size);
    [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")] private static extern nint GetClassLongPtr(nint handle, int index);
    [DllImport("user32.dll", EntryPoint = "GetClassLongW")] private static extern uint GetClassLong(nint handle, int index);
    [DllImport("user32.dll")] private static extern nint CopyIcon(nint icon);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyIcon(nint icon);
}
