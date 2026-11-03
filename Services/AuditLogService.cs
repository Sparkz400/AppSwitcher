namespace AppSwitcher.Services;

public sealed record AuditActor(string Role, string User)
{
    public string DisplayLabel =>
        string.IsNullOrWhiteSpace(User)
            ? Role
            : $"{Role} / {User}";
}

public sealed record AuditLogEntry(
    DateTime Timestamp,
    string Action,
    string ActorRole,
    string ActorUser,
    string Outcome,
    string Target,
    string Details)
{
    public string ActionDisplay => DisplayCode("AuditEvent_", Action);
    public string OutcomeDisplay => DisplayCode("AuditOutcome_", Outcome);
    private static string DisplayCode(string prefix, string code)
    {
        var key = prefix + code;
        var translated = LocalizationService.T(key);
        return translated == key ? code : translated;
    }
    public string ActorDisplay =>
        string.IsNullOrWhiteSpace(ActorUser)
            ? ActorRole
            : $"{ActorRole} / {ActorUser}";
}

public sealed class AuditLogService
{
    private readonly ILogger<AuditLogService> _logger;
    private readonly string _auditLogPath;
    private readonly object _sync = new();
    internal const long RotationBytes = 16 * 1024 * 1024;
    internal const int RetainedArchives = 5;

    public event Action<AuditLogEntry>? EntryRecorded;

    public AuditLogService(ILogger<AuditLogService> logger)
        : this(logger, AppPaths.AuditLogPath)
    {
    }

    internal AuditLogService(ILogger<AuditLogService> logger, string auditLogPath)
    {
        _logger = logger;
        _auditLogPath = auditLogPath;
    }

    public void Record(string action, AuditActor actor, string outcome, string target, string details = "")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentNullException.ThrowIfNull(actor);

        Record(new AuditLogEntry(
            DateTime.Now,
            action,
            actor.Role,
            actor.User,
            outcome,
            target ?? string.Empty,
            details ?? string.Empty));
    }

    public void Record(AuditLogEntry entry)
    {
        try
        {
            lock (_sync)
            {
                var directory = Path.GetDirectoryName(_auditLogPath);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);

                RotateIfNeeded(_auditLogPath);
                var needsHeader = !File.Exists(_auditLogPath) || new FileInfo(_auditLogPath).Length == 0;
                using var writer = new StreamWriter(_auditLogPath, append: true);
                if (needsHeader)
                    writer.WriteLine("Timestamp,Action,ActorRole,ActorUser,Outcome,Target,Details");

                writer.WriteLine(string.Join(",",
                    EscapeCsv(entry.Timestamp.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
                    EscapeCsv(entry.Action),
                    EscapeCsv(entry.ActorRole),
                    EscapeCsv(entry.ActorUser),
                    EscapeCsv(entry.Outcome),
                    EscapeCsv(entry.Target),
                    EscapeCsv(entry.Details)));
            }

            EntryRecorded?.Invoke(entry);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write audit log entry for action {Action}", entry.Action);
        }
    }

    public List<AuditLogEntry> GetRecentEntries(int maxEntries = 200)
    {
        var entries = new List<AuditLogEntry>();
        maxEntries = Math.Min(maxEntries, 5000);
        if (maxEntries <= 0)
            return entries;

        try
        {
            lock (_sync)
            {
                for (var archive = 0; archive <= RetainedArchives && entries.Count < maxEntries; archive++)
                {
                    var path = archive == 0 ? _auditLogPath : $"{_auditLogPath}.{archive}";
                    if (!File.Exists(path)) continue;
                    foreach (var line in ReadRecordsBackwards(path))
                    {
                        var parts = ParseCsvLine(line);
                        if (parts.Length < 7)
                            continue;

                        if (!DateTime.TryParse(parts[0], CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp))
                            continue;

                        entries.Add(new AuditLogEntry(
                            timestamp,
                            parts[1],
                            parts[2],
                            parts[3],
                            parts[4],
                            parts[5],
                            parts[6]));
                        if (entries.Count >= maxEntries) break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read audit log entries");
        }

        return entries;
    }

    internal static void RotateIfNeeded(string path)
    {
        if (!File.Exists(path) || new FileInfo(path).Length < RotationBytes) return;
        for (var index = RetainedArchives; index >= 1; index--)
        {
            var source = index == 1 ? path : $"{path}.{index - 1}";
            if (File.Exists(source)) File.Move(source, $"{path}.{index}", overwrite: true);
        }
    }

    // Quote parity works in either direction, including doubled CSV quotes. Scan
    // UTF-8 bytes so a block boundary cannot split a character during decoding.
    internal static IEnumerable<string> ReadRecordsBackwards(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var block = new byte[8192];
        var record = new List<byte>(256);
        var quoted = false;
        var oversized = false;
        var remaining = stream.Length;
        while (remaining > 0)
        {
            var count = (int)Math.Min(block.Length, remaining);
            remaining -= count;
            stream.Position = remaining;
            stream.ReadExactly(block.AsSpan(0, count));
            for (var index = count - 1; index >= 0; index--)
            {
                var value = block[index];
                if (value == (byte)'"') quoted = !quoted;
                if (value == (byte)'\n' && !quoted)
                {
                    if (record.Count > 0 && !oversized) yield return Decode();
                    record.Clear();
                    oversized = false;
                }
                else if (!oversized)
                {
                    if (record.Count >= 1024 * 1024) { oversized = true; record.Clear(); }
                    else record.Add(value);
                }
            }
        }
        if (record.Count > 0 && !oversized && !quoted) yield return Decode();

        string Decode()
        {
            record.Reverse();
            return System.Text.Encoding.UTF8.GetString(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(record)).TrimEnd('\r');
        }
    }

    private static string EscapeCsv(string value)
    {
        value ??= string.Empty;
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            return $"\"{value.Replace("\"", "\"\"")}\"";

        return value;
    }

    internal static string[] ParseCsvLine(string line)
    {
        var result = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            if (inQuotes)
            {
                if (line[i] == '"' && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else if (line[i] == '"')
                {
                    inQuotes = false;
                }
                else
                {
                    current.Append(line[i]);
                }
            }
            else
            {
                if (line[i] == '"')
                {
                    inQuotes = true;
                }
                else if (line[i] == ',')
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(line[i]);
                }
            }
        }

        result.Add(current.ToString());
        return [.. result];
    }
}
