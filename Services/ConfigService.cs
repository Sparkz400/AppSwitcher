using System.Security.Cryptography;
using System.Text;
using System.Reflection;

namespace AppSwitcher.Services;

public sealed class ConfigurationLoadException(string message, Exception inner) : InvalidOperationException(message, inner);

public class ConfigService : IDisposable
{
    private const string ConfigFileName = "app_config.json";
    private const string BundledDefaultConfigResourceName = "AppSwitcher.DefaultConfig.app_config.json";
    private const int CurrentVersion = 12;
    private bool _migrationNeedsSave;
    private static readonly byte[] MagicHeader = [0x41, 0x53, 0x43, 0x45]; // "ASCE"
    private const int WatcherDebounceMilliseconds = 500;
    private const int WatcherSettleDelayMilliseconds = 250;
    private const int WatcherSelfSaveSuppressMilliseconds = 1500;

    private readonly ILogger<ConfigService> _logger;
    private readonly ConfigValidationService _validationService;
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private FileSystemWatcher? _watcher;
    private long _watcherLastEventTicks;
    private long _watcherIgnoreUntilTicks;
    private readonly string? _dataDirectory;
    public bool IsFirstRun { get; private set; }
    internal string ConfigFilePath => _dataDirectory == null ? AppPaths.ConfigFilePath : Path.Combine(_dataDirectory, ConfigFileName);
    private string BackupDirectory => _dataDirectory == null ? AppPaths.ConfigBackupDirectory : Path.Combine(_dataDirectory, "config_backups");

    internal ConfigService(ILogger<ConfigService> logger, ConfigValidationService validationService, string dataDirectory)
        : this(logger, validationService) => _dataDirectory = Path.GetFullPath(dataDirectory);

    /// <summary>
    /// Fired when the config file changes on disk (external edit or hot-reload).
    /// </summary>
    public event Action<ConfigRoot>? ConfigChanged;

    public ConfigService(ILogger<ConfigService> logger, ConfigValidationService validationService)
    {
        _logger = logger;
        _validationService = validationService;
    }

    public virtual async Task<ConfigRoot> LoadAsync()
    {
        TryMigrateLegacyConfig();

        if (!File.Exists(ConfigFilePath))
        {
            IsFirstRun = true;
            var bundledConfig = await TryLoadBundledDefaultConfigAsync();
            if (bundledConfig != null)
            {
                await TryPersistBundledDefaultConfigAsync(bundledConfig);
                _logger.LogInformation("No config file found, using bundled default configuration");
                return bundledConfig;
            }

            _logger.LogInformation("No config file found, using built-in fallback defaults");
            return new ConfigRoot
            {
                Version = CurrentVersion,
                Settings = new AppSettings(),
                Apps = [new AppItem { Name = "Notepad", Path = "notepad.exe" }]
            };
        }

        try
        {
            var root = await LoadConfigFileAsync(ConfigFilePath);
            EnsureConfigIsApplicable(root, "load");
            if (_migrationNeedsSave)
            {
                await SaveAsync(root);
                _migrationNeedsSave = false;
            }
            _logger.LogInformation("Config loaded: {Count} apps, version {Version}", root.Apps.Count, root.Version);
            return root;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load config");
            throw new ConfigurationLoadException($"Could not load configuration from {ConfigFilePath}.", ex);
        }
    }

    public virtual Task SaveAsync(ConfigRoot config) => SaveCoreAsync(config);

