using Microsoft.Extensions.Logging.Abstractions;

namespace AppSwitcher.Tests;

public class SetupAndMonitoringTests
{
    [Fact]
    public async Task AttentionFilterTracksRecoveryAcknowledgementAndOperatorScope()
    {
        using var fixture = await MainViewModelAuditTests.CreateHarnessAsync(new ConfigRoot
        {
            Apps = [new() { Name = "Visible", Path = "visible.exe" }, new() { Name = "Hidden", Path = "hidden.exe" }],
            Settings = new() { OperatorScope = new() { AllowedAppNames = ["Visible"] } }
        });
        var vm = fixture.ViewModel;
        vm.CurrentAccessLevel = AccessLevel.Operator;
        var app = vm.Apps[0];
        vm.Apps[1].State = AppState.Crashed;
        vm.ShowNeedsAttentionOnly = true;
        Assert.Empty(vm.MonitoringApps);
        foreach (var state in new[] { AppState.Crashed, AppState.Restarting, AppState.Unhealthy, AppState.Recovering })
        {
            app.State = state;
            Assert.Same(app, Assert.Single(vm.MonitoringApps));
        }
        app.State = AppState.Running;
        Assert.Empty(vm.MonitoringApps);
        app.IsRunning = true;
        app.IsResponding = false;
        Assert.Same(app, Assert.Single(vm.MonitoringApps));
        app.IsResponding = true;
        app.RequiresWatchdogAcknowledgement = true;
        app.State = AppState.Stopped;
        Assert.Same(app, Assert.Single(vm.MonitoringApps));
        app.RequiresWatchdogAcknowledgement = false;
        Assert.Empty(vm.MonitoringApps);
        vm.ShowNeedsAttentionOnly = false;
        Assert.Same(app, Assert.Single(vm.MonitoringApps));
        vm.CurrentAccessLevel = AccessLevel.Admin;
        Assert.Equal(2, vm.MonitoringApps.Count);
        vm.ShowNeedsAttentionOnly = true;
        Assert.Same(vm.Apps[1], Assert.Single(vm.MonitoringApps));
        vm.Apps.RemoveAt(1);
        Assert.Empty(vm.MonitoringApps);
        Assert.DoesNotContain("NeedsAttention", JsonSerializer.Serialize(app));
    }

    [Theory]
    [InlineData("Ready", "Browser_RepairReady")]
    [InlineData("ConnectionUnavailable", "Browser_RepairConnection")]
    [InlineData("ProfileRequired", "Browser_RepairPairing")]
    [InlineData("ProfileConflict", "Browser_RepairProfileConflict")]
    [InlineData("IdentityConflict", "Browser_RepairIdentity")]
    [InlineData("Recovering", "Browser_RepairIdentity")]
    [InlineData("InvalidIdentity", "Browser_RepairInvalidIdentity")]
    [InlineData("ProfileChanged", "Browser_RepairProfileChanged")]
    [InlineData("JournalUnavailable", "Browser_RepairJournal")]
    [InlineData("InvalidUrl", "Browser_RepairUrl")]
    [InlineData("Absent", "Browser_RepairAbsent")]
    [InlineData("CheckTimeout", "Browser_RepairConnection")]
    public void BrowserRecoveryFollowsTheLatestFaultAndHostAvailability(string code, string expected)
    {
        var row = new BrowserConnectionRow { HostAvailable = true, StateCode = "Ready" };
        var notified = new List<string?>();
        row.PropertyChanged += (_, args) => notified.Add(args.PropertyName);
        row.StateCode = code;
        Assert.Equal(expected, row.ActionHint);
        if (code != "Ready") Assert.Contains(nameof(row.ActionHint), notified);
        Assert.Equal("Browser_RepairHost", BrowserConnectionRow.RecoveryKey(code, false));
    }

    [Fact]
    public async Task SetupReflectsChangesAndCannotNavigateAfterSessionExpiry()
    {
        using var fixture = await MainViewModelAuditTests.CreateHarnessAsync(new ConfigRoot());
        var vm = fixture.ViewModel;
        vm.CurrentAccessLevel = AccessLevel.Admin;
        using var session = new SessionTimeoutService(NullLogger<SessionTimeoutService>.Instance);
        using var editor = new SettingsViewModel(vm, fixture.ConfigService, new SecurityService(), fixture.NotificationService,
            new ScreenLayoutService(NullLogger<ScreenLayoutService>.Instance, fixture.ProcessHelper),
            new CrashHistoryService(NullLogger<CrashHistoryService>.Instance), session);
        Assert.Equal("Setup_Review", editor.SetupSteps[0].Status);
        vm.Apps.Add(new() { Name = "Native", Path = "native.exe" });
        Assert.Equal("Setup_Configured", editor.SetupSteps[0].Status);
        vm.Settings.AdminPasswordHash = new SecurityService().HashPassword("1234");
        Assert.Equal("Setup_Review", editor.SetupSteps[1].Status);
        vm.Settings.SessionTimeoutMinutes = 5;
        Assert.Equal("Setup_Configured", editor.SetupSteps[1].Status);
        Assert.Equal("Setup_Optional", editor.SetupSteps[2].Status);
        var website = new AppItem { Name = "Site", TargetKind = AppTargetKind.Website, WebsiteUrl = "https://example.com" };
        vm.Apps.Add(website);
        Assert.Equal("Setup_Review", editor.SetupSteps[2].Status);
        website.BrowserProfileId = Guid.NewGuid().ToString();
        Assert.Equal("Setup_Configured", editor.SetupSteps[2].Status);
        vm.Layouts.Add(new ScreenLayout { Name = "Workspace" });
        Assert.Equal("Setup_Configured", editor.SetupSteps[3].Status);
        Assert.Equal("Setup_TestRequired", editor.SetupSteps[5].Status);
        SettingsSearchResult? destination = null;
        editor.NavigateToSetting += result => destination = result;
        editor.OpenSetupStepCommand.Execute(editor.SetupSteps[4]);
        Assert.Equal("System", destination!.Section);
        Assert.Equal(3, destination.Tab);
        editor.ActiveSection = "Setup";
        vm.CurrentAccessLevel = AccessLevel.None;
        editor.OpenSetupStepCommand.Execute(editor.SetupSteps[1]);
        Assert.Equal("Setup", editor.ActiveSection);
    }

    [Fact]
    public async Task OnlyNewConfigurationOpensTheFirstRunChecklist()
    {
        var folder = Path.Combine(Path.GetTempPath(), "AppSwitcher-FirstRun-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var config = new ConfigService(NullLogger<ConfigService>.Instance, new(NullLogger<ConfigValidationService>.Instance), folder);
            await config.LoadAsync();
            Assert.True(config.IsFirstRun);
            using var fixture = await MainViewModelAuditTests.CreateHarnessAsync();
            using var session = new SessionTimeoutService(NullLogger<SessionTimeoutService>.Instance);
            using var editor = new SettingsViewModel(fixture.ViewModel, config, new SecurityService(), fixture.NotificationService,
                new ScreenLayoutService(NullLogger<ScreenLayoutService>.Instance, fixture.ProcessHelper),
                new CrashHistoryService(NullLogger<CrashHistoryService>.Instance), session);
            Assert.Equal("Setup", editor.ActiveSection);
            using var existing = new ConfigService(NullLogger<ConfigService>.Instance, new(NullLogger<ConfigValidationService>.Instance), folder);
            await existing.LoadAsync();
            Assert.False(existing.IsFirstRun);
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
    }
}
