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
