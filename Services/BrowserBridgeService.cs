using System.Collections.Concurrent;
using System.IO.Pipes;
using AppSwitcher.BrowserIntegration;

namespace AppSwitcher.Services;

public enum WebsitePresence { Unknown, Present, Absent, Conflict }
public sealed record BrowserProfileChoice(string ProfileId, BrowserKind Browser, string Name, string ExtensionVersion = "")
{
    public string DisplayName => $"{Browser} — {Name} ({ProfileId[..Math.Min(8, ProfileId.Length)]})";
}

/// <summary>Per-user broker. Website commands never fall back to ShellExecute or process matching.</summary>
public sealed class BrowserBridgeService : IDisposable
{
    private readonly ILogger<BrowserBridgeService> _logger;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly ConcurrentDictionary<Guid, Connection> _clients = new();
    private readonly BoundedOperationGates _gates = new();
    private readonly ConcurrentDictionary<string, DateTime> _bootstraps = new();
    private readonly BrowserOwnershipStore? _ownership;
    private readonly Task _listener;
    private readonly string _pipeName;
    private readonly object _bootstrapGate = new();

    public BrowserBridgeService(ILogger<BrowserBridgeService> logger)
        : this(logger, Path.Combine(AppPaths.DataDirectory, "browser-ownership.json"),
            AppPaths.IsolatedDataDirectory is { } isolated
                ? BrowserWire.PipeName + ".Test." + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(isolated)))[..24]
                : BrowserWire.PipeName) { }

    internal BrowserBridgeService(ILogger<BrowserBridgeService> logger, string journalPath, string pipeName)
    {
        _logger = logger;
        _pipeName = pipeName;
        try { _ownership = new(journalPath); }
        catch (Exception ex) { _logger.LogError(ex, "Browser ownership journal cannot be read; website creation is disabled"); }
        _listener = Task.Run(AcceptLoopAsync);
    }

    public IReadOnlyList<BrowserProfileChoice> Profiles => _clients.Values.Where(c => c.Alive)
        .Select(c => new BrowserProfileChoice(c.ProfileId, c.Browser, c.Name, c.ExtensionVersion)).OrderBy(p => p.DisplayName).ToArray();

    public WebsitePresence GetPresence(AppProcessTarget target)
    {
        var clients = MatchingConnections(target.Browser, target.BrowserProfileId ?? "");
        if (clients.Length > 1) return WebsitePresence.Conflict;
        if (clients.Length != 1 || _ownership == null) return WebsitePresence.Unknown;
        var owner = _ownership.Get(target.WebsiteId!);
        if (owner == null || owner.ProfileId != target.BrowserProfileId || owner.Browser != target.Browser) return WebsitePresence.Unknown;
        if (owner.Phase == "Closing") return WebsitePresence.Unknown;
        if (!clients[0].States.TryGetValue(target.WebsiteId!, out var state) || state.Generation != owner.Generation)
            return WebsitePresence.Unknown;
        return Enum.TryParse<WebsitePresence>(state.State, out var result) ? result : WebsitePresence.Unknown;
    }

    public async Task<string> InspectAsync(AppItem app, CancellationToken ct)
    {
        app = Snapshot(app);
        ValidateWebsite(app);
        var gate = _gates.For(app.AppId);
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var connection = GetConnection(app.Browser, app.BrowserProfileId);
            var owner = (_ownership ?? throw Failure("JournalUnavailable")).Get(app.AppId);
            if (owner == null) return "Connected";
            if (owner.ProfileId != app.BrowserProfileId || owner.Browser != app.Browser) return "ProfileChanged";
            var reply = await connection.RequestAsync(new("inspect", app.AppId, owner.Generation), ct).ConfigureAwait(false);
            return reply.State == "Present" ? "Ready" : reply.Code;
        }
        finally { gate.Release(); }
    }

    public async Task<AppLaunchResult> OpenAsync(AppItem app, bool focus, Func<Task>? beforeCreate, CancellationToken ct, bool activateOnly = false)
    {
        app = Snapshot(app);
        ValidateWebsite(app);
        var gate = _gates.For(app.AppId);
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var connection = await ConnectAsync(app, ct).ConfigureAwait(false);
            var store = _ownership ?? throw Failure("JournalUnavailable");
            var owner = store.Get(app.AppId);
            if (owner != null && (owner.ProfileId != app.BrowserProfileId || owner.Browser != app.Browser))
            {
                if (owner.Phase != "Closed") throw Failure("ProfileChanged");
                owner = null;
            }
            if (owner == null && activateOnly) throw Failure("Recovering");
            owner ??= store.Reserve(app);
            if (owner.Phase == "Closing") throw Failure("Recovering");
            if (activateOnly)
            {
                var activated = await connection.RequestAsync(new("activate", app.AppId, owner.Generation, Focus: focus), ct).ConfigureAwait(false);
                if (activated.Code != "ActivatedExisting") throw Failure(activated.Code);
                store.Put(owner with { Phase = "Active" });
                return new(AppLaunchKind.ActivatedExisting);
            }
            var request = new BrowserRequest("inspect", app.AppId, owner.Generation, app.WebsiteUrl, app.Name,
                AllowCreate: owner.Phase == "Reserved", Focus: focus);
            var inspection = await connection.RequestAsync(request, ct).ConfigureAwait(false);
            if (inspection.State == "Conflict") throw Failure("IdentityConflict");
            if (inspection.State == "Absent")
            {
                if (owner.Phase != "Reserved") owner = store.Reserve(app);
                if (beforeCreate != null) await beforeCreate().ConfigureAwait(false);
                // Persist before sending. A timeout is an unknown result, never a fresh launch permission.
                store.Put(owner with { Phase = "Issued" });
                request = request with { Action = "ensure", Generation = owner.Generation, AllowCreate = true };
            }
            else request = request with { Action = "ensure", AllowCreate = false };
            var reply = await connection.RequestAsync(request, ct).ConfigureAwait(false);
            if (reply.State == "Present") store.Put(owner with { Phase = "Active" });
            if (reply.Code == "CreatedFocusDenied")
                return new AppLaunchResult(AppLaunchKind.StartedNewProcess, FocusSucceeded: false);
            if (reply.Code == "CreatedNew") return new(AppLaunchKind.StartedNewProcess);
            if (reply.Code == "ActivatedExisting") return new(AppLaunchKind.ActivatedExisting);
            throw Failure(reply.Code);
        }
        finally { gate.Release(); }
    }

    public async Task CloseAsync(AppItem app, CancellationToken ct)
    {
        app = Snapshot(app);
        ValidateWebsite(app);
        var gate = _gates.For(app.AppId);
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var store = _ownership ?? throw Failure("JournalUnavailable");
            var owner = store.Get(app.AppId);
            if (owner == null) throw Failure("Recovering");
            if (owner.ProfileId != app.BrowserProfileId || owner.Browser != app.Browser) throw Failure("ProfileChanged");
            var connection = GetConnection(app.Browser, app.BrowserProfileId);
            store.Put(owner with { Phase = "Closing" });
            var reply = await connection.RequestAsync(new("close", app.AppId, owner.Generation), ct).ConfigureAwait(false);
            if (reply.Code != "Closed") throw Failure(reply.Code);
            store.Put(owner with { Phase = "Closed" });
        }
        finally { gate.Release(); }
    }

    public static void ValidateWebsite(AppItem app)
    {
        if (!Guid.TryParse(app.AppId, out _) || !Guid.TryParse(app.BrowserProfileId, out _)) throw Failure("ProfileRequired");
        if (!Uri.TryCreate(app.WebsiteUrl, UriKind.Absolute, out var url) ||
            url.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(url.UserInfo)) throw Failure("InvalidUrl");
    }

    private static AppItem Snapshot(AppItem app) => new()
    {
        AppId = app.AppId, TargetKind = app.TargetKind, Name = app.Name, WebsiteUrl = app.WebsiteUrl,
        Browser = app.Browser, BrowserProfileId = app.BrowserProfileId,
        BrowserProfileDirectory = app.BrowserProfileDirectory, BrowserExecutablePath = app.BrowserExecutablePath
    };

    private Connection[] MatchingConnections(BrowserKind browser, string profileId) => _clients.Values
        .Where(c => c.Alive && c.Browser == browser && c.ProfileId == profileId).ToArray();
    private Connection GetConnection(BrowserKind browser, string profileId)
    {
        var matches = MatchingConnections(browser, profileId);
        return matches.Length switch { 1 => matches[0], > 1 => throw Failure("ProfileConflict"), _ => throw Failure("ConnectionUnavailable") };
    }
    private async Task<Connection> ConnectAsync(AppItem app, CancellationToken ct)
    {
        if (MatchingConnections(app.Browser, app.BrowserProfileId).Length > 0) return GetConnection(app.Browser, app.BrowserProfileId);
        // Only bootstrap a browser that is not running; never relaunch an unresponsive existing browser.
        var processName = app.Browser switch { BrowserKind.Chrome => "chrome", BrowserKind.Edge => "msedge", _ => "firefox" };
        var processes = Process.GetProcessesByName(processName);
        var running = processes.Length > 0;
        foreach (var process in processes) process.Dispose();
        var bootstrapKey = $"{app.Browser}:{app.BrowserProfileId}";
        lock (_bootstrapGate)
        {
            if (!running && !string.IsNullOrWhiteSpace(app.BrowserProfileDirectory) &&
                Directory.Exists(app.BrowserProfileDirectory) && File.Exists(app.BrowserExecutablePath) &&
                (!_bootstraps.TryGetValue(bootstrapKey, out var previous) || DateTime.UtcNow - previous > TimeSpan.FromSeconds(30)))
            {
                foreach (var expired in _bootstraps.Where(entry => DateTime.UtcNow - entry.Value > TimeSpan.FromSeconds(30)))
                    _bootstraps.TryRemove(expired);
                _bootstraps[bootstrapKey] = DateTime.UtcNow;
                var start = new ProcessStartInfo(app.BrowserExecutablePath) { UseShellExecute = false };
                var profile = Path.GetFullPath(app.BrowserProfileDirectory);
                if (app.Browser == BrowserKind.Firefox) { start.ArgumentList.Add("-profile"); start.ArgumentList.Add(profile); }
                else
                {
                    start.ArgumentList.Add($"--user-data-dir={Path.GetDirectoryName(profile)}");
                    start.ArgumentList.Add($"--profile-directory={Path.GetFileName(profile)}");
                }
                using var launched = Process.Start(start);
            }
        }
        for (var attempt = 0; attempt < 30; attempt++)
        {
            await Task.Delay(500, ct).ConfigureAwait(false);
            if (MatchingConnections(app.Browser, app.BrowserProfileId).Length > 0) return GetConnection(app.Browser, app.BrowserProfileId);
        }
        throw Failure("ConnectionUnavailable");
    }

    private async Task AcceptLoopAsync()
    {
        while (!_shutdown.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = new(_pipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(_shutdown.Token).ConfigureAwait(false);
                _ = HandleClientAsync(pipe);
                pipe = null;
            }
            catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Browser bridge listener failed");
                try { await Task.Delay(1000, _shutdown.Token).ConfigureAwait(false); } catch (OperationCanceledException) { }
            }
            finally { pipe?.Dispose(); }
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream pipe)
    {
        var id = Guid.NewGuid();
        Connection? connection = null;
        try
        {
            using var helloTimeout = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
            helloTimeout.CancelAfter(TimeSpan.FromSeconds(8));
            var hello = await BrowserWire.ReadAsync(pipe, helloTimeout.Token).ConfigureAwait(false);
            if (hello is not { Type: "hello", Protocol: 1 } || !Guid.TryParse(hello.ProfileId, out _) ||
                !Guid.TryParse(hello.SessionId, out _) || !Enum.TryParse<BrowserKind>(hello.Browser, out var browser) || !Enum.IsDefined(browser))
                throw new InvalidDataException("Invalid browser handshake.");
            connection = new(pipe, browser, hello.ProfileId!, (hello.ProfileName ?? browser.ToString())[..Math.Min(80, (hello.ProfileName ?? browser.ToString()).Length)], BrowserForeground.FindBrowserProcess(pipe.SafePipeHandle, browser), _shutdown.Token);
            connection.ExtensionVersion = hello.ExtensionVersion ?? "";
            _clients[id] = connection;
            await connection.ReadLoopAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or JsonException or InvalidDataException)
        { _logger.LogDebug(ex, "Browser connection ended"); }
        finally { _clients.TryRemove(id, out _); connection?.Dispose(); pipe.Dispose(); }
    }

    internal static InvalidOperationException Failure(string code) => new(LocalizationService.T($"Browser_{code}"));
    public void Dispose()
    {
        _shutdown.Cancel();
        foreach (var client in _clients.Values) client.Dispose();
        // The listener observes cancellation; do not synchronously join on the WPF dispatcher.
    }

    private sealed class Connection(Stream pipe, BrowserKind browser, string profileId, string name, uint browserProcessId, CancellationToken shutdown) : IDisposable
    {
        private readonly ConcurrentDictionary<string, TaskCompletionSource<BrowserReply>> _pending = new();
        private readonly SemaphoreSlim _writer = new(1, 1);
        private long _lastSeen = DateTime.UtcNow.Ticks;
        private int _disposed;
        public string ExtensionVersion { get; set; } = "";
        public BrowserKind Browser { get; } = browser;
        public string ProfileId { get; } = profileId;
        public string Name { get; } = name;
        public ConcurrentDictionary<string, BrowserTabState> States { get; } = new();
        public bool Alive => Volatile.Read(ref _disposed) == 0 && DateTime.UtcNow - new DateTime(Interlocked.Read(ref _lastSeen), DateTimeKind.Utc) < TimeSpan.FromSeconds(20);
        public async Task<BrowserReply> RequestAsync(BrowserRequest request, CancellationToken ct)
        {
            if (request.Focus) BrowserForeground.Allow(browserProcessId);
            var id = Guid.NewGuid().ToString("N");
            var completion = new TaskCompletionSource<BrowserReply>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[id] = completion;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct, shutdown);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            try
            {
                await _writer.WaitAsync(timeout.Token).ConfigureAwait(false);
                try { await BrowserWire.WriteAsync(pipe, new() { Type = "request", RequestId = id, Request = request }, timeout.Token).ConfigureAwait(false); }
                finally { _writer.Release(); }
                var reply = await completion.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                States[request.AppId] = new(request.AppId, request.Generation, reply.State);
                return reply;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested && !shutdown.IsCancellationRequested) { throw Failure("Recovering"); }
            finally { _pending.TryRemove(id, out _); }
        }
        public async Task ReadLoopAsync()
        {
            while (await BrowserWire.ReadAsync(pipe, shutdown).ConfigureAwait(false) is { } message)
            {
                if (message.Protocol != 1) throw new InvalidDataException("Browser protocol mismatch.");
                Interlocked.Exchange(ref _lastSeen, DateTime.UtcNow.Ticks);
                if (message.Type == "reply" && message.RequestId != null && message.Reply != null && _pending.TryGetValue(message.RequestId, out var pending))
                    pending.TrySetResult(message.Reply);
                else if (message.Type == "state" && message.States != null)
                {
                    States.Clear();
                    foreach (var state in message.States.Take(2000)) States[state.AppId] = state;
                }
            }
        }
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            foreach (var pending in _pending.Values) pending.TrySetException(Failure("ConnectionUnavailable"));
            pipe.Dispose();
        }
    }
}
