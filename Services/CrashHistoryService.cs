namespace AppSwitcher.Services;

public class CrashHistoryService
{
    private readonly ILogger<CrashHistoryService> _logger;
    private readonly string _crashLogPath;

    public CrashHistoryService(ILogger<CrashHistoryService> logger)
    {
        _logger = logger;
        _crashLogPath = Path.Combine(AppPaths.DataDirectory, "crash_history.csv");
    }

    public virtual void RecordCrash(string appName, string appPath)
    {
        WriteEntry(appName, appPath, "Crash");
    }

    public virtual void RecordRestart(string appName, string appPath, int attemptNumber)
    {
        WriteEntry(appName, appPath, $"Restart (attempt {attemptNumber})");
    }

    public virtual void RecordEvent(string appName, string appPath, string eventType)
    {
        WriteEntry(appName, appPath, eventType);
    }

    private void WriteEntry(string appName, string appPath, string eventType)
    {
        try
        {
            var needsHeader = !File.Exists(_crashLogPath);
            using var writer = new StreamWriter(_crashLogPath, append: true);
            if (needsHeader)
                writer.WriteLine("Timestamp,AppName,AppPath,Event");
            writer.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss},{EscapeCsv(appName)},{EscapeCsv(appPath)},{EscapeCsv(eventType)}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record {Event} for {App}", eventType, appName);
        }
    }

    public List<CrashEntry> GetHistory(int maxEntries = 200)
    {
        var entries = new List<CrashEntry>();
        if (!File.Exists(_crashLogPath)) return entries;

        try
        {
            var lines = File.ReadAllLines(_crashLogPath);
            foreach (var line in lines.Skip(1).TakeLast(maxEntries))
            {
                var parts = ParseCsvLine(line);
                if (parts.Length >= 4 && DateTime.TryParse(parts[0], out var timestamp))
                    entries.Add(new CrashEntry(timestamp, parts[1], parts[2], parts[3]));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read crash history");
        }

        return entries;
    }

    public Dictionary<string, int> GetCrashCountsByApp(int daysBack = 7)
    {
        var cutoff = DateTime.Now.AddDays(-daysBack);
        return GetHistory(1000)
            .Where(e => e.Timestamp >= cutoff && e.Event.StartsWith("Crash", StringComparison.Ordinal))
            .GroupBy(e => e.AppName)
            .ToDictionary(g => g.Key, g => g.Count());
    }

    private static string EscapeCsv(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }

    private static string[] ParseCsvLine(string line)
    {
        var result = new List<string>();
        var current = new System.Text.StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            if (inQuotes)
            {
                if (line[i] == '"' && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else if (line[i] == '"')
                    inQuotes = false;
                else
                    current.Append(line[i]);
            }
            else
            {
                if (line[i] == '"')
                    inQuotes = true;
                else if (line[i] == ',')
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
                else
                    current.Append(line[i]);
            }
        }
        result.Add(current.ToString());
        return [.. result];
    }

    public record CrashEntry(DateTime Timestamp, string AppName, string AppPath, string Event);
}
