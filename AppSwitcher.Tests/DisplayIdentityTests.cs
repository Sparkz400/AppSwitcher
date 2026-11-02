using Microsoft.Extensions.Logging.Abstractions;

namespace AppSwitcher.Tests;

public class DisplayIdentityTests
{
    private static DisplayInfo Display(int index, string id, double x, bool primary = false, double scale = 1) =>
        new(index, id, "Example panel", new(x, 0, 1920, 1080), new(x, 0, 1920, 1040), primary, scale);

    [Fact]
    public void CaptureAndRestoreUsePhysicalPixelsAndIdentityAfterReordering()
    {
        var first = Display(1, "panel-a", -1920, scale: 1.5);
        var second = Display(2, "panel-b", 0, primary: true, scale: 1.5);
        var bounds = new Rect(-1800, 100, 800, 600);
        var saved = ScreenLayoutService.CapturePosition(new() { Name = "App" }, bounds, [first, second]);
        Assert.Equal("panel-a", saved.MonitorDeviceId);
        Assert.Equal(1920, saved.SavedMonitorWidth);
        Assert.Equal(1280, first.WpfWorkArea.Width);
        Assert.Equal(bounds, ScreenLayoutService.ComputeRestoreBounds(saved, new[] { second with { Index = 1 }, first with { Index = 2 } }));
        var resized = first with { Index = 2, WorkArea = new(-1280, -500, 1280, 720), DpiScale = 2 };
        var restored = ScreenLayoutService.ComputeRestoreBounds(saved, new[] { second with { Index = 1 }, resized });
        Assert.True(resized.WorkArea.Contains(restored));
        Assert.Equal(800, restored.Width);
        Assert.Equal(600, restored.Height);
    }

    [Fact]
    public void MissingAndAmbiguousIdsUsePrimaryWithoutReassigningTheSavedIdentity()
    {
        var saved = new WindowPosition { MonitorIndex = 1, MonitorDeviceId = "unplugged", Left = -900, Top = 50, Width = 3000, Height = 1500 };
        var other = Display(1, "other", -1920);
        var primary = Display(2, "primary", 0, true);
        Assert.Equal(primary.WorkArea, ScreenLayoutService.ComputeRestoreBounds(saved, new[] { other, primary }));
        Assert.Equal("unplugged", saved.MonitorDeviceId);
        Assert.Equal(2, MonitorHelper.ResolveDisplay(1, "duplicate", [other with { DeviceId = "duplicate" }, primary with { DeviceId = "duplicate" }])!.Index);
        Assert.Equal(0, MonitorHelper.FindConfiguredDisplay(1, "missing", [other, primary]));
        Assert.Equal(1, MonitorHelper.ResolveDisplay(1, "", [other, primary])!.Index);
        Assert.Equal(2, MonitorHelper.ResolveDisplay(99, "", [other, primary])!.Index);
        Assert.Null(MonitorHelper.ResolveDisplay(1, "missing", []));
    }

    [Fact]
    public async Task EditorPreservesOfflineIdentityAndStoresExplicitDisplaySelection()
    {
        using var fixture = await MainViewModelAuditTests.CreateHarnessAsync(new ConfigRoot
        {
            Settings = new() { PreferredMonitor = 1, PreferredMonitorId = "saved" },
            Apps = [new() { Name = "App", Path = "app.exe", ForcedMonitor = 1, ForcedMonitorId = "saved" }]
        });
        var vm = fixture.ViewModel;
        vm.CurrentAccessLevel = AccessLevel.Admin;
        using var session = new SessionTimeoutService(NullLogger<SessionTimeoutService>.Instance);
        using var editor = new SettingsViewModel(vm, fixture.ConfigService, new SecurityService(), fixture.NotificationService,
            new ScreenLayoutService(NullLogger<ScreenLayoutService>.Instance, fixture.ProcessHelper),
            new CrashHistoryService(NullLogger<CrashHistoryService>.Instance), session);
        editor.UpdateMonitorChoices([Display(1, "new", 0, true)]);
        Assert.Equal(-1, editor.LauncherMonitorSelection);
        Assert.Equal(-1, editor.AppMonitorSelection);
        Assert.Contains(editor.MonitorChoices, option => option.Value == -1);
        Assert.False(editor.HasUnsavedChanges);
        editor.LauncherMonitorSelection = -1;
        editor.AppMonitorSelection = -1;
        Assert.Equal("saved", editor.Settings.PreferredMonitorId);
        Assert.Equal("saved", editor.SelectedApp!.ForcedMonitorId);
        editor.UpdateMonitorChoices([Display(1, "new", 0, true), Display(2, "SAVED", 1920)]);
        Assert.Equal(2, editor.LauncherMonitorSelection);
        Assert.Equal(2, editor.AppMonitorSelection);
        Assert.False(editor.HasUnsavedChanges);
        editor.LauncherMonitorSelection = 1;
        editor.AppMonitorSelection = 1;
        Assert.Equal("new", editor.Settings.PreferredMonitorId);
        Assert.Equal("new", editor.SelectedApp.ForcedMonitorId);
        editor.LauncherMonitorSelection = 0;
        editor.AppMonitorSelection = 0;
        Assert.Equal("", editor.Settings.PreferredMonitorId);
        Assert.Equal("", editor.SelectedApp.ForcedMonitorId);
        vm.CurrentAccessLevel = AccessLevel.None;
        editor.LauncherMonitorSelection = 1;
        editor.AppMonitorSelection = 1;
        Assert.Equal("", editor.Settings.PreferredMonitorId);
        Assert.Equal("", editor.SelectedApp.ForcedMonitorId);
    }

    [Theory]
    [InlineData("bad\nidentity")]
    [InlineData("oversize")]
    public void InvalidIdentityMetadataIsRejectedAtAllImportLocations(string value)
    {
        if (value == "oversize") value = new string('x', 513);
        var config = new ConfigRoot
        {
            Settings = new() { PreferredMonitorId = value },
            Apps = [new() { Name = "App", Path = "app.exe", ForcedMonitorId = value }],
            Layouts = [new() { Name = "Layout", Positions = [new() { MonitorDeviceId = value }] }]
        };
        var result = new ConfigValidationService(NullLogger<ConfigValidationService>.Instance).Validate(config);
        Assert.Equal(3, result.Errors.Count(error => error.Message == "Display_InvalidIdentity"));
    }
}
