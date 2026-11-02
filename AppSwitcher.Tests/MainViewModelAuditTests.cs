using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;

namespace AppSwitcher.Tests;

public class MainViewModelAuditTests
{
    [Fact]
    public async Task SaveConfigAsync_Writes_ConfigChanged_Audit_Entry()
    {
        using var harness = await CreateHarnessAsync();
        harness.ViewModel.CurrentAccessLevel = AccessLevel.Admin;
        harness.ViewModel.Settings.BgColor = "#ffffff";

        await harness.ViewModel.SaveConfigAsync();

        var entry = Assert.Single(harness.AuditLog.GetRecentEntries());
        Assert.Equal("ConfigChanged", entry.Action);
        Assert.Equal("Admin", entry.ActorRole);
        Assert.Equal("Configuration", entry.Target);
        Assert.Contains("BgColor: #2c3e50 → #ffffff", entry.Details);
    }

    [Fact]
    public async Task RestoreBackupCommand_Writes_BackupRestored_Audit_Entry()
    {
        var restoredConfig = new ConfigRoot
        {
            Settings = new AppSettings { Theme = "Light" },
            Apps = [new AppItem { Name = "Worker", Path = "worker.exe" }]
        };
        using var harness = await CreateHarnessAsync(configService: new TestConfigService { RestoreResult = restoredConfig });
        harness.ViewModel.CurrentAccessLevel = AccessLevel.Admin;

        var settingsVm = new SettingsViewModel(
            harness.ViewModel,
            harness.ConfigService,
            new SecurityService(),
            harness.NotificationService,
            new ScreenLayoutService(NullLogger<ScreenLayoutService>.Instance, harness.ProcessHelper),
            new CrashHistoryService(NullLogger<CrashHistoryService>.Instance),
            new SessionTimeoutService(NullLogger<SessionTimeoutService>.Instance),
            AccessLevel.Admin);

        try
        {
            var backup = new ConfigService.ConfigBackupInfo(@"C:\temp\backup.json", "backup.json", new DateTime(2026, 4, 17, 10, 15, 0), 512);

            await settingsVm.RestoreBackupCommand.ExecuteAsync(backup);

            var entry = Assert.Single(harness.AuditLog.GetRecentEntries());
            Assert.Equal("BackupRestored", entry.Action);
            Assert.Equal("Success", entry.Outcome);
            Assert.Equal("backup.json", entry.Target);
        }
        finally
        {
            settingsVm.Dispose();
        }
    }

    [Fact]
    public async Task KioskToggle_Records_Specific_Audit_And_Suppresses_Generic_Config_Audit()
    {
        using var harness = await CreateHarnessAsync();
        harness.ViewModel.CurrentAccessLevel = AccessLevel.Admin;

        var auditTcs = new TaskCompletionSource<AuditLogEntry>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(AuditLogEntry entry)
        {
            if (entry.Action == "KioskToggled")
                auditTcs.TrySetResult(entry);
        }

        harness.AuditLog.EntryRecorded += Handler;
        try
        {
            harness.ViewModel.Settings.KioskMode = true;
            var kioskEntry = await auditTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.Equal("Success", kioskEntry.Outcome);
            Assert.Equal("Enabled", kioskEntry.Target);

            await harness.ViewModel.SaveConfigAsync();

            var entries = harness.AuditLog.GetRecentEntries();
            Assert.Single(entries);
            Assert.Equal("KioskToggled", entries[0].Action);
        }
        finally
        {
            harness.AuditLog.EntryRecorded -= Handler;
        }
    }

    [Fact]
    public async Task KillAppCommand_Writes_AppStopped_Audit_Entry()
    {
        var app = new AppItem { Name = "Pump UI", Path = "pump.exe", IsRunning = true };
        using var harness = await CreateHarnessAsync(new ConfigRoot
        {
            Settings = new AppSettings(),
            Apps = [app]
        });
        harness.ViewModel.CurrentAccessLevel = AccessLevel.Admin;

        harness.ViewModel.KillAppCommand.Execute(app);

        var entry = Assert.Single(harness.AuditLog.GetRecentEntries());
        Assert.Equal("AppStopped", entry.Action);
        Assert.Equal("Pump UI", entry.Target);
        Assert.Contains("Path: pump.exe", entry.Details);
    }

