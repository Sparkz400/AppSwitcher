using System.Globalization;
using System.Windows;
using AppSwitcher.Converters;
using Microsoft.Extensions.Logging.Abstractions;

namespace AppSwitcher.Tests;

public class PresentationTests
{
    [Theory]
    [InlineData(AppState.Running, true, "State_Running")]
    [InlineData(AppState.Running, false, "State_NotResponding")]
    [InlineData(AppState.Stopped, false, "State_Stopped")]
    [InlineData(AppState.Restarting, false, "State_Restarting")]
    [InlineData(AppState.Crashed, true, "State_Crashed")]
    [InlineData(AppState.Unhealthy, true, "State_Unhealthy")]
    public void StateText_DoesNotMistakeStoppedAppsForHungApps(AppState state, bool responding, string key)
    {
        Assert.Equal(key, new AppStateTextConverter().Convert([state, responding], typeof(string), null!, CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("Applications", "Applications", true)]
    [InlineData("Access", "Applications", false)]
    [InlineData(null, "Applications", false)]
    public void SectionVisibility_MatchesOnlyTheSelectedSection(string? selected, string section, bool visible)
    {
        Assert.Equal(visible ? Visibility.Visible : Visibility.Collapsed,
            new SectionVisibilityConverter().Convert(selected!, typeof(Visibility), section, CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Editor_ReordersWithoutLosingSelection_AndKeepsColoursSeparate()
    {
        using var fixture = await MainViewModelAuditTests.CreateHarnessAsync(new ConfigRoot { Apps = [new() { Name = "A", Path = "a.exe" }, new() { Name = "B", Path = "b.exe" }] });
        fixture.ViewModel.CurrentAccessLevel = AccessLevel.Admin;
        using var editor = CreateEditor(fixture, AccessLevel.Admin);
        var selected = editor.SelectedApp!;
        editor.MoveAppCommand.Execute("-1");
        Assert.Same(selected, editor.Apps[0]);
        editor.MoveAppCommand.Execute(int.MinValue.ToString());
        Assert.Same(selected, editor.Apps[0]);
        editor.MoveAppCommand.Execute("1");
        Assert.Same(selected, editor.Apps[1]);
        Assert.Same(selected, editor.SelectedApp);
        var launcherColour = editor.Settings.BgColor;
        editor.SetBackgroundColorCommand.Execute("#112233");
        Assert.Equal("#112233", selected.BgColor);
        Assert.Equal(launcherColour, editor.Settings.BgColor);
        editor.ActiveSection = "System";
        editor.SetBackgroundColorCommand.Execute("#445566");
        Assert.Equal("#445566", editor.Settings.BgColor);
        Assert.Equal("#112233", selected.BgColor);
        editor.HasInputErrors = true;
        Assert.False(editor.SaveCommand.CanExecute(null));
        await editor.SaveCommand.ExecuteAsync(null);
        Assert.Null(fixture.ConfigService.LastSavedConfig);
        editor.MoveAppCommand.Execute("1");
        editor.HasInputErrors = false;
        await editor.SaveCommand.ExecuteAsync(null);
        Assert.True(editor.LastSaveSucceeded);
        Assert.Equal("B", fixture.ConfigService.LastSavedConfig!.Apps[0].Name);
    }

    [Fact]
    public async Task SaveActionTracksEditsRevertsAndSuccessfulSaves()
    {
        using var fixture = await MainViewModelAuditTests.CreateHarnessAsync();
        using var editor = CreateEditor(fixture, AccessLevel.Admin);
        Assert.False(editor.HasUnsavedChanges);
        Assert.False(editor.ShowSaveAction);
        Assert.False(editor.SaveCommand.CanExecute(null));
        await editor.SaveCommand.ExecuteAsync(null);
        Assert.Null(fixture.ConfigService.LastSavedConfig);

        var original = editor.Settings.MenuWidth;
        editor.Settings.MenuWidth = original + 10;
        Assert.True(editor.ShowSaveAction);
        Assert.True(editor.SaveCommand.CanExecute(null));
        editor.Settings.MenuWidth = original;
        Assert.False(editor.ShowSaveAction);
        editor.Settings.MenuWidth = original + 20;
        await editor.SaveCommand.ExecuteAsync(null);
        Assert.True(editor.LastSaveSucceeded);
        Assert.False(editor.ShowSaveAction);
        Assert.False(editor.SaveCommand.CanExecute(null));
        editor.Settings.MenuWidth = original;
        Assert.True(editor.HasUnsavedChanges);
        await fixture.ViewModel.SaveConfigAsync();
        Assert.False(editor.ShowSaveAction);
    }

    [Fact]
    public async Task RuntimeUpdatesAndEditorNavigationDoNotShowSave()
    {
        using var fixture = await MainViewModelAuditTests.CreateHarnessAsync(new ConfigRoot { Apps = [new() { Name = "A", Path = "a.exe" }] });
        using var editor = CreateEditor(fixture, AccessLevel.Admin);
        var app = editor.Apps[0];
        app.IsRunning = true;
        app.State = AppState.Running;
        app.CpuPercent = 30;
        app.MemoryMB = 512;
        app.HealthStatus = "Healthy";
        editor.ActiveSection = "Monitoring";
        Assert.False(editor.HasUnsavedChanges);
        Assert.False(editor.ShowSaveAction);
        app.Name = "Renamed";
        Assert.True(editor.ShowSaveAction);
        app.Name = "A";
        Assert.False(editor.ShowSaveAction);
    }

    [Fact]
    public async Task SaveTracksGroupsLayoutsScopesAndCollectionResets()
    {
        using var fixture = await MainViewModelAuditTests.CreateHarnessAsync(new ConfigRoot
        {
            Apps = [new() { Name = "A", Path = "a.exe" }, new() { Name = "B", Path = "b.exe" }],
            Groups = [new() { Key = "test", Names = new() { ["en"] = "Test" } }]
        });
        using var editor = CreateEditor(fixture, AccessLevel.Admin);
        var entry = editor.Groups[0].NameEntries.Single(x => x.LanguageCode == "en");
        entry.Name = "Changed";
        Assert.True(editor.HasUnsavedChanges);
        entry.Name = "Test";
        Assert.False(editor.HasUnsavedChanges);
        var layout = new ScreenLayout { Name = "Test layout" };
        editor.Layouts.Add(layout);
        Assert.True(editor.HasUnsavedChanges);
        editor.Layouts.Remove(layout);
        Assert.False(editor.HasUnsavedChanges);
        editor.Settings.OperatorScope.SetAllowedAppNames(["A"]);
        Assert.True(editor.HasUnsavedChanges);
        editor.Settings.OperatorScope.SetAllowedAppNames([]);
        Assert.True(editor.HasUnsavedChanges); // Empty restrictions no longer imply all apps.
        editor.Settings.OperatorScope.Restricted = false;
        Assert.False(editor.HasUnsavedChanges);
        editor.Apps.Move(0, 1);
        Assert.True(editor.HasUnsavedChanges);
        editor.Apps.Move(0, 1);
        Assert.False(editor.HasUnsavedChanges);
        var detached = editor.Apps[0];
        editor.Apps.Clear();
        Assert.True(editor.HasUnsavedChanges);
        await editor.SaveCommand.ExecuteAsync(null);
        detached.Name = "No longer tracked";
        Assert.False(editor.HasUnsavedChanges);
        editor.Apps.Add(detached);
        Assert.True(editor.HasUnsavedChanges);
    }

    [Fact]
    public async Task InvalidInputShowsDisabledSaveEvenIfModelIsUnchanged()
    {
        using var fixture = await MainViewModelAuditTests.CreateHarnessAsync();
        using var editor = CreateEditor(fixture, AccessLevel.Admin);
        editor.HasInputErrors = true;
        Assert.True(editor.ShowSaveAction);
        Assert.False(editor.SaveCommand.CanExecute(null));
        Assert.Equal("UI_FieldErrors", editor.SaveStatus);
        editor.HasInputErrors = false;
        Assert.False(editor.ShowSaveAction);
    }

    [Fact]
    public async Task ChangesDuringSaveRemainDirtyAndAreSavedOnNextAttempt()
    {
        using var fixture = await MainViewModelAuditTests.CreateHarnessAsync();
        using var editor = CreateEditor(fixture, AccessLevel.Admin);
        var resume = new TaskCompletionSource();
        fixture.ConfigService.BeforeSave = () => resume.Task;
        editor.Settings.MenuWidth = 420;
        var save = editor.SaveCommand.ExecuteAsync(null);
        editor.Settings.MenuWidth = 450;
        resume.SetResult();
        await save;
        Assert.Equal(420, fixture.ConfigService.LastSavedConfig!.Settings.MenuWidth);
        Assert.True(editor.ShowSaveAction);
        await editor.SaveCommand.ExecuteAsync(null);
        Assert.Equal(450, fixture.ConfigService.LastSavedConfig.Settings.MenuWidth);
        Assert.False(editor.ShowSaveAction);
    }

    [Fact]
    public async Task FailedSaveKeepsEditsAndDoesNotReportSuccess()
    {
        using var fixture = await MainViewModelAuditTests.CreateHarnessAsync();
        fixture.ViewModel.CurrentAccessLevel = AccessLevel.Admin;
        using var editor = CreateEditor(fixture, AccessLevel.Admin);
        fixture.ConfigService.FailSave = true;
        editor.Settings.MenuWidth = 420;
        await editor.SaveCommand.ExecuteAsync(null);
        Assert.False(editor.LastSaveSucceeded);
        Assert.Null(fixture.ConfigService.LastSavedConfig);
        Assert.Equal(420, editor.Settings.MenuWidth);
        Assert.Equal("UI_SaveFailed", editor.SaveStatus);
        Assert.True(editor.ShowSaveAction);
        Assert.True(editor.SaveCommand.CanExecute(null));
    }

    [Fact]
    public async Task OperatorCannotEditReorderSaveOrChangeColours()
    {
        using var fixture = await MainViewModelAuditTests.CreateHarnessAsync(new ConfigRoot { Apps = [new() { Name = "A", Path = "a.exe" }] });
        fixture.ViewModel.CurrentAccessLevel = AccessLevel.Operator;
        using var editor = CreateEditor(fixture, AccessLevel.Operator);
        var before = editor.SelectedApp!.BgColor;
        Assert.False(editor.AddAppCommand.CanExecute(null));
        Assert.False(editor.MoveAppCommand.CanExecute("1"));
        Assert.False(editor.SaveCommand.CanExecute(null));
        editor.SetBackgroundColorCommand.Execute("#112233");
        Assert.Equal(before, editor.SelectedApp.BgColor);
    }

    [Fact]
    public async Task UndoRestoresConfigurationWithoutLosingLiveAppState()
    {
        using var fixture = await MainViewModelAuditTests.CreateHarnessAsync(new ConfigRoot
        { Apps = [new() { Name = "A", Path = "a.exe" }], Settings = new AppSettings { OperatorScope = new() { AllowedAppNames = ["A"] } } });
        fixture.ViewModel.CurrentAccessLevel = AccessLevel.Admin;
        using var editor = CreateEditor(fixture, AccessLevel.Admin);
        var liveApp = editor.Apps[0];
        liveApp.IsRunning = true;
        liveApp.MemoryMB = 128;
        liveApp.Name = "Renamed";
        editor.Settings.MenuWidth += 20;
        Assert.True(editor.CanUndo);
        await editor.UndoCommand.ExecuteAsync(null);
        Assert.Same(liveApp, editor.Apps[0]);
        Assert.True(liveApp.IsRunning);
        Assert.Equal(128, liveApp.MemoryMB);
        Assert.Equal("A", liveApp.Name);
        Assert.Equal(new[] { "A" }, editor.Settings.OperatorScope.AllowedAppNames);
        Assert.False(editor.HasUnsavedChanges);
        Assert.False(editor.CanUndo);
        Assert.Null(fixture.ConfigService.LastSavedConfig);
    }

    [Fact]
    public async Task UndoTracksCollectionEditsAndIsBlockedByInvalidInput()
    {
        using var fixture = await MainViewModelAuditTests.CreateHarnessAsync();
        fixture.ViewModel.CurrentAccessLevel = AccessLevel.Admin;
        using var editor = CreateEditor(fixture, AccessLevel.Admin);
        editor.Layouts.Add(new ScreenLayout { Name = "Layout" });
        editor.HasInputErrors = true;
        Assert.False(editor.UndoCommand.CanExecute(null));
        editor.HasInputErrors = false;
        await editor.UndoCommand.ExecuteAsync(null);
        Assert.Empty(editor.Layouts);
        editor.Settings.MenuWidth += 10;
        await editor.SaveCommand.ExecuteAsync(null);
        Assert.False(editor.CanUndo);
    }

    [Fact]
    public async Task SettingsSearchSupportsBothLanguagesAndPermissionFiltering()
    {
        using var fixture = await MainViewModelAuditTests.CreateHarnessAsync(new ConfigRoot { Apps = [new() { Name = "A", Path = "a.exe" }] });
        using var admin = CreateEditor(fixture, AccessLevel.Admin);
        admin.SettingsSearch = "bildschirm";
        var result = Assert.Single(admin.SearchResults);
        Assert.Equal("AppMonitorSelection", result.PropertyPath);
        admin.OpenSearchResultCommand.Execute(result);
        Assert.Equal("Applications", admin.ActiveSection);
        Assert.False(admin.HasSettingsSearch);
        admin.SettingsSearch = "firefox";
        Assert.Contains(admin.SearchResults, result => result.Section == "Browsers");
        using var op = CreateEditor(fixture, AccessLevel.Operator);
        op.SettingsSearch = "pin";
        Assert.Empty(op.SearchResults);
        op.SettingsSearch = "monitor";
        Assert.NotEmpty(op.SearchResults);
        Assert.False(op.UndoCommand.CanExecute(null));
    }

    private static SettingsViewModel CreateEditor(MainViewModelAuditTests.TestHarness fixture, AccessLevel role) => new(
        fixture.ViewModel, fixture.ConfigService, new SecurityService(), fixture.NotificationService,
        new ScreenLayoutService(NullLogger<ScreenLayoutService>.Instance, fixture.ProcessHelper),
        new CrashHistoryService(NullLogger<CrashHistoryService>.Instance),
        new SessionTimeoutService(NullLogger<SessionTimeoutService>.Instance), role);
}
