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

    private static readonly ConcurrentDictionary<string, ImageSource> _iconCache = new();
    private static readonly ConcurrentQueue<string> _cacheOrder = new();

    public object? Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2) return null;

        string? inputPath = values[0] as string;
        string? customIconPath = values[1] as string;

        if (!string.IsNullOrEmpty(customIconPath) && File.Exists(customIconPath))
        {
            if (_iconCache.TryGetValue(customIconPath, out var cachedCustom)) return cachedCustom;
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(customIconPath);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                AddToCache(customIconPath, bmp);
                return bmp;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load custom icon '{customIconPath}': {ex.Message}");
            }
        }

        if (string.IsNullOrWhiteSpace(inputPath)) return null;

        string? fullPath = ResolvePath(inputPath);

        if (string.IsNullOrEmpty(fullPath) || !File.Exists(fullPath))
            return null;

        if (_iconCache.TryGetValue(fullPath, out var cachedIcon))
            return cachedIcon;

        ImageSource? sysIcon = GetIconFromPath(fullPath);

        if (sysIcon != null)
        {
            AddToCache(fullPath, sysIcon);
            return sysIcon;
        }

        return null;
    }

    private static void AddToCache(string key, ImageSource icon)
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
