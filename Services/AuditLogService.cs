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

                var needsHeader = !File.Exists(_auditLogPath);
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
        if (maxEntries <= 0 || !File.Exists(_auditLogPath))
            return entries;

        try
        {
            var lines = File.ReadAllLines(_auditLogPath);
            foreach (var line in lines.Skip(1).TakeLast(maxEntries).Reverse())
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
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read audit log entries");
        }

        return entries;
    }

    private static string EscapeCsv(string value)
    {
        value ??= string.Empty;
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            return $"\"{value.Replace("\"", "\"\"")}\"";

        return value;
    }

    private static string[] ParseCsvLine(string line)
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
