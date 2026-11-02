namespace AppSwitcher.Services;

internal enum KioskForegroundDisposition
{
    Ignore,
    Allow,
    Block
}

internal sealed record KioskForegroundDecision(
    KioskForegroundDisposition Disposition,
    string SurfaceName,
    string Description)
{
    public static readonly KioskForegroundDecision IgnoreDecision =
        new(KioskForegroundDisposition.Ignore, string.Empty, string.Empty);
}

internal sealed class KioskForegroundPolicy
{
    private static readonly HashSet<string> TaskbarClasses = new(StringComparer.Ordinal)
    {
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd"
    };

    private static readonly HashSet<string> DesktopClasses = new(StringComparer.Ordinal)
    {
        "Progman",
        "WorkerW"
    };

    private static readonly HashSet<string> ShellSurfaceClasses = new(StringComparer.Ordinal)
    {
        "DV2ControlHost",
        "MultitaskingViewFrame",
        "TaskSwitcherWnd",
        "Windows.UI.Core.CoreWindow",
        "XamlExplorerHostIslandWindow"
    };

    private static readonly HashSet<string> ShellProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer",
        "ShellExperienceHost",
        "StartMenuExperienceHost",
        "SearchHost"
    };

    public KioskForegroundDecision Evaluate(
        KioskWindowSnapshot snapshot,
        bool isOwnedWindow,
        bool isConfiguredAppWindow)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var description = Describe(snapshot);

        if (isOwnedWindow)
            return new(KioskForegroundDisposition.Allow, "AppSwitcher", description);

        if (isConfiguredAppWindow)
            return new(KioskForegroundDisposition.Allow, "Configured app", description);

        if (TaskbarClasses.Contains(snapshot.WindowClassName))
            return new(KioskForegroundDisposition.Block, "Taskbar", description);

        if (DesktopClasses.Contains(snapshot.WindowClassName))
            return new(KioskForegroundDisposition.Block, "Shell desktop", description);

        if (IsShellSurface(snapshot))
            return new(KioskForegroundDisposition.Block, "Shell surface", description);

        return KioskForegroundDecision.IgnoreDecision;
    }

    internal static string Describe(KioskWindowSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(snapshot.WindowTitle))
            parts.Add(snapshot.WindowTitle.Trim());

        if (!string.IsNullOrWhiteSpace(snapshot.ProcessName))
            parts.Add(snapshot.ProcessName.Trim());

        if (!string.IsNullOrWhiteSpace(snapshot.WindowClassName))
            parts.Add(snapshot.WindowClassName.Trim());

        return parts.Count == 0
            ? "Unnamed window"
            : string.Join(" / ", parts.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static bool IsShellSurface(KioskWindowSnapshot snapshot)
    {
        if (!ShellProcesses.Contains(snapshot.ProcessName))
            return false;

        if (ShellSurfaceClasses.Contains(snapshot.WindowClassName))
            return true;

        return snapshot.WindowTitle.Contains("Start", StringComparison.OrdinalIgnoreCase)
               || snapshot.WindowTitle.Contains("Search", StringComparison.OrdinalIgnoreCase)
               || snapshot.WindowTitle.Contains("Task View", StringComparison.OrdinalIgnoreCase);
    }
}
