namespace AppSwitcher.Tests;

public class AppDependencyPlannerTests
{
    [Fact]
    public void BuildReverseShutdownOrder_Stops_Dependents_Before_Dependencies()
    {
        var database = new AppItem { Name = "Database", Path = "db.exe" };
        var api = new AppItem { Name = "Api", Path = "api.exe", DependsOn = "Database" };
        var ui = new AppItem { Name = "Ui", Path = "ui.exe", DependsOn = "Api" };

        var order = AppDependencyPlanner.BuildReverseShutdownOrder([database, api, ui]);

        Assert.Equal(["Ui", "Api", "Database"], order.Select(app => app.Name).ToArray());
    }

    [Fact]
    public void FindByName_Matches_Case_Insensitive_Names()
    {
        var apps = new List<AppItem>
        {
            new() { Name = "Launcher", Path = "launcher.exe" }
        };

        var match = AppDependencyPlanner.FindByName(apps, "launcher");

        Assert.NotNull(match);
        Assert.Equal("Launcher", match!.Name);
    }

    [Fact]
    public void BuildLaunchPlan_Includes_Dependencies_Before_Requested_App()
    {
        var database = new AppItem { Name = "Database", Path = "db.exe" };
        var api = new AppItem { Name = "Api", Path = "api.exe", DependsOn = "Database" };
        var ui = new AppItem { Name = "Ui", Path = "ui.exe", DependsOn = "Api" };

        var plan = AppDependencyPlanner.BuildLaunchPlan([ui], [database, api, ui]);

        Assert.Equal(["Database", "Api", "Ui"], plan.Select(app => app.Name).ToArray());
    }

    [Fact]
    public void BuildLaunchPlan_Deduplicates_Shared_Dependencies()
    {
        var database = new AppItem { Name = "Database", Path = "db.exe" };
        var api = new AppItem { Name = "Api", Path = "api.exe", DependsOn = "Database" };
        var worker = new AppItem { Name = "Worker", Path = "worker.exe", DependsOn = "Database" };

        var plan = AppDependencyPlanner.BuildLaunchPlan([api, worker], [database, api, worker]);

        Assert.Equal(["Database", "Api", "Worker"], plan.Select(app => app.Name).ToArray());
    }
}
