using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO.Pipes;
using AppSwitcher.BrowserIntegration;
using Microsoft.Extensions.Logging.Abstractions;

namespace AppSwitcher.Tests;

public class BrowserIntegrationTests
{
    [Fact]
    public async Task Setup_Check_Never_Reserves_Or_Creates_A_Website()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "ownership.json");
        var pipe = $"AppSwitcher.Tests.{Guid.NewGuid():N}";
        var app = Website();
        using var bridge = new BrowserBridgeService(NullLogger<BrowserBridgeService>.Instance, path, pipe);
        using var browser = await FakeBrowser.ConnectAsync(pipe, app, _ => new("Absent", "Absent"));
        await WaitForProfile(bridge);
        Assert.Equal("Connected", await bridge.InspectAsync(app, default));
        Assert.Empty(browser.Requests);
        Assert.Null(new BrowserOwnershipStore(path).Get(app.AppId));
    }

    [Fact]
    public async Task Wire_Handles_Fragmented_Frames_And_Clean_Eof()
    {
        using var stream = new FragmentedStream();
        var message = new BrowserMessage { Type = "reply", Reply = new("CreatedNew", "Present", 12, 3) };
        await BrowserWire.WriteAsync(stream, message, default);
        stream.Position = 0;
        var result = await BrowserWire.ReadAsync(stream, default);
        Assert.Equal(message.Reply, result!.Reply);
        Assert.Null(await BrowserWire.ReadAsync(stream, default));
    }

    [Theory]
    [InlineData(0)] [InlineData(-1)] [InlineData(1048577)]
    public async Task Wire_Rejects_Invalid_Lengths_Before_Allocating(int length)
    {
        var bytes = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(bytes, length);
        await Assert.ThrowsAsync<InvalidDataException>(() => BrowserWire.ReadAsync(new MemoryStream(bytes), default));
    }

    [Fact]
    public async Task Wire_Rejects_Truncated_Payload()
    {
        await Assert.ThrowsAsync<EndOfStreamException>(() => BrowserWire.ReadAsync(new MemoryStream([4, 0, 0, 0, 123]), default));
    }

    [Fact]
    public void Journal_Preserves_Uncertain_Issued_State_Across_Restart_And_Rejects_Corruption()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "ownership.json");
        var store = new BrowserOwnershipStore(path);
        var owner = store.Reserve(Website()) with { Phase = "Issued" };
        store.Put(owner);
        Assert.Equal(owner, new BrowserOwnershipStore(path).Get(owner.AppId));
        File.WriteAllText(path, "{ broken");
        Assert.Throws<JsonException>(() => new BrowserOwnershipStore(path));
    }

    [Fact]
    public async Task Bridge_Serializes_Clicks_And_Recovers_Desktop_Restart_Without_New_Creation()
    {
        using var temp = new TempDirectory();
        var journal = Path.Combine(temp.Path, "ownership.json");
        var pipe = $"AppSwitcher.Tests.{Guid.NewGuid():N}";
        var app = Website();
        var present = false;
        var hooks = 0;
        BrowserReply Respond(BrowserRequest request)
        {
            if (request.Action == "inspect") return present ? new("Present", "Present") : new("Absent", "Absent");
            if (!present && request.AllowCreate) { present = true; return new("CreatedNew", "Present", 14, 2); }
            return new("ActivatedExisting", "Present", 14, 2);
        }
        using (var bridge = new BrowserBridgeService(NullLogger<BrowserBridgeService>.Instance, journal, pipe))
        using (var client = await FakeBrowser.ConnectAsync(pipe, app, Respond))
        {
            await WaitForProfile(bridge);
            var replies = await Task.WhenAll(Enumerable.Range(0, 25).Select(_ => bridge.OpenAsync(app, true, () => { Interlocked.Increment(ref hooks); return Task.CompletedTask; }, default)));
            Assert.Single(replies, r => r.Kind == AppLaunchKind.StartedNewProcess);
            Assert.Equal(1, hooks);
            Assert.Single(client.Requests, r => r.Action == "ensure" && r.AllowCreate);
            Assert.Equal(WebsitePresence.Present, bridge.GetPresence(app.ProcessTarget));
        }
        var secondPipe = $"AppSwitcher.Tests.{Guid.NewGuid():N}";
        using var restarted = new BrowserBridgeService(NullLogger<BrowserBridgeService>.Instance, journal, secondPipe);
        using var reconnected = await FakeBrowser.ConnectAsync(secondPipe, app, Respond);
        await WaitForProfile(restarted);
        Assert.Equal(AppLaunchKind.ActivatedExisting, (await restarted.OpenAsync(app, true, null, default)).Kind);
        Assert.DoesNotContain(reconnected.Requests, r => r.AllowCreate);
    }

    [Fact]
    public async Task Lost_Create_Reply_Never_Authorizes_Another_Create()
    {
        using var temp = new TempDirectory();
        var pipe = $"AppSwitcher.Tests.{Guid.NewGuid():N}";
        var app = Website();
        var issued = false;
        using var bridge = new BrowserBridgeService(NullLogger<BrowserBridgeService>.Instance, Path.Combine(temp.Path, "ownership.json"), pipe);
        using var client = await FakeBrowser.ConnectAsync(pipe, app, request =>
        {
            if (!issued && request.Action == "inspect") return new("Absent", "Absent");
            if (!issued && request.Action == "ensure") { issued = true; return null; }
            return new("Recovering", "Unknown");
        });
        await WaitForProfile(bridge);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => bridge.OpenAsync(app, true, null, timeout.Token));
        await Assert.ThrowsAsync<InvalidOperationException>(() => bridge.OpenAsync(app, true, null, default));
        Assert.Single(client.Requests, r => r.Action == "ensure" && r.AllowCreate);
        Assert.Equal(WebsitePresence.Unknown, bridge.GetPresence(app.ProcessTarget));
    }

    [Fact]
    public async Task Activation_Cannot_Reserve_Or_Create_A_Tab()
    {
        using var temp = new TempDirectory();
        var pipe = $"AppSwitcher.Tests.{Guid.NewGuid():N}";
        var app = Website();
        var journal = Path.Combine(temp.Path, "ownership.json");
        using var bridge = new BrowserBridgeService(NullLogger<BrowserBridgeService>.Instance, journal, pipe);
        using var client = await FakeBrowser.ConnectAsync(pipe, app, _ => new("Absent", "Absent"));
        await WaitForProfile(bridge);
        await Assert.ThrowsAsync<InvalidOperationException>(() => bridge.OpenAsync(app, true, null, default, activateOnly: true));
        Assert.Empty(client.Requests);
        Assert.False(File.Exists(journal));
    }

    [Fact]
    public async Task Duplicate_Profile_Connections_Block_All_Commands()
    {
        using var temp = new TempDirectory();
        var pipe = $"AppSwitcher.Tests.{Guid.NewGuid():N}";
        var app = Website();
        using var bridge = new BrowserBridgeService(NullLogger<BrowserBridgeService>.Instance, Path.Combine(temp.Path, "ownership.json"), pipe);
        using var first = await FakeBrowser.ConnectAsync(pipe, app, _ => new("Absent", "Absent"));
        using var second = await FakeBrowser.ConnectAsync(pipe, app, _ => new("Absent", "Absent"));
        await WaitForProfile(bridge, 2);
        await Assert.ThrowsAsync<InvalidOperationException>(() => bridge.OpenAsync(app, true, null, default));
        Assert.Empty(first.Requests); Assert.Empty(second.Requests);
        Assert.Equal(WebsitePresence.Conflict, bridge.GetPresence(app.ProcessTarget));
    }

    [Theory]
    [InlineData("https://192.168.1.78", true)]
    [InlineData("http://[::1]:8080/test", true)]
    [InlineData("https://en.wikipedia.org/wiki/Test?x=1#section", true)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("file:///C:/secret", false)]
    [InlineData("https://name:password@example.com", false)]
    public void Website_Validation_Accepts_Hosts_And_Ips_But_Rejects_Unsafe_Schemes(string url, bool valid)
        => Assert.Equal(valid, WebsiteConfiguration.IsHttpUrl(url));

    [Fact]
    public void Website_Never_Uses_Browser_Process_Kill_Or_Title_Matching()
    {
        var helper = new ProcessHelper(NullLogger<ProcessHelper>.Instance);
        var target = Website().ProcessTarget;
        Assert.Empty(helper.GetMatchingProcesses(target));
        Assert.Equal(IntPtr.Zero, helper.GetMainWindowHandle(target));
        Assert.Throws<InvalidOperationException>(() => helper.KillApp(target));
        Assert.Throws<InvalidOperationException>(() => helper.CloseApp(target));
        Assert.Throws<InvalidOperationException>(() => helper.BringToFront(target));
    }

    [Fact]
    public void Configuration_Roundtrip_Preserves_Identity_And_Separates_Same_Url_Apps()
    {
        var first = Website(); var second = Website();
        Assert.NotEqual(first.ProcessTarget.Key, second.ProcessTarget.Key);
        var restored = JsonSerializer.Deserialize<AppItem>(JsonSerializer.Serialize(first, AppJsonOptions.Default), AppJsonOptions.Default)!;
        Assert.Equal(first.AppId, restored.AppId);
        Assert.Equal(first.ProcessTarget, restored.ProcessTarget);
    }

    private static AppItem Website() => new() { TargetKind = AppTargetKind.Website, Name = "Test website", WebsiteUrl = "https://127.0.0.1", Browser = BrowserKind.Chrome, BrowserProfileId = Guid.NewGuid().ToString("N") };
    private static async Task WaitForProfile(BrowserBridgeService bridge, int count = 1)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (bridge.Profiles.Count != count) await Task.Delay(10, timeout.Token);
    }
    private sealed class FragmentedStream : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => base.ReadAsync(buffer[..Math.Min(1, buffer.Length)], ct);
    }
    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"AppSwitcher.Tests.{Guid.NewGuid():N}");
        public TempDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
    private sealed class FakeBrowser : IDisposable
    {
        private readonly NamedPipeClientStream _pipe;
        private readonly CancellationTokenSource _shutdown = new();
        public ConcurrentQueue<BrowserRequest> Requests { get; } = new();
        private FakeBrowser(NamedPipeClientStream pipe) => _pipe = pipe;
        public static async Task<FakeBrowser> ConnectAsync(string name, AppItem app, Func<BrowserRequest, BrowserReply?> respond)
        {
            var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(5000);
            await BrowserWire.WriteAsync(pipe, new() { Type = "hello", Browser = app.Browser.ToString(), ProfileId = app.BrowserProfileId, SessionId = Guid.NewGuid().ToString("N") }, default);
            var client = new FakeBrowser(pipe);
            _ = client.RunAsync(respond);
            return client;
        }
        private async Task RunAsync(Func<BrowserRequest, BrowserReply?> respond)
        {
            try
            {
                while (await BrowserWire.ReadAsync(_pipe, _shutdown.Token) is { Request: { } request } message)
                {
                    Requests.Enqueue(request);
                    if (respond(request) is { } reply)
                        await BrowserWire.WriteAsync(_pipe, new() { Type = "reply", RequestId = message.RequestId, Reply = reply }, _shutdown.Token);
                }
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException) { }
        }
        public void Dispose() { _shutdown.Cancel(); _pipe.Dispose(); }
    }
}
