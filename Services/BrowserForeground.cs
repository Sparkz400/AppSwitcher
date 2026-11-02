using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace AppSwitcher.Services;

internal static class BrowserForeground
{
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint processId);
    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(IntPtr process, int infoClass, out ProcessInformation information, int size, out int returned);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(uint processId);
    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr Reserved1, Peb, Reserved2, Reserved3, ProcessId, ParentId;
    }

    // Resolve the browser from the OS-authenticated pipe client, never from extension-supplied PIDs.
    internal static uint FindBrowserProcess(SafePipeHandle pipe, BrowserKind browser)
    {
        var expected = browser switch { BrowserKind.Chrome => "chrome", BrowserKind.Edge => "msedge", _ => "firefox" };
        try
        {
            if (!GetNamedPipeClientProcessId(pipe, out var id)) return 0;
            for (var depth = 0; id != 0 && depth < 8; depth++)
            {
                using var process = Process.GetProcessById(checked((int)id));
                if (process.ProcessName.Equals(expected, StringComparison.OrdinalIgnoreCase)) return id;
                if (NtQueryInformationProcess(process.Handle, 0, out var info, Marshal.SizeOf<ProcessInformation>(), out _) != 0) return 0;
                id = checked((uint)info.ParentId.ToInt64());
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception or OverflowException) { }
        return 0;
    }

    internal static void Allow(uint processId)
    {
        if (processId != 0) AllowSetForegroundWindow(processId);
    }
}
