using Microsoft.Extensions.Logging.Abstractions;

namespace AppSwitcher.Tests;

public class ConfigurationRecoveryTests
{
    [Fact]
    public void BackupPreviewDescribesLaunchAndPermissionChangesWithoutExposingSecrets()
    {
        var previous = new ConfigRoot { Apps = [new AppItem { Name = "Worker", Path = "worker.exe" }] };
        var next = JsonSerializer.Deserialize<ConfigRoot>(JsonSerializer.Serialize(previous))!;
        next.Apps[0].Args = "--password private-argument";
        next.Apps[0].WorkingDirectory = "C:\\private-folder";
        next.Apps[0].ScheduleRestartTime = "10:30";
        next.Settings.AdminPasswordHash = new SecurityService().HashPassword("1234");
        next.Settings.OperatorScope.AllowedAppNames = ["Worker"];
        next.Layouts.Add(new ScreenLayout { Name = "New layout" });
        var changes = new ConfigDiffService(NullLogger<ConfigDiffService>.Instance).Compute(previous, next);
        Assert.Contains(changes.Changes, change => change.Key.EndsWith(":arguments"));
        Assert.Contains(changes.Changes, change => change.Key.EndsWith(":workingdirectory"));
        Assert.Contains(changes.Changes, change => change.Key.EndsWith(":schedulerestart"));
        Assert.Contains(changes.Changes, change => change.Key == "settings.operatorscope");
        Assert.Contains(changes.Changes, change => change.Key == "layouts");
        var details = changes.BuildDetails();
        Assert.DoesNotContain("private-argument", details);
        Assert.DoesNotContain("private-folder", details);
        Assert.DoesNotContain(next.Settings.AdminPasswordHash, details);
    }

    [Fact]
    public async Task FailedStartupPreservesTheExactDamagedFile()
    {
        using var fixture = new RecoveryFiles();
        await File.WriteAllTextAsync(fixture.ConfigPath, "{broken settings");
        await Assert.ThrowsAsync<ConfigurationLoadException>(() => fixture.Service.LoadAsync());
        Assert.Equal("{broken settings", await File.ReadAllTextAsync(fixture.ConfigPath));
    }

    [Fact]
    public async Task RestorePreservesDamagedCurrentFileAndSelectedBackup()
    {
        using var fixture = new RecoveryFiles();
        await File.WriteAllTextAsync(fixture.ConfigPath, "damaged original");
        var path = await fixture.BackupAsync(new ConfigRoot { Settings = new AppSettings { Theme = "Light", MaxConfigBackups = 0 } });
        var bytes = await File.ReadAllBytesAsync(path);
        var preview = await fixture.Service.ReadBackupPreviewAsync(path);
        Assert.NotNull(preview);
        var restored = await fixture.Service.RestoreBackupAsync(path, expectedFingerprint: preview.Fingerprint);
        Assert.NotNull(restored);
        Assert.Equal("Light", (await fixture.Service.LoadAsync()).Settings.Theme);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        var preserved = Assert.Single(Directory.GetFiles(Path.Combine(fixture.Directory, "recovery")));
        Assert.Equal("damaged original", await File.ReadAllTextAsync(preserved));
    }

    [Fact]
    public async Task ChangedBackupCannotReplaceTheReviewedConfiguration()
    {
        using var fixture = new RecoveryFiles();
        await fixture.Service.SaveAsync(new ConfigRoot());
        var original = await File.ReadAllBytesAsync(fixture.ConfigPath);
        var path = await fixture.BackupAsync(new ConfigRoot());
        var preview = await fixture.Service.ReadBackupPreviewAsync(path);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new ConfigRoot { Settings = new AppSettings { KioskMode = true } }));
        Assert.Null(await fixture.Service.RestoreBackupAsync(path, expectedFingerprint: preview!.Fingerprint));
        Assert.Equal(original, await File.ReadAllBytesAsync(fixture.ConfigPath));
        Assert.False(Directory.Exists(Path.Combine(fixture.Directory, "recovery")));
    }

    [Fact]
    public async Task FailedPreservationAndExpiredAuthorizationBothLeaveCurrentFileUntouched()
    {
        using var fixture = new RecoveryFiles();
        await File.WriteAllTextAsync(fixture.ConfigPath, "original");
        var path = await fixture.BackupAsync(new ConfigRoot());
        Assert.Null(await fixture.Service.RestoreBackupAsync(path, _ => throw new UnauthorizedAccessException("expired")));
        Assert.False(Directory.Exists(Path.Combine(fixture.Directory, "recovery")));
        await File.WriteAllTextAsync(Path.Combine(fixture.Directory, "recovery"), "blocked directory");
        Assert.Null(await fixture.Service.RestoreBackupAsync(path));
        Assert.Equal("original", await File.ReadAllTextAsync(fixture.ConfigPath));
    }

    [Fact]
    public async Task MultipleSavesDoNotOverwriteBackupsAndRetentionIsBounded()
    {
        using var fixture = new RecoveryFiles();
        var config = new ConfigRoot { Settings = new AppSettings { MaxConfigBackups = 4 } };
        for (var iteration = 0; iteration < 8; iteration++)
        {
            config.Settings.PosX = iteration;
            await fixture.Service.SaveAsync(config);
        }
        var backups = fixture.Service.GetBackups();
        Assert.Equal(4, backups.Count);
        var savedPositions = new List<double>();
        foreach (var backup in backups)
            savedPositions.Add((await fixture.Service.ReadBackupPreviewAsync(backup.FilePath))!.Config.Settings.PosX);
        Assert.Equal(new double[] { 6, 5, 4, 3 }, savedPositions);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"Settings\":{\"AdminPasswordHash\":\"broken\"}}")]
    [InlineData("{\"Apps\":[{\"Name\":\"invalid\",\"Path\":\"\"}]}")]
    public async Task InvalidBackupIsRejectedWithoutWriting(string contents)
    {
        using var fixture = new RecoveryFiles();
        var path = Path.Combine(fixture.Directory, "invalid.json");
        await File.WriteAllTextAsync(path, contents);
        Assert.Null(await fixture.Service.ReadBackupPreviewAsync(path));
        Assert.Null(await fixture.Service.RestoreBackupAsync(path));
        Assert.False(File.Exists(fixture.ConfigPath));
    }

    private sealed class RecoveryFiles : IDisposable
    {
        public string Directory { get; } = Path.Combine(Path.GetTempPath(), "AppSwitcher-Recovery-" + Guid.NewGuid().ToString("N"));
        public string ConfigPath => Path.Combine(Directory, "app_config.json");
        public ConfigService Service { get; }
        public RecoveryFiles()
        {
            System.IO.Directory.CreateDirectory(Directory);
            Service = new ConfigService(NullLogger<ConfigService>.Instance,
                new ConfigValidationService(NullLogger<ConfigValidationService>.Instance), Directory);
        }
        public async Task<string> BackupAsync(ConfigRoot config)
        {
            var folder = Path.Combine(Directory, "config_backups");
            System.IO.Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, $"app_config_{DateTime.Now:yyyyMMdd_HHmmss}.json");
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(config, AppJsonOptions.Default));
            return path;
        }
        public void Dispose() { Service.Dispose(); System.IO.Directory.Delete(Directory, true); }
    }
}
