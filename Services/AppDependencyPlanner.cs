namespace AppSwitcher.Services;

internal static class AppDependencyPlanner
{
    public static List<AppItem> BuildLaunchPlan(IEnumerable<AppItem> requestedApps, IEnumerable<AppItem> allApps)
    {
        var requestedList = requestedApps.ToList();
        var appsByName = allApps
            .Where(app => !string.IsNullOrWhiteSpace(app.Name))
            .GroupBy(app => app.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var launchPlan = new List<AppItem>();

        foreach (var requestedApp in requestedList)
            Visit(requestedApp);

        return launchPlan;

        void Visit(AppItem app)
        {
            var name = app.Name?.Trim() ?? string.Empty;
            if (!string.IsNullOrEmpty(name))
            {
                if (visited.Contains(name))
                    return;

                if (!visiting.Add(name))
                    throw new InvalidOperationException($"Circular dependency detected while planning launch for '{name}'.");
            }

            if (!string.IsNullOrWhiteSpace(app.DependsOn))
            {
                var dependencyName = app.DependsOn.Trim();
                if (!appsByName.TryGetValue(dependencyName, out var dependency))
                {
                    throw new InvalidOperationException(
                        $"Dependency '{dependencyName}' is not configured for '{app.Name}'.");
                }

                Visit(dependency);
            }

            launchPlan.Add(app);

            if (!string.IsNullOrEmpty(name))
            {
                visiting.Remove(name);
                visited.Add(name);
            }
        }
    }

    public static List<AppItem> BuildReverseShutdownOrder(IEnumerable<AppItem> apps)
    {
        var appList = apps.ToList();
        var appsByName = appList
            .Where(app => !string.IsNullOrWhiteSpace(app.Name))
            .GroupBy(app => app.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var order = new List<AppItem>();

        foreach (var app in appList)
            Visit(app);

        return order;

        void Visit(AppItem app)
        {
            var name = app.Name?.Trim() ?? string.Empty;
            if (!string.IsNullOrEmpty(name) && !visited.Add(name))
                return;

            if (!string.IsNullOrWhiteSpace(app.DependsOn)
                && appsByName.TryGetValue(app.DependsOn.Trim(), out var dependency))
            {
                Visit(dependency);
            }

            order.Insert(0, app);
        }
    }

    public static AppItem? FindByName(IEnumerable<AppItem> apps, string? appName)
    {
        if (string.IsNullOrWhiteSpace(appName))
            return null;

        return apps.FirstOrDefault(app =>
            !string.IsNullOrWhiteSpace(app.Name)
            && app.Name.Trim().Equals(appName.Trim(), StringComparison.OrdinalIgnoreCase));
    }
}
