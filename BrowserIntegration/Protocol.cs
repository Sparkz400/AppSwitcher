using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AppSwitcher.BrowserIntegration;

public sealed record BrowserRequest(string Action, string AppId, string Generation,
    string Url = "", string Name = "", bool AllowCreate = false, bool Focus = false);

public sealed record BrowserReply(string Code, string State = "Unknown", int? TabId = null, int? WindowId = null);
public sealed record BrowserTabState(string AppId, string Generation, string State);
public sealed record BrowserMessage
{
    public int Protocol { get; init; } = 1;
    public string Type { get; init; } = "";
    public string? RequestId { get; init; }
    public string? Browser { get; init; }
    public string? ProfileId { get; init; }
    public string? ProfileName { get; init; }
    public string? SessionId { get; init; }
    public string? ExtensionVersion { get; init; }
    public BrowserRequest? Request { get; init; }
    public BrowserReply? Reply { get; init; }
    public List<BrowserTabState>? States { get; init; }
}

/// <summary>The same bounded framing is used for native messaging and the local named pipe.</summary>
public static class BrowserWire
{
    public const int MaximumMessageBytes = 1_048_576;
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public static string PipeName
    {
        get
        {
            var identity = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
            var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..24];
            return $"AppSwitcher.Browser.v1.{digest}";
        }
    }

    public static async Task<BrowserMessage?> ReadAsync(Stream stream, CancellationToken ct)
    {
        var header = new byte[4];
        var first = await stream.ReadAsync(header.AsMemory(0, 1), ct).ConfigureAwait(false);
        if (first == 0) return null;
        await stream.ReadExactlyAsync(header.AsMemory(1), ct).ConfigureAwait(false);
        var length = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length <= 0 || length > MaximumMessageBytes) throw new InvalidDataException("Invalid browser message size.");
        var body = new byte[length];
        await stream.ReadExactlyAsync(body, ct).ConfigureAwait(false);
        return JsonSerializer.Deserialize<BrowserMessage>(body, JsonOptions)
            ?? throw new InvalidDataException("Invalid browser message.");
    }

    public static async Task WriteAsync(Stream stream, BrowserMessage message, CancellationToken ct)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);
        if (body.Length > MaximumMessageBytes) throw new InvalidDataException("Browser message too large.");
        var header = new byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(header, body.Length);
        await stream.WriteAsync(header, ct).ConfigureAwait(false);
        await stream.WriteAsync(body, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }
}