    [Fact]
    public async Task StopGroupCommand_Writes_One_Entry_Per_Killed_App()
    {
        var appOne = new AppItem { Name = "Pump UI", Path = "pump.exe", IsRunning = true };
        var appTwo = new AppItem { Name = "Trend UI", Path = "trend.exe", IsRunning = true };
        using var harness = await CreateHarnessAsync(new ConfigRoot
        {
            Settings = new AppSettings(),
            Apps = [appOne, appTwo]
        });
        harness.ViewModel.CurrentAccessLevel = AccessLevel.Admin;

        var group = new AppGroup("operations", "Operations")
        {
            Apps = new ObservableCollection<AppItem> { appOne, appTwo }
        };
        harness.ViewModel.StopGroupCommand.Execute(group);

        var entries = harness.AuditLog.GetRecentEntries();
        Assert.Equal(2, entries.Count);
        Assert.All(entries, entry => Assert.Equal("AppStopped", entry.Action));
        Assert.All(entries, entry => Assert.Contains("Group: Operations", entry.Details));
    }

    public static async Task<TestHarness> CreateHarnessAsync(
        ConfigRoot? initialConfig = null,
        TestConfigService? configService = null)
    {
        var auditPath = Path.Combine(Path.GetTempPath(), $"audit-{Guid.NewGuid():N}.csv");
        var auditLog = new AuditLogService(NullLogger<AuditLogService>.Instance, auditPath);
        var validation = new ConfigValidationService(NullLogger<ConfigValidationService>.Instance);
        var config = configService ?? new TestConfigService();
        var processHelper = new TestProcessHelper();
        var notificationService = new NotificationService();
        var lifecycleService = new NoOpAppLifecycleService();
        var crashHistory = new CrashHistoryService(NullLogger<CrashHistoryService>.Instance);
        var watchdog = new WatchdogService(
            NullLogger<WatchdogService>.Instance,
            processHelper,
            lifecycleService,
            notificationService,
            crashHistory,
            new MemoryWatchdogCircuitStore(),
            TimeProvider.System);
        var scheduler = new SchedulerService(
            NullLogger<SchedulerService>.Instance,
            processHelper,
            lifecycleService,
            watchdog,
            notificationService);
        var kioskService = new KioskService(
            NullLogger<KioskService>.Instance,
            new TestKioskPlatform(),
            new TestConfiguredAppMatcher(),
            new KioskForegroundPolicy(),
            new TestMachineLockdownService());
        var windowPolicy = new WindowPolicyEnforcementService(
            NullLogger<WindowPolicyEnforcementService>.Instance,
            processHelper,
            new TestWindowPolicyPlatform());
        var viewModel = new MainViewModel(
            config,
            processHelper,
            lifecycleService,
            kioskService,
            watchdog,
            notificationService,
            new SecurityService(),
            new ThemeService(NullLogger<ThemeService>.Instance),
            new LocalizationService(NullLogger<LocalizationService>.Instance),
            scheduler,
            new ConfigDiffService(NullLogger<ConfigDiffService>.Instance),
            auditLog,
            new SessionTimeoutService(NullLogger<SessionTimeoutService>.Instance),
            new HealthCheckService(NullLogger<HealthCheckService>.Instance),
            new ResourceMonitorService(NullLogger<ResourceMonitorService>.Instance, processHelper, notificationService),
            new ScreenLayoutService(NullLogger<ScreenLayoutService>.Instance, processHelper),
            windowPolicy,
            validation,
            crashHistory,
            new AppVersionService(NullLogger<AppVersionService>.Instance, notificationService),
            NullLogger<MainViewModel>.Instance);

        var configRoot = CloneConfig(initialConfig ?? new ConfigRoot { Settings = new AppSettings() });
        await viewModel.ApplyConfigAsync(configRoot, showNotifications: false);
        viewModel.MarkConfigAsPersisted(configRoot);

        return new TestHarness(viewModel, config, processHelper, notificationService, auditLog, auditPath);
    }

    private static ConfigRoot CloneConfig(ConfigRoot config)
    {
        var json = JsonSerializer.Serialize(config, AppJsonOptions.Default);
        return JsonSerializer.Deserialize<ConfigRoot>(json, AppJsonOptions.Default)
               ?? throw new InvalidOperationException("Failed to clone test config.");
    }

    public sealed class TestHarness : IDisposable
    {
        public TestHarness(
            MainViewModel viewModel,
            TestConfigService configService,
            TestProcessHelper processHelper,
            NotificationService notificationService,
            AuditLogService auditLog,
            string auditPath)
        {
            ViewModel = viewModel;
            ConfigService = configService;
            ProcessHelper = processHelper;
            NotificationService = notificationService;
            AuditLog = auditLog;
            AuditPath = auditPath;
        }

        public MainViewModel ViewModel { get; }
        public TestConfigService ConfigService { get; }
        public TestProcessHelper ProcessHelper { get; }
        public NotificationService NotificationService { get; }
        public AuditLogService AuditLog { get; }
        public string AuditPath { get; }

