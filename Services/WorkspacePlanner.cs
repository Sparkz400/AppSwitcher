namespace AppSwitcher.Services;

internal static class WorkspacePlanner
{
    internal static IReadOnlyList<AppItem> Resolve(ScreenLayout layout, IReadOnlyList<AppItem> apps)
    {
        var selected = new HashSet<AppItem>();
        foreach (var id in layout.AppIds)
            selected.Add(apps.SingleOrDefault(a => a.AppId == id)
                ?? throw new InvalidOperationException(LocalizationService.T("Workspace_Missing")));
        foreach (var position in layout.Positions)
            selected.Add(apps.SingleOrDefault(a => !string.IsNullOrEmpty(position.AppId)
                ? a.AppId == position.AppId : string.Equals(a.Name, position.AppName, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException(LocalizationService.T("Workspace_Missing")));
        foreach (var group in layout.GroupKeys)
        {
            var members = apps.Where(a => string.Equals(a.Group, group, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (members.Length == 0) throw new InvalidOperationException(LocalizationService.T("Workspace_Missing"));
            selected.UnionWith(members);
        }
        if (selected.Count == 0) throw new InvalidOperationException(LocalizationService.T("Workspace_Missing"));
        return AppDependencyPlanner.BuildLaunchPlan(apps.Where(selected.Contains).ToArray(), apps);
    }
}

public enum WorkspaceMemberKind { App, Group }

/// <summary>An app or group that a workspace can include.</summary>
public sealed record WorkspaceChoice(WorkspaceMemberKind Kind, string Key, string Name)
{
    public string Label => Kind == WorkspaceMemberKind.Group ? LocalizationService.T("Workspace_GroupLabel", Name) : Name;
}

/// <summary>One saved workspace reference: an AppId (or legacy app name from a saved position) or a group key.</summary>
public sealed class WorkspaceMember
{
    public required ScreenLayout Layout { get; init; }
    public required WorkspaceMemberKind Kind { get; init; }
    public required string Reference { get; init; }
    public required string DisplayName { get; init; }
    public string? ResolvedKey { get; init; }
    public bool IsMissing => ResolvedKey == null;
    public bool HasPosition { get; init; }
    public IReadOnlyList<WorkspaceChoice> ReplacementChoices { get; init; } = [];
    public WorkspaceChoice? Replacement { get; set; }
    public string Detail => LocalizationService.T(IsMissing ? "Workspace_MemberMissing" : Kind == WorkspaceMemberKind.Group ? "Workspace_KindGroup"
        : HasPosition ? "Workspace_KindAppPosition" : "Workspace_KindApp");
    public string RemoveName => LocalizationService.T("Workspace_RemoveMemberName", DisplayName);
    public string ReplaceName => LocalizationService.T("Workspace_ReplaceMemberName", DisplayName);
}

/// <summary>A workspace with its resolved members, as shown in settings.</summary>
public sealed class WorkspaceCard
{
    public required ScreenLayout Layout { get; init; }
    public required IReadOnlyList<WorkspaceMember> Members { get; init; }
    public IReadOnlyList<WorkspaceChoice> AdditionChoices { get; init; } = [];
    public WorkspaceChoice? Addition { get; set; }
    public int MissingCount => Members.Count(member => member.IsMissing);
    public string Status => Members.Count == 0 ? LocalizationService.T("Workspace_Empty")
        : MissingCount > 0 ? LocalizationService.T("Workspace_NeedsRepair", MissingCount) : "";
    public string AddName => LocalizationService.T("Workspace_AddMemberName", Layout.Name);
}

/// <summary>Member inspection and per-member repair, so a workspace need not be recaptured.</summary>
internal static class WorkspaceEditor
{
    internal static IReadOnlyList<WorkspaceChoice> Choices(IReadOnlyList<AppItem> apps, IEnumerable<GroupDefinition> groups) =>
        [.. apps.Select(app => new WorkspaceChoice(WorkspaceMemberKind.App, app.AppId, app.Name)),
         .. groups.Where(group => !string.IsNullOrWhiteSpace(group.Key))
             .Select(group => new WorkspaceChoice(WorkspaceMemberKind.Group, group.Key, string.IsNullOrEmpty(group.CurrentDisplayName) ? group.Key : group.CurrentDisplayName))];

    // Mirrors WorkspacePlanner.Resolve, so "missing" here means "Start workspace would fail".
    internal static IReadOnlyList<WorkspaceMember> Describe(ScreenLayout layout, IReadOnlyList<AppItem> apps, IReadOnlyList<WorkspaceChoice> choices)
    {
        var members = new List<WorkspaceMember>();
        var appRefs = layout.AppIds.Concat(layout.Positions.Select(Reference))
            .Where(reference => !string.IsNullOrEmpty(reference)).Distinct(StringComparer.OrdinalIgnoreCase);
        var listed = new HashSet<string>();
        foreach (var reference in appRefs)
        {
            var position = layout.Positions.FirstOrDefault(p => Matches(p, reference));
            var app = apps.FirstOrDefault(a => a.AppId == reference)
                ?? (position != null && string.IsNullOrEmpty(position.AppId)
                    ? apps.FirstOrDefault(a => string.Equals(a.Name, reference, StringComparison.OrdinalIgnoreCase)) : null);
            if (app != null && !listed.Add(app.AppId)) continue; // legacy name-only position of an app already listed
            position ??= app == null ? null : layout.Positions.FirstOrDefault(p => Covers(p, app.AppId, app.Name));
            members.Add(new()
            {
                Layout = layout, Kind = WorkspaceMemberKind.App, Reference = reference, ResolvedKey = app?.AppId,
                DisplayName = app?.Name ?? (string.IsNullOrEmpty(position?.AppName) ? reference : position.AppName),
                HasPosition = position != null,
                ReplacementChoices = [.. choices.Where(choice => choice.Kind == WorkspaceMemberKind.App)],
            });
        }
        foreach (var key in layout.GroupKeys.Distinct(StringComparer.OrdinalIgnoreCase))
            members.Add(new()
            {
                Layout = layout, Kind = WorkspaceMemberKind.Group, Reference = key,
                ResolvedKey = apps.Any(app => string.Equals(app.Group, key, StringComparison.OrdinalIgnoreCase)) ? key : null,
                DisplayName = choices.FirstOrDefault(c => c.Kind == WorkspaceMemberKind.Group && c.Key.Equals(key, StringComparison.OrdinalIgnoreCase))?.Name ?? key,
                ReplacementChoices = [.. choices.Where(choice => choice.Kind == WorkspaceMemberKind.Group)],
            });
        return members;
    }

    internal static WorkspaceCard Card(ScreenLayout layout, IReadOnlyList<AppItem> apps, IReadOnlyList<WorkspaceChoice> choices)
    {
        var members = Describe(layout, apps, choices);
        return new()
        {
            Layout = layout, Members = members,
            AdditionChoices = [.. choices.Where(choice => !members.Any(member => member.Kind == choice.Kind
                && string.Equals(member.ResolvedKey, choice.Key, StringComparison.OrdinalIgnoreCase)))],
        };
    }

    /// <summary>Localized summary of what a workspace contains, used to explain delete and replace effects.</summary>
    internal static string Summary(ScreenLayout layout, IReadOnlyList<AppItem> apps, IReadOnlyList<WorkspaceChoice> choices)
    {
        var members = Describe(layout, apps, choices);
        string Names(WorkspaceMemberKind kind)
        {
            var names = members.Where(m => m.Kind == kind)
                .Select(m => m.IsMissing ? LocalizationService.T("Workspace_MissingName", m.DisplayName) : m.DisplayName).ToArray();
            return names.Length == 0 ? "—" : string.Join(", ", names);
        }
        return LocalizationService.T("Workspace_Summary", Names(WorkspaceMemberKind.App), Names(WorkspaceMemberKind.Group), layout.Positions.Count);
    }

    internal static void Remove(WorkspaceMember member)
    {
        var layout = member.Layout;
        if (member.Kind == WorkspaceMemberKind.Group)
        {
            layout.GroupKeys.RemoveAll(key => key.Equals(member.Reference, StringComparison.OrdinalIgnoreCase));
            return;
        }
        layout.AppIds.RemoveAll(id => id.Equals(member.Reference, StringComparison.OrdinalIgnoreCase));
        layout.Positions.RemoveAll(position => Belongs(position, member));
    }

    /// <summary>Points a member at another app or group. A saved window position follows the replacement app.</summary>
    internal static void Replace(WorkspaceMember member, WorkspaceChoice replacement)
    {
        if (replacement.Kind != member.Kind) throw new ArgumentException("Replacement kind must match the member kind.", nameof(replacement));
        var layout = member.Layout;
        var moved = layout.Positions.FirstOrDefault(position => Belongs(position, member));
        Remove(member);
        Add(layout, replacement);
        // Keep the old window position only if the replacement has none of its own.
        if (member.Kind == WorkspaceMemberKind.App && moved != null && !layout.Positions.Any(position => Covers(position, replacement.Key, replacement.Name)))
        {
            moved.AppId = replacement.Key;
            moved.AppName = replacement.Name;
            layout.Positions.Add(moved);
        }
    }

    internal static void Add(ScreenLayout layout, WorkspaceChoice choice)
    {
        var list = choice.Kind == WorkspaceMemberKind.Group ? layout.GroupKeys : layout.AppIds;
        if (!list.Contains(choice.Key, StringComparer.OrdinalIgnoreCase)) list.Add(choice.Key);
    }

    private static string Reference(WindowPosition position) => string.IsNullOrEmpty(position.AppId) ? position.AppName : position.AppId;

    private static bool Matches(WindowPosition position, string reference) => Reference(position).Equals(reference, StringComparison.OrdinalIgnoreCase);

    private static bool Covers(WindowPosition position, string appId, string appName) => Matches(position, appId)
        || string.IsNullOrEmpty(position.AppId) && position.AppName.Equals(appName, StringComparison.OrdinalIgnoreCase);

    private static bool Belongs(WindowPosition position, WorkspaceMember member) => member.ResolvedKey == null
        ? Matches(position, member.Reference) : Covers(position, member.ResolvedKey, member.DisplayName);
}
