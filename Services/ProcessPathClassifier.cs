namespace AppSwitcher.Services;

internal static class ProcessPathClassifier
{
    public static bool IsNetworkPath(string path) =>
        IsNetworkPath(path, root => new DriveInfo(root).DriveType);

    internal static bool IsNetworkPath(string path, Func<string, DriveType> driveTypeResolver)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        if (path.StartsWith(@"\\", StringComparison.Ordinal))
            return true;

        try
        {
            if (!Path.IsPathRooted(path))
                return false;

            var root = Path.GetPathRoot(path);
            if (string.IsNullOrWhiteSpace(root) || root.Length < 3 || root[1] != ':')
                return false;

            return driveTypeResolver(root) == DriveType.Network;
        }
        catch
        {
            return false;
        }
    }
}
