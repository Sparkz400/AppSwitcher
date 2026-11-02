using System.IO.Pipes;
using System.Text.Json;
using AppSwitcher.BrowserIntegration;

// Only the framed protocol goes to stdout. Diagnostic output goes to stderr.
try
{
    var allowedPath = Path.Combine(AppContext.BaseDirectory, "allowed-origins.json");
    var allowed = JsonSerializer.Deserialize<string[]>(await File.ReadAllTextAsync(allowedPath)) ?? [];
    if (!args.Any(arg => allowed.Contains(arg, StringComparer.Ordinal)))
        throw new InvalidOperationException("Unregistered browser extension origin.");

    using var shutdown = new CancellationTokenSource();
    using var pipe = new NamedPipeClientStream(".", BrowserWire.PipeName, PipeDirection.InOut,
        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
    // The extension reconnects if AppSwitcher has not started yet.
    await pipe.ConnectAsync(5000, shutdown.Token);
    var input = Console.OpenStandardInput();
    var output = Console.OpenStandardOutput();
    var inbound = ForwardAsync(input, pipe, shutdown.Token);
    var outbound = ForwardAsync(pipe, output, shutdown.Token);
    await Task.WhenAny(inbound, outbound);
    await shutdown.CancelAsync();
    try { await Task.WhenAll(inbound, outbound); } catch (OperationCanceledException) { }
}
catch (Exception ex)
{
    await Console.Error.WriteLineAsync($"AppSwitcher browser bridge: {ex.Message}");
    return 1;
}
return 0;

static async Task ForwardAsync(Stream source, Stream destination, CancellationToken ct)
{
    while (await BrowserWire.ReadAsync(source, ct) is { } message)
        await BrowserWire.WriteAsync(destination, message, ct);
}
