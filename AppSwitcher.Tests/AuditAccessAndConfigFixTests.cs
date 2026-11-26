using Microsoft.Extensions.Logging.Abstractions;

namespace AppSwitcher.Tests;

public class AuditAccessAndConfigFixTests
{
    private static readonly ConfigDiffService Diff = new(NullLogger<ConfigDiffService>.Instance);

    [Theory]
    [InlineData("admin")]
    [InlineData("operator")]
    [InlineData("kiosk")]
    [InlineData("scope")]
    [InlineData("hook")]
    [InlineData("added-app-hook")]
    public void Diff_Flags_Changes_That_A_File_Edit_Must_Not_Make(string change)
    {
        var current = new ConfigRoot { Apps = [new() { Name = "A", Path = "a.exe" }] };
        var incoming = new ConfigRoot { Apps = [new() { Name = "A", Path = "a.exe" }] };
        current.Settings.AdminPasswordHash = incoming.Settings.AdminPasswordHash = new SecurityService().HashPassword("1234");
        switch (change)
        {
            case "admin": incoming.Settings.AdminPasswordHash = ""; break;
            case "operator": incoming.Settings.OperatorPasswordHash = new SecurityService().HashPassword("5678"); break;
            case "kiosk": incoming.Settings.KioskMode = true; break;
            case "scope": incoming.Settings.OperatorScope.Restricted = !current.Settings.OperatorScope.Restricted; break;
            case "hook": incoming.Apps[0].PreLaunchHookPath = "cmd.exe"; break;
            case "added-app-hook": incoming.Apps.Add(new AppItem { Name = "B", Path = "b.exe", PostStopHookPath = "cmd.exe" }); break;
        }

        Assert.True(Diff.Compute(current, incoming).TouchesProtectedSettings);
    }

    [Fact]
    public void Diff_Allows_Ordinary_File_Edits()
    {
        var current = new ConfigRoot { Apps = [new() { Name = "A", Path = "a.exe" }] };
        var incoming = new ConfigRoot { Apps = [new() { Name = "A", Path = "a2.exe", Args = "--x" }, new() { Name = "B", Path = "b.exe" }] };
        incoming.Settings.Theme = "Light";

        var diff = Diff.Compute(current, incoming);

        Assert.True(diff.HasChanges);
        Assert.False(diff.TouchesProtectedSettings);
    }

    [Fact]
    public async Task ApplyConfig_Keeps_Live_App_Object_So_Running_State_Survives_Reload()
    {
        using var fixture = await MainViewModelAuditTests.CreateHarnessAsync(new ConfigRoot { Apps = [new() { Name = "A", Path = "a.exe" }] });
        var live = fixture.ViewModel.Apps.Single();
        live.IsRunning = true;
        live.State = AppState.Running;
        var incoming = JsonSerializer.Deserialize<ConfigRoot>(fixture.ViewModel.EditorSnapshot, AppJsonOptions.Default)!;
        incoming.Apps[0].Args = "--changed";

        await fixture.ViewModel.ApplyConfigAsync(incoming);

        Assert.Same(live, fixture.ViewModel.Apps.Single());
        Assert.Equal("--changed", live.Args);
        Assert.True(live.IsRunning);
        Assert.Equal(AppState.Running, live.State);
    }

    [Fact]
    public async Task New_Pin_Matching_The_Other_Role_Is_Rejected()
    {
        using var fixture = await MainViewModelAuditTests.CreateHarnessAsync();
        fixture.ViewModel.CurrentAccessLevel = AccessLevel.Admin;
        fixture.ViewModel.Settings.OperatorPasswordHash = new SecurityService().HashPassword("2468");
        using var editor = new SettingsViewModel(fixture.ViewModel, fixture.ConfigService, new SecurityService(), fixture.NotificationService,
            new ScreenLayoutService(NullLogger<ScreenLayoutService>.Instance, fixture.ProcessHelper),
            new SessionTimeoutService(NullLogger<SessionTimeoutService>.Instance));

        Assert.True(editor.RejectPinMatching("2468", fixture.ViewModel.Settings.OperatorPasswordHash));
        Assert.False(editor.RejectPinMatching("1357", fixture.ViewModel.Settings.OperatorPasswordHash));
        Assert.False(editor.RejectPinMatching("2468", ""));
    }

    [Fact]
    public async Task Sign_Out_Ends_The_Session_And_Is_Audited()
    {
        using var fixture = await MainViewModelAuditTests.CreateHarnessAsync();
        fixture.ViewModel.CurrentAccessLevel = AccessLevel.Admin;

        fixture.ViewModel.SignOutCommand.Execute(null);

        Assert.Equal(AccessLevel.None, fixture.ViewModel.CurrentAccessLevel);
        Assert.Contains(fixture.AuditLog.GetRecentEntries(), entry => entry.Action == "SignOut" && entry.Outcome == "Success");
    }
}