    private async Task SaveCoreAsync(ConfigRoot config, Action? beforeSave = null)
    {
        await _saveLock.WaitAsync();
        string? tempPath = null;
        try
        {
            beforeSave?.Invoke();
            NormalizeConfig(config);
            EnsureConfigIsApplicable(config, "save");
            CreateBackup(config.Settings.MaxConfigBackups);

            config.Version = CurrentVersion;
            var json = JsonSerializer.Serialize(config, AppJsonOptions.Default);
            byte[] output;

            if (config.Settings.ConfigEncryption)
            {
                var encrypted = Encrypt(json);
                output = new byte[MagicHeader.Length + encrypted.Length];
                MagicHeader.CopyTo(output, 0);
                encrypted.CopyTo(output, MagicHeader.Length);
            }
            else
            {
                output = Encoding.UTF8.GetBytes(json);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(ConfigFilePath) ?? AppPaths.DataDirectory);
            tempPath = $"{ConfigFilePath}.tmp";
            await File.WriteAllBytesAsync(tempPath, output);

            Interlocked.Exchange(
                ref _watcherIgnoreUntilTicks,
                DateTime.UtcNow.AddMilliseconds(WatcherSelfSaveSuppressMilliseconds).Ticks);

            File.Move(tempPath, ConfigFilePath, overwrite: true);
            tempPath = null;

            _logger.LogInformation("Config saved ({Mode})", config.Settings.ConfigEncryption ? "encrypted" : "plaintext");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save config");
            throw;
        }
        finally
        {
            if (tempPath != null)
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch (Exception cleanupEx)
                {
                    _logger.LogDebug(cleanupEx, "Failed to delete temporary config file {Path}", tempPath);
                }
            }

            _saveLock.Release();
        }
    }

    public virtual async Task ExportAsync(string targetPath, ConfigRoot config)
    {
        var exportCopy = CreatePortableExportCopy(config);
        var json = JsonSerializer.Serialize(exportCopy, AppJsonOptions.Default);
        await File.WriteAllTextAsync(targetPath, json);
        _logger.LogInformation("Config exported to {Path}", targetPath);
    }

    public virtual async Task<ConfigRoot?> ImportAsync(string sourcePath)
    {
        try
        {
            var config = await LoadConfigFileAsync(sourcePath);
            if (config != null)
            {
                if (!TryValidateWithoutThrow(config, "import"))
                    return null;
            }
            _logger.LogInformation("Config imported from {Path}", sourcePath);
            return config;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to import config from {Path}", sourcePath);
            return null;
        }
    }

    // ========== Config Migration Pipeline ==========

    private ConfigRoot MigrateConfig(ConfigRoot config)
    {
        config.Settings ??= new AppSettings();
        config.Settings.OperatorScope ??= new OperatorScopeSettings();
        config.Groups ??= [];
        config.Apps ??= [];
        config.Layouts ??= [];

        NormalizeConfig(config);

        if (config.Version >= CurrentVersion) return config;
        _migrationNeedsSave = true;

        _logger.LogInformation("Migrating config from version {From} to {To}", config.Version, CurrentVersion);

        if (config.Version < 2)
        {
            // v1 → v2: Groups list was added
            config.Groups ??= [];
            _logger.LogInformation("Migration v1→v2: Ensured Groups list exists");
        }

        if (config.Version < 3)
        {
            // v2 → v3: Removed MainRadius, added DependsOn/Schedule/PreferredMonitor
            // New fields have defaults, nothing to do for data migration
            // Derive groups from apps if Groups list is empty but apps have groups
            if (config.Groups.Count == 0)
            {
                var appGroups = config.Apps
                    .Where(a => !string.IsNullOrEmpty(a.Group))
                    .Select(a => a.Group)
                    .Distinct()
                    .ToList();
                if (appGroups.Count > 0)
                    config.Groups = appGroups.Select(g => new GroupDefinition
                    {
                        Key = g,
                        Names = new Dictionary<string, string> { ["de"] = g, ["en"] = g }
                    }).ToList();
            }
            _logger.LogInformation("Migration v2→v3: Applied new feature defaults");
        }

        if (config.Version < 4)
        {
            // v3 → v4: Added OperatorPasswordHash (defaults to empty, no data migration needed)
            _logger.LogInformation("Migration v3→v4: Role-based access support added");
        }

        if (config.Version < 5)
        {
            // v4 → v5: Health checks, session timeout, scheduled restart/shutdown,
            // resource monitoring, sort order, confirmation, favorites, screen layouts.
            // All new fields have defaults, no data transformation needed.
            config.Layouts ??= [];
            _logger.LogInformation("Migration v4→v5: New features (health, layouts, favorites, etc.) added with defaults");
        }

        if (config.Version < 6)
        {
            // v5 → v6: Groups changed from List<string> to List<GroupDefinition>.
            // The GroupListJsonConverter already handles legacy string arrays during deserialization,
            // so Groups will be properly populated. Just ensure display names are initialized.
            _logger.LogInformation("Migration v5→v6: Multilingual group names support added");
        }

        if (config.Version < 7)
        {
            // v6 → v7: Added operator app/group scope configuration.
            config.Settings.OperatorScope ??= new OperatorScopeSettings();
            config.Settings.OperatorScope.Normalize();
            _logger.LogInformation("Migration v6→v7: Operator app/group scope added");
        }

        if (config.Version < 8)
        {
            // v7 → v8: Added per-app window policy settings.
            _logger.LogInformation("Migration v7→v8: Window policy settings added with defaults");
        }

        if (config.Version < 9)
        {
            // v8 → v9: Per-app lifecycle hooks added with empty-string defaults.
            _logger.LogInformation("Migration v8→v9: Lifecycle hooks support added");
        }

        if (config.Version < 12)
        {
            // Previous releases allowed a 40px setting but rendered at least 44px,
            // and defaulted menu width below the actual popup minimum.
            if (config.Settings.ButtonSize is >= 40 and < 44) config.Settings.ButtonSize = 44;
            if (config.Settings.MenuWidth is > 0 and < 360) config.Settings.MenuWidth = 360;
            if (config.Settings.MenuItemHeight is > 0 and < 44) config.Settings.MenuItemHeight = 44;
            foreach (var app in config.Apps.Where(app => app.IsWebsite))
            { app.Args = ""; app.MemoryThresholdMB = 0; }
        }
        config.Version = CurrentVersion;
        return config;
    }

    // ========== Backup Management ==========

    public record ConfigBackupInfo(string FilePath, string FileName, DateTime Timestamp, long SizeBytes)
    {
        public string SizeDisplay =>
            string.Format(CultureInfo.CurrentCulture, "{0:N0} {1}", SizeBytes, LocalizationService.T("Unit_Bytes"));
    }

    public sealed record BackupPreview(string FilePath, string Fingerprint, ConfigRoot Config);

    public virtual async Task<BackupPreview?> ReadBackupPreviewAsync(string path)
    {
        try
        {
            var bytes = await File.ReadAllBytesAsync(path);
            var config = ParseConfig(bytes, path);
            EnsureConfigIsApplicable(config, "preview");
            return new(path, Convert.ToHexString(SHA256.HashData(bytes)), config);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not preview configuration backup");
            return null;
        }
    }

    public virtual List<ConfigBackupInfo> GetBackups()
    {
        var backups = new List<ConfigBackupInfo>();
        if (!Directory.Exists(BackupDirectory)) return backups;

        try
        {
            foreach (var file in Directory.GetFiles(BackupDirectory, "app_config_*.json")
                         .OrderByDescending(f => f))
            {
                var info = new FileInfo(file);
                // Parse timestamp from filename: app_config_yyyyMMdd_HHmmss.json
                var name = Path.GetFileNameWithoutExtension(file);
                var timestampStr = name.Replace("app_config_", "");
                if (timestampStr.Length > 15) timestampStr = timestampStr[..15];
                DateTime timestamp = DateTime.TryParseExact(timestampStr, "yyyyMMdd_HHmmss",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var parsed)
                    ? parsed
                    : info.LastWriteTime;

                backups.Add(new ConfigBackupInfo(file, info.Name, timestamp, info.Length));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to enumerate config backups");
        }

        return backups;
    }

    public virtual async Task<ConfigRoot?> RestoreBackupAsync(string backupPath, Action<ConfigRoot>? beforeSave = null,
        string? expectedFingerprint = null)
    {
        try
        {
            var preview = await ReadBackupPreviewAsync(backupPath);
            if (preview == null || (expectedFingerprint != null && preview.Fingerprint != expectedFingerprint)) return null;
            var config = preview.Config;
            await SaveCoreAsync(config, () =>
            {
                beforeSave?.Invoke(config);
                EnsureConfigIsApplicable(config, "restore");
                // Recovery copies are outside normal backup retention, including a corrupt current file.
                PreserveConfigurationForRecovery();
            });
            _logger.LogInformation("Config restored from backup: {Path}", backupPath);
            return config;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restore config from backup: {Path}", backupPath);
            return null;
        }
    }

    internal string? PreserveConfigurationForRecovery()
    {
        if (!File.Exists(ConfigFilePath)) return null;
        var directory = Path.Combine(Path.GetDirectoryName(ConfigFilePath)!, "recovery");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"app_config_{DateTime.UtcNow:yyyyMMdd_HHmmss_fffffff}_{Guid.NewGuid():N}.json");
        File.Copy(ConfigFilePath, path, overwrite: false);
        return path;
    }

    // ========== Hot-Reload via FileSystemWatcher ==========

    public void StartWatching()
    {
        if (_watcher != null) return;

        var dir = Path.GetDirectoryName(ConfigFilePath) ?? AppPaths.DataDirectory;
        Directory.CreateDirectory(dir);

        _watcher = new FileSystemWatcher(dir, ConfigFileName)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime,
            EnableRaisingEvents = true
        };

        Interlocked.Exchange(ref _watcherLastEventTicks, 0);
        Interlocked.Exchange(ref _watcherIgnoreUntilTicks, 0);

        _watcher.Changed += OnWatchedConfigChanged;
        _watcher.Created += OnWatchedConfigChanged;
        _watcher.Renamed += OnWatchedConfigChanged;

        _logger.LogInformation("Config file watcher started");
    }

    public void StopWatching()
    {
        if (_watcher != null)
        {
            _watcher.Changed -= OnWatchedConfigChanged;
            _watcher.Created -= OnWatchedConfigChanged;
            _watcher.Renamed -= OnWatchedConfigChanged;
        }

        _watcher?.Dispose();
        _watcher = null;
    }

    // ========== Backup ==========

    private void CreateBackup(int maxBackups)
    {
        if (!File.Exists(ConfigFilePath) || maxBackups <= 0) return;

        try
        {
            Directory.CreateDirectory(BackupDirectory);
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fffffff", CultureInfo.InvariantCulture);
            var backupPath = Path.Combine(BackupDirectory, $"app_config_{timestamp}_{Guid.NewGuid():N}.json");
            File.Copy(ConfigFilePath, backupPath, overwrite: false);

            var backups = Directory.GetFiles(BackupDirectory, "app_config_*.json")
                .OrderByDescending(f => f)
                .Skip(maxBackups)
                .ToList();

            foreach (var old in backups)
                File.Delete(old);

            _logger.LogDebug("Config backup created: {Path}", backupPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create config backup");
        }
    }

    private void TryMigrateLegacyConfig()
    {
        if (_dataDirectory != null) return;
        if (File.Exists(ConfigFilePath))
            return;

        foreach (var legacyPath in AppPaths.GetLegacyConfigPaths())
        {
            if (!File.Exists(legacyPath))
                continue;

            try
            {
                File.Copy(legacyPath, ConfigFilePath, overwrite: false);
                _logger.LogInformation("Migrated config from legacy path {Path} to {NewPath}", legacyPath, ConfigFilePath);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to migrate legacy config from {Path}", legacyPath);
            }
        }
    }

    // ========== Encryption ==========

    private static byte[] Encrypt(string json)
    {
        var plainBytes = Encoding.UTF8.GetBytes(json);
        return ProtectedData.Protect(plainBytes, null, DataProtectionScope.LocalMachine);
    }

    private static string Decrypt(byte[] encrypted)
    {
        var plainBytes = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.LocalMachine);
        return Encoding.UTF8.GetString(plainBytes);
    }

    private async void OnWatchedConfigChanged(object sender, FileSystemEventArgs e)
    {
        if (ShouldIgnoreWatcherEvent())
            return;

        try
        {
            await Task.Delay(WatcherSettleDelayMilliseconds);
            var config = await TryLoadForHotReloadAsync();
            if (config == null)
                return;

            ConfigChanged?.Invoke(config);
            _logger.LogInformation("Config hot-reloaded from disk");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to hot-reload config");
        }
    }

    private bool ShouldIgnoreWatcherEvent()
    {
        var nowTicks = DateTime.UtcNow.Ticks;
        if (nowTicks <= Interlocked.Read(ref _watcherIgnoreUntilTicks))
            return true;

        var previousTicks = Interlocked.Read(ref _watcherLastEventTicks);
        if (previousTicks != 0
            && TimeSpan.FromTicks(nowTicks - previousTicks).TotalMilliseconds < WatcherDebounceMilliseconds)
        {
            return true;
        }

        Interlocked.Exchange(ref _watcherLastEventTicks, nowTicks);
        return false;
    }

    private async Task<ConfigRoot?> TryLoadForHotReloadAsync()
    {
        try
        {
            return await LoadConfigFileAsync(ConfigFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ignoring invalid external config update");
            return null;
        }
    }

    private async Task<ConfigRoot> LoadConfigFileAsync(string path)
    {
        var bytes = await File.ReadAllBytesAsync(path);
        return ParseConfig(bytes, path);
    }

    private ConfigRoot ParseConfig(byte[] bytes, string path)
    {
        var json = IsEncryptedConfig(bytes)
            ? Decrypt(bytes.AsSpan(MagicHeader.Length).ToArray())
            : Encoding.UTF8.GetString(bytes);

        var root = JsonSerializer.Deserialize<ConfigRoot>(json, AppJsonOptions.Default)
                   ?? throw new InvalidDataException($"Configuration file '{path}' is empty or invalid.");

        using var document = JsonDocument.Parse(json);
        var apps = document.RootElement.EnumerateObject().FirstOrDefault(p => p.Name.Equals("Apps", StringComparison.OrdinalIgnoreCase)).Value;
        if (apps.ValueKind == JsonValueKind.Array && apps.EnumerateArray().Any(app =>
            !app.EnumerateObject().Any(p => p.Name.Equals("AppId", StringComparison.OrdinalIgnoreCase))))
            _migrationNeedsSave = true;

        return MigrateConfig(root);
    }

    private async Task<ConfigRoot?> TryLoadBundledDefaultConfigAsync()
    {
        try
        {
            await using var stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream(BundledDefaultConfigResourceName);
            if (stream == null)
                return null;

            using var reader = new StreamReader(stream, Encoding.UTF8);
            var json = await reader.ReadToEndAsync();
            var root = JsonSerializer.Deserialize<ConfigRoot>(json, AppJsonOptions.Default);
            if (root == null)
                return null;

            root = MigrateConfig(root);
            SanitizeBundledDefaultConfig(root);
            EnsureConfigIsApplicable(root, "initialization");
            return root;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load bundled default config");
            return null;
        }
    }

    private async Task TryPersistBundledDefaultConfigAsync(ConfigRoot config)
    {
        try
        {
            await SaveAsync(config);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist bundled default config to {Path}", ConfigFilePath);
        }
    }

    private static bool IsEncryptedConfig(byte[] bytes) =>
        bytes.Length > MagicHeader.Length
        && bytes[0] == MagicHeader[0]
        && bytes[1] == MagicHeader[1]
        && bytes[2] == MagicHeader[2]
        && bytes[3] == MagicHeader[3];

    internal static ConfigRoot CreatePortableExportCopy(ConfigRoot config)
    {
        ArgumentNullException.ThrowIfNull(config);

        NormalizeConfig(config);

        var json = JsonSerializer.Serialize(config, AppJsonOptions.Default);
        var exportCopy = JsonSerializer.Deserialize<ConfigRoot>(json, AppJsonOptions.Default)
                         ?? throw new InvalidOperationException("Could not clone configuration for export.");

        exportCopy.Settings ??= new AppSettings();
        exportCopy.Settings.AdminPasswordHash = string.Empty;
        exportCopy.Settings.OperatorPasswordHash = string.Empty;
        return exportCopy;
    }

    private static void SanitizeBundledDefaultConfig(ConfigRoot config)
    {
        config.Settings ??= new AppSettings();
        config.Settings.AdminPasswordHash = string.Empty;
        config.Settings.OperatorPasswordHash = string.Empty;
    }

    private static void NormalizeConfig(ConfigRoot config)
    {
        config.Settings ??= new AppSettings();
        config.Settings.OperatorScope ??= new OperatorScopeSettings();
        config.Settings.OperatorScope.Normalize();
    }

    public void Dispose()
    {
        StopWatching();
        _saveLock.Dispose();
    }

    private void EnsureConfigIsApplicable(ConfigRoot config, string operation)
    {
        var result = _validationService.Validate(config);
        _validationService.LogIssues(result);

        if (!result.HasErrors)
            return;

        var summary = ConfigValidationService.BuildSummary(result.Errors);
        throw new InvalidDataException($"Configuration {operation} blocked: {summary}");
    }

    private bool TryValidateWithoutThrow(ConfigRoot config, string operation)
    {
        var result = _validationService.Validate(config);
        _validationService.LogIssues(result);

        if (!result.HasErrors)
            return true;

        _logger.LogWarning(
            "Rejected configuration {Operation} because of blocking issues: {Summary}",
            operation,
            ConfigValidationService.BuildSummary(result.Errors));
        return false;
    }
}
