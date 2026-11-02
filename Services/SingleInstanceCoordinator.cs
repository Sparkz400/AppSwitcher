using System.IO.Pipes;
using System.Security.Principal;

namespace AppSwitcher.Services;

/// <summary>Owns the instance lock and an activation-only, same-user/session channel.</summary>
internal sealed class SingleInstanceCoordinator : IDisposable
{
    private readonly Mutex _mutex;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _stop = new();
    private Task? _listener;
    public bool IsPrimary { get; }

    public SingleInstanceCoordinator(string? suffix = null)
    {
        _pipeName = suffix ?? $"AppSwitcher.Activate.{WindowsIdentity.GetCurrent().User!.Value}.{Process.GetCurrentProcess().SessionId}";
        _mutex = new Mutex(false, suffix == null ? "AppSwitcher_SingleInstance_SCADA" : suffix);
        try { IsPrimary = _mutex.WaitOne(0); }
        catch (AbandonedMutexException) { IsPrimary = true; }
    }

    public void Listen(Action activate)
    {
        if (!IsPrimary || _listener != null) throw new InvalidOperationException();
        _listener = Task.Run(async () =>
        {
            while (!_stop.IsCancellationRequested)
            {
                try
                {
                    using var pipe = new NamedPipeServerStream(_pipeName, PipeDirection.InOut, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await pipe.WaitForConnectionAsync(_stop.Token).ConfigureAwait(false);
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                    timeout.CancelAfter(TimeSpan.FromSeconds(3));
                    await pipe.WriteAsync(BitConverter.GetBytes(Environment.ProcessId), timeout.Token).ConfigureAwait(false);
                    var command = new byte[1];
                    await pipe.ReadExactlyAsync(command, timeout.Token).ConfigureAwait(false);
                    if (command[0] != 1) continue;
                    activate();
                    await pipe.WriteAsync(new byte[] { 1 }, timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { }
                catch (IOException) { if (!_stop.IsCancellationRequested) await Task.Delay(100, _stop.Token).ConfigureAwait(false); }
            }
        });
    }

    public async Task<bool> ActivateExistingAsync(CancellationToken ct = default)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            using var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);
            var pidBytes = new byte[4];
            await pipe.ReadExactlyAsync(pidBytes, timeout.Token).ConfigureAwait(false);
            BrowserForeground.Allow(checked((uint)BitConverter.ToInt32(pidBytes)));
            await pipe.WriteAsync(new byte[] { 1 }, timeout.Token).ConfigureAwait(false);
            var ack = new byte[1];
            await pipe.ReadExactlyAsync(ack, timeout.Token).ConfigureAwait(false);
            return ack[0] == 1;
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or UnauthorizedAccessException or OverflowException)
        { return false; }
    }

    public void Dispose()
    {
        _stop.Cancel();
        if (IsPrimary) _mutex.ReleaseMutex();
        _mutex.Dispose();
        // The listener owns its stream; cancellation closes it without blocking the UI.
        if (_listener != null) _ = _listener.ContinueWith(_ => _stop.Dispose(), TaskScheduler.Default);
        else _stop.Dispose();
    }
}
