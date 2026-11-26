using System.Globalization;
using AppSwitcher.Converters;

namespace AppSwitcher.Tests;

public class AuditFollowUpTests
{
    private static readonly SecurityService Security = new();

    private static string? Never(bool _) => throw new InvalidOperationException("No PIN prompt expected.");

    [Fact]
    public void First_Sign_In_Sets_Admin_Pin_Unless_It_Equals_The_Operator_Pin()
    {
        var settings = new AppSettings { OperatorPasswordHash = Security.HashPassword("2468") };
        var signIn = new PinSignIn(new SecurityService());

        var rejected = signIn.SignIn(settings, AccessLevel.Operator, setMode => setMode ? "2468" : null);
        Assert.Equal(AccessLevel.None, rejected.Level);
        Assert.Equal(NotificationType.Error, rejected.MessageType);
        Assert.Equal("", settings.AdminPasswordHash);

        var accepted = signIn.SignIn(settings, AccessLevel.Operator, setMode => setMode ? "1357" : null);
        Assert.Equal(AccessLevel.Admin, accepted.Level);
        Assert.True(accepted.HashesChanged);
        Assert.True(Security.VerifyPassword("1357", settings.AdminPasswordHash));
    }

    [Fact]
    public void Wrong_Pin_And_Insufficient_Role_Are_Denied_And_Cancel_Is_Silent()
    {
        var settings = new AppSettings { AdminPasswordHash = Security.HashPassword("1357"), OperatorPasswordHash = Security.HashPassword("2468") };
        var signIn = new PinSignIn(new SecurityService());

        Assert.True(signIn.SignIn(settings, AccessLevel.Operator, _ => "0000").Denied);
        Assert.True(signIn.SignIn(settings, AccessLevel.Admin, _ => "2468").Denied);
        var cancelled = signIn.SignIn(settings, AccessLevel.Operator, _ => null);
        Assert.Equal(AccessLevel.None, cancelled.Level);
        Assert.False(cancelled.Denied);
        Assert.Null(cancelled.Message);
    }

    [Fact]
    public void Legacy_Operator_Pin_Is_Upgraded_And_Reported_For_Saving()
    {
        var legacy = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes("AppSwitcher_SCADA_2024" + "2468")));
        var settings = new AppSettings { AdminPasswordHash = Security.HashPassword("1357"), OperatorPasswordHash = legacy };

        var outcome = new PinSignIn(new SecurityService()).SignIn(settings, AccessLevel.Operator, _ => "2468");

        Assert.Equal(AccessLevel.Operator, outcome.Level);
        Assert.True(outcome.HashesChanged);
        Assert.False(Security.NeedsRehash(settings.OperatorPasswordHash));
        Assert.True(Security.VerifyPassword("2468", settings.OperatorPasswordHash));
    }

    [Fact]
    public void Lockout_Refuses_Without_Prompting()
    {
        var settings = new AppSettings { AdminPasswordHash = Security.HashPassword("1357") };
        var security = new SecurityService();
        var signIn = new PinSignIn(security);
        for (var attempt = 0; attempt < 3; attempt++) signIn.SignIn(settings, AccessLevel.Operator, _ => "0000");

        var outcome = signIn.SignIn(settings, AccessLevel.Operator, Never);

        Assert.Equal(AccessLevel.None, outcome.Level);
        Assert.False(outcome.Denied);
    }

    [Fact]
    public async Task Gates_Serialize_One_Key_Without_Blocking_Others_And_Forget_Idle_Keys()
    {
        var gates = new BoundedOperationGates();
        var first = await gates.EnterAsync("a", CancellationToken.None);

        var sameKey = gates.EnterAsync("a", CancellationToken.None);
        var otherKey = gates.EnterAsync("b", CancellationToken.None);
        Assert.True(otherKey.IsCompleted);
        Assert.False(sameKey.IsCompleted);

        first.Release();
        first.Release(); // A second release is ignored, not a second slot.
        (await sameKey).Release();
        (await otherKey).Release();
        Assert.Equal(0, gates.ActiveKeys);
    }

    [Fact]
    public async Task Cancelled_Wait_Does_Not_Leak_A_Gate()
    {
        var gates = new BoundedOperationGates();
        var held = await gates.EnterAsync("a", CancellationToken.None);
        using var cancel = new CancellationTokenSource();
        var waiting = gates.EnterAsync("a", cancel.Token);

        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        held.Release();

        Assert.Equal(0, gates.ActiveKeys);
    }

    [Fact]
    public async Task Undo_That_Switches_Kiosk_Is_Audited()
    {
        using var fixture = await MainViewModelAuditTests.CreateHarnessAsync();
        fixture.ViewModel.CurrentAccessLevel = AccessLevel.Admin;
        var snapshot = JsonSerializer.Deserialize<ConfigRoot>(fixture.ViewModel.EditorSnapshot, AppJsonOptions.Default)!;
        snapshot.Settings.KioskMode = true;

        await fixture.ViewModel.RestoreEditorSnapshotAsync(JsonSerializer.Serialize(snapshot, AppJsonOptions.Default));
        try
        {
            Assert.Contains(fixture.AuditLog.GetRecentEntries(), entry => entry.Action == "KioskToggled" && entry.Target == "Enabled");
        }
        finally
        {
            fixture.ViewModel.Settings.KioskMode = false;
            await fixture.ViewModel.RestoreEditorSnapshotAsync(fixture.ViewModel.PersistedEditorSnapshot);
        }
    }

    [Fact]
    public void Icon_Lookup_Misses_Are_Cached_Instead_Of_Hitting_The_Disk_Again()
    {
        var path = Path.Combine(Path.GetTempPath(), $"appswitcher-icon-{Guid.NewGuid():N}.exe");
        var converter = new PathToIconConverter();
        try
        {
            Assert.Null(converter.Convert([path, ""], typeof(object), null!, CultureInfo.InvariantCulture));
            File.WriteAllBytes(path, []);
            // Within the retry window the cached miss is returned without a new disk lookup.
            Assert.Null(converter.Convert([path, ""], typeof(object), null!, CultureInfo.InvariantCulture));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
