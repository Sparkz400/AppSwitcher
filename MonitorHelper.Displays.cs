using System.Runtime.InteropServices;

namespace AppSwitcher;

public sealed record DisplayInfo(int Index, string DeviceId, string Name, Rect Bounds, Rect WorkArea, bool IsPrimary, double DpiScale = 1)
{
    public Rect WpfWorkArea => new(WorkArea.X / DpiScale, WorkArea.Y / DpiScale, WorkArea.Width / DpiScale, WorkArea.Height / DpiScale);
}

public static partial class MonitorHelper
{
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfoExtended(IntPtr monitor, ref MONITORINFOEX info);

    [DllImport("user32.dll", EntryPoint = "EnumDisplayDevicesW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayDevices(string device, uint index, ref DISPLAY_DEVICE info, uint flags);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public uint Size;
        public RECT Bounds, WorkArea;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAY_DEVICE
    {
        public uint Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    public static IReadOnlyList<DisplayInfo> GetDisplays()
    {
        var displays = new List<DisplayInfo>();
        var scale = Math.Max(1, GetDpiScale());
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr handle, IntPtr _, ref RECT bounds, IntPtr data) =>
        {
            var info = new MONITORINFOEX { Size = (uint)Marshal.SizeOf<MONITORINFOEX>() };
            if (!GetMonitorInfoExtended(handle, ref info)) return true;
            var attached = new List<DISPLAY_DEVICE>();
            for (uint index = 0; ; index++)
            {
                var device = new DISPLAY_DEVICE { Size = (uint)Marshal.SizeOf<DISPLAY_DEVICE>() };
                if (!EnumDisplayDevices(info.DeviceName, index, ref device, 1)) break; // EDD_GET_DEVICE_INTERFACE_NAME
                if ((device.Flags & 1) != 0) attached.Add(device);
            }
            // A cloned desktop may represent multiple panels; never invent a unique identity for it.
            var identity = attached.Count == 1 ? attached[0].DeviceId : "";
            var name = attached.Count == 1 ? attached[0].DeviceString : info.DeviceName;
            displays.Add(new(displays.Count + 1, identity ?? "", string.IsNullOrWhiteSpace(name) ? info.DeviceName : name,
                ToWpfRect(info.Bounds, 1), ToWpfRect(info.WorkArea, 1), (info.Flags & 1) != 0, scale));
            return true;
        }, IntPtr.Zero);
        return displays;
    }

    internal static int FindConfiguredDisplay(int index, string? identity, IReadOnlyList<DisplayInfo> displays)
    {
        if (string.IsNullOrEmpty(identity)) return displays.Any(display => display.Index == index) ? index : 0;
        var matches = displays.Where(display => string.Equals(display.DeviceId, identity, StringComparison.OrdinalIgnoreCase)).ToArray();
        return matches.Length == 1 ? matches[0].Index : 0;
    }

    internal static DisplayInfo? ResolveDisplay(int index, string? identity, IReadOnlyList<DisplayInfo> displays)
    {
        var resolved = FindConfiguredDisplay(index, identity, displays);
        return displays.FirstOrDefault(display => display.Index == resolved)
            ?? displays.FirstOrDefault(display => display.IsPrimary) ?? displays.FirstOrDefault();
    }
}
