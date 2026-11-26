namespace AppSwitcher.Services;

public enum NotificationType { Info, Warning, Error }

public class NotificationService
{
    // Toasts show one at a time for several seconds; without a cap, repeating warnings would queue up for hours.
    internal const int MaxQueued = 20;
    private readonly Queue<(string message, NotificationType type)> _queue = new();
    private bool _isShowing;

    public void Show(string message, NotificationType type = NotificationType.Info)
    {
        UiDispatcher.TryBeginInvoke(() =>
        {
            if (_queue.Contains((message, type))) return;
            if (_queue.Count >= MaxQueued) _queue.Dequeue();
            _queue.Enqueue((message, type));
            if (!_isShowing) ShowNext();
        });
    }

    private void ShowNext()
    {
        if (_queue.Count == 0) { _isShowing = false; return; }
        _isShowing = true;

        if (Application.Current?.Dispatcher is not { HasShutdownStarted: false, HasShutdownFinished: false })
        {
            _queue.Clear();
            _isShowing = false;
            return;
        }

        var (message, type) = _queue.Dequeue();
        var toast = new ToastWindow(message, type);
        toast.Closed += (_, _) => ShowNext();
        toast.Show();
    }
}
