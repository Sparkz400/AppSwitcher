namespace AppSwitcher.Services;

internal static class UiDispatcher
{
    public static bool TryBeginInvoke(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
            return false;

        if (dispatcher.CheckAccess())
        {
            action();
            return true;
        }

        _ = dispatcher.InvokeAsync(() =>
        {
            if (!dispatcher.HasShutdownStarted && !dispatcher.HasShutdownFinished)
                action();
        });

        return true;
    }
}
