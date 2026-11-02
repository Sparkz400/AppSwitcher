namespace AppSwitcher.Services;

internal interface IKioskConfiguredAppMatcher
{
    bool IsConfiguredAppWindow(KioskWindowSnapshot snapshot, IReadOnlyCollection<AppProcessTarget> targets);
}

internal sealed class ProcessHelperKioskConfiguredAppMatcher(ProcessHelper processHelper) : IKioskConfiguredAppMatcher
{
    private readonly ProcessHelper _processHelper = processHelper;

    public bool IsConfiguredAppWindow(KioskWindowSnapshot snapshot, IReadOnlyCollection<AppProcessTarget> targets)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(targets);

        return _processHelper.IsWindowMatchedByAnyTarget(snapshot, targets);
    }
}
