namespace AppSwitcher.Tests;

public class WorkspaceRepairTests
{
    private static readonly AppItem Database = new() { Name = "Database", Path = "db.exe", Group = "production" };
    private static readonly AppItem Viewer = new() { Name = "Viewer", Path = "viewer.exe" };
    private static readonly AppItem[] Apps = [Database, Viewer];
    private static readonly GroupDefinition[] Groups = [new() { Key = "production" }, new() { Key = "empty" }];

    private static IReadOnlyList<WorkspaceChoice> Choices => WorkspaceEditor.Choices(Apps, Groups);

    private static ScreenLayout BrokenLayout(string deletedId) => new()
    {
        Name = "Shift",
        AppIds = [Database.AppId, deletedId],
        Positions = [new() { AppId = deletedId, AppName = "Old viewer", Left = 10, Width = 500 }, new() { AppName = "Database" }],
        GroupKeys = ["production", "renamed-group"],
    };

    [Fact]
    public void Describe_Flags_Deleted_Apps_And_Groups_Without_Apps()
    {
        var deletedId = Guid.NewGuid().ToString();
        var members = WorkspaceEditor.Describe(BrokenLayout(deletedId), Apps, Choices);

        Assert.Equal(["Database", "Old viewer", "production", "renamed-group"], members.Select(m => m.DisplayName));
        Assert.Equal([false, true, false, true], members.Select(m => m.IsMissing));
        Assert.True(members.Single(m => m.Reference == deletedId).HasPosition);
        // A legacy name-only position is folded into the app's AppId entry, and removing the app removes it too.
        var database = members.Single(m => m.ResolvedKey == Database.AppId);
        Assert.True(database.HasPosition);
        WorkspaceEditor.Remove(database);
        Assert.DoesNotContain(database.Layout.Positions, p => p.AppName == "Database");
        Assert.True(WorkspaceEditor.Describe(new ScreenLayout { GroupKeys = ["empty"] }, Apps, Choices).Single().IsMissing);
    }

    [Fact]
    public void Repairing_Every_Missing_Member_Makes_The_Workspace_Startable_And_Keeps_Positions()
    {
        var deletedId = Guid.NewGuid().ToString();
        var layout = BrokenLayout(deletedId);
        Assert.Throws<InvalidOperationException>(() => WorkspacePlanner.Resolve(layout, Apps));

        var card = WorkspaceEditor.Card(layout, Apps, Choices);
        var app = card.Members.Single(m => m.Reference == deletedId);
        WorkspaceEditor.Replace(app, app.ReplacementChoices.Single(c => c.Key == Viewer.AppId));
        WorkspaceEditor.Remove(card.Members.Single(m => m.Reference == "renamed-group"));

        Assert.Equal([Database.AppId, Viewer.AppId], layout.AppIds);
        Assert.Equal(["production"], layout.GroupKeys);
        var moved = layout.Positions.Single(p => p.AppId == Viewer.AppId);
        Assert.Equal(("Viewer", 10d, 500d), (moved.AppName, moved.Left, moved.Width));
        Assert.Equal([Database, Viewer], WorkspacePlanner.Resolve(layout, Apps));
        Assert.Equal(0, WorkspaceEditor.Card(layout, Apps, Choices).MissingCount);
    }

    [Fact]
    public void Replace_Does_Not_Duplicate_A_Position_The_Replacement_Already_Has()
    {
        var layout = new ScreenLayout
        {
            AppIds = ["gone", Viewer.AppId],
            Positions = [new() { AppId = "gone", Left = 1 }, new() { AppId = Viewer.AppId, Left = 2 }],
        };
        var member = WorkspaceEditor.Describe(layout, Apps, Choices).First();
        WorkspaceEditor.Replace(member, new(WorkspaceMemberKind.App, Viewer.AppId, Viewer.Name));

        Assert.Equal([Viewer.AppId], layout.AppIds);
        Assert.Equal(2d, Assert.Single(layout.Positions).Left);
        Assert.Throws<ArgumentException>(() => WorkspaceEditor.Replace(
            WorkspaceEditor.Describe(layout, Apps, Choices).Single(), new(WorkspaceMemberKind.Group, "production", "production")));
    }

    [Fact]
    public void Add_Offers_Only_Members_Not_Yet_Included_And_Ignores_Duplicates()
    {
        var layout = new ScreenLayout { AppIds = [Database.AppId], GroupKeys = ["production"] };
        var card = WorkspaceEditor.Card(layout, Apps, Choices);
        Assert.Equal([Viewer.AppId, "empty"], card.AdditionChoices.Select(c => c.Key));

        WorkspaceEditor.Add(layout, card.AdditionChoices[0]);
        WorkspaceEditor.Add(layout, card.AdditionChoices[0]);
        WorkspaceEditor.Add(layout, new(WorkspaceMemberKind.Group, "PRODUCTION", "production"));
        Assert.Equal([Database.AppId, Viewer.AppId], layout.AppIds);
        Assert.Equal(["production"], layout.GroupKeys);
    }
}