        public void Dispose()
        {
            ViewModel.Dispose();
            if (File.Exists(AuditPath))
                File.Delete(AuditPath);
        }
    }

    public sealed class TestConfigService : ConfigService
    {
        public TestConfigService()
            : base(NullLogger<ConfigService>.Instance, new ConfigValidationService(NullLogger<ConfigValidationService>.Instance))
        {
        }

        public ConfigRoot? LastSavedConfig { get; private set; }
        public ConfigRoot? RestoreResult { get; set; }
        public bool FailSave { get; set; }
        public Func<Task>? BeforeSave { get; set; }

        public override List<ConfigService.ConfigBackupInfo> GetBackups() => [];

        public override async Task SaveAsync(ConfigRoot config)
        {
            if (BeforeSave != null) await BeforeSave();
            if (FailSave) throw new IOException("Test storage is unavailable.");
            LastSavedConfig = CloneConfig(config);
        }

        public override Task<ConfigService.BackupPreview?> ReadBackupPreviewAsync(string path) =>
            Task.FromResult(RestoreResult == null ? null : new ConfigService.BackupPreview(path, "test-fingerprint", CloneConfig(RestoreResult)));

        public override Task<ConfigRoot?> RestoreBackupAsync(string backupPath, Action<ConfigRoot>? beforeSave = null,
            string? expectedFingerprint = null)
        {
            if (RestoreResult == null)
                return Task.FromResult<ConfigRoot?>(null);

            var restored = CloneConfig(RestoreResult);
            beforeSave?.Invoke(restored);
            return Task.FromResult<ConfigRoot?>(restored);
        }
    }

    public sealed class TestProcessHelper : ProcessHelper
    {
        public TestProcessHelper()
            : base(NullLogger<ProcessHelper>.Instance)
        {
        }

        public override int KillApp(AppProcessTarget target) => 1;
    }

    private sealed class TestMachineLockdownService : IKioskMachineLockdownService
    {
        public Task<KioskMachineLockdownResult> ApplyAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(KioskMachineLockdownResult.AlreadyApplied("configured"));

        public Task<KioskMachineLockdownResult> RemoveAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(KioskMachineLockdownResult.Removed("removed"));

        public Task<KioskMachineLockdownResult> QueryAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(KioskMachineLockdownResult.NotConfigured("not configured"));
    }

    private sealed class TestConfiguredAppMatcher : IKioskConfiguredAppMatcher
    {
        public bool IsConfiguredAppWindow(KioskWindowSnapshot snapshot, IReadOnlyCollection<AppProcessTarget> targets) => false;
    }

    private sealed class TestKioskPlatform : IKioskPlatform
    {
        public bool TryInstallKeyboardHook(KioskKeyboardProc callback, out IntPtr hookHandle)
        {
            hookHandle = new IntPtr(1);
            return true;
        }

        public void RemoveKeyboardHook(IntPtr hookHandle)
        {
        }

        public IReadOnlyList<IntPtr> GetTaskbarHandles() => [];

        public bool HideWindow(IntPtr hwnd) => true;

        public bool ShowWindow(IntPtr hwnd) => true;

        public bool IsWindowVisible(IntPtr hwnd) => false;

        public KioskWindowSnapshot? TryGetForegroundWindow() => null;

        public bool RestoreWindow(IntPtr hwnd) => true;

        public bool SetForegroundWindow(IntPtr hwnd) => true;

        public bool BringWindowToTop(IntPtr hwnd) => true;

        public bool SetWindowTopmost(IntPtr hwnd, bool activate) => true;
    }

    private sealed class TestWindowPolicyPlatform : IWindowPolicyPlatform
    {
        public bool IsWindow(IntPtr hwnd) => false;
        public bool IsWindowMinimized(IntPtr hwnd) => false;
        public bool RestoreWindow(IntPtr hwnd) => false;
        public bool MaximizeWindow(IntPtr hwnd) => false;
        public bool TryGetWindowRect(IntPtr hwnd, out NativeRect rect)
        {
            rect = default;
            return false;
        }

        public bool SetWindowRect(IntPtr hwnd, NativeRect rect, bool activate) => false;
        public bool IsTopmost(IntPtr hwnd) => false;
        public bool SetTopmost(IntPtr hwnd, bool topmost, bool activate) => false;
        public bool TryGetWindowStyle(IntPtr hwnd, out WindowStyleState style)
        {
            style = default;
            return false;
        }

        public bool SetWindowStyle(IntPtr hwnd, WindowStyleState style) => false;
        public IReadOnlyList<NativeMonitorInfo> GetMonitors() => [];
        public int GetCurrentMonitorIndex(IntPtr hwnd) => 0;
    }
}
