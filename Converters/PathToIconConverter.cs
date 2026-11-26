using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AppSwitcher.Converters;

public partial class PathToIconConverter : IMultiValueConverter
{
    private const int MaxCacheSize = 128;

    // A missing icon is looked up again after this long, so a file that appears later still gets its icon.
    internal static readonly TimeSpan MissRetry = TimeSpan.FromSeconds(60);

    private sealed record CacheEntry(ImageSource? Icon, long RetryAfterTicks);

    private static readonly ConcurrentDictionary<string, CacheEntry> _iconCache = new();
    private static readonly ConcurrentQueue<string> _cacheOrder = new();

    public object? Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2) return null;

        string? inputPath = values[0] as string;
        string? customIconPath = values[1] as string;
        if (string.IsNullOrWhiteSpace(inputPath) && string.IsNullOrEmpty(customIconPath)) return null;

        // Runs on the UI thread for every launcher row on every rebuild (each filter keystroke). Check the cache, misses
        // included, before touching the disk: File.Exists on an offline network path can block for seconds.
        var key = $"{inputPath}|{customIconPath}";
        if (_iconCache.TryGetValue(key, out var cached) && (cached.Icon != null || Environment.TickCount64 < cached.RetryAfterTicks))
            return cached.Icon;

        var icon = LoadCustomIcon(customIconPath) ?? LoadExecutableIcon(inputPath);
        AddToCache(key, new CacheEntry(icon, Environment.TickCount64 + (long)MissRetry.TotalMilliseconds));
        return icon;
    }

    private static ImageSource? LoadCustomIcon(string? customIconPath)
    {
        if (string.IsNullOrEmpty(customIconPath) || !File.Exists(customIconPath)) return null;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(customIconPath);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load custom icon '{customIconPath}': {ex.Message}");
            return null;
        }
    }

    private static ImageSource? LoadExecutableIcon(string? inputPath)
    {
        if (string.IsNullOrWhiteSpace(inputPath)) return null;
        var fullPath = ResolvePath(inputPath);
        return string.IsNullOrEmpty(fullPath) || !File.Exists(fullPath) ? null : GetIconFromPath(fullPath);
    }

    private static void AddToCache(string key, CacheEntry icon)
    {
        _iconCache[key] = icon;
        _cacheOrder.Enqueue(key);

        // Evict oldest entries when cache exceeds max size
        while (_iconCache.Count > MaxCacheSize && _cacheOrder.TryDequeue(out var evictKey))
        {
            _iconCache.TryRemove(evictKey, out _);
        }
    }

    private static string? ResolvePath(string fileName)
    {
        if (File.Exists(fileName)) return Path.GetFullPath(fileName);

        if (!Path.HasExtension(fileName)) fileName += ".exe";

        var values = Environment.GetEnvironmentVariable("PATH");
        if (values != null)
        {
            foreach (var path in values.Split(Path.PathSeparator))
            {
                try
                {
                    var fullPath = Path.Combine(path, fileName);
                    if (File.Exists(fullPath)) return fullPath;
                }
                catch
                {
                    // Invalid PATH entry, skip
                }
            }
        }

        return null;
    }


    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotImplementedException();

    private static ImageSource? GetIconFromPath(string path)
    {
        IntPtr hIcon = IntPtr.Zero;
        try
        {
            SHFILEINFO shinfo = new();
            IntPtr result = SHGetFileInfo(path, 0, ref shinfo, (uint)Marshal.SizeOf(shinfo), SHGFI_ICON | SHGFI_LARGEICON);

            if (result == IntPtr.Zero || shinfo.hIcon == IntPtr.Zero) return null;
            hIcon = shinfo.hIcon;

            var imageSource = Imaging.CreateBitmapSourceFromHIcon(hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            imageSource.Freeze();
            return imageSource;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to extract icon from '{path}': {ex.Message}");
            return null;
        }
        finally { if (hIcon != IntPtr.Zero) DestroyIcon(hIcon); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    };

    private const uint SHGFI_ICON = 0x100;
    private const uint SHGFI_LARGEICON = 0x0;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(IntPtr hIcon);
}
