using Microsoft.Extensions.Logging.Abstractions;

namespace AppSwitcher.Tests;

public class OperatorScopeTests
{
    [Fact]
    public async Task ImportAsync_Migrates_V6_Config_To_V7_With_Default_Operator_Scope()
    {
        var validationService = new ConfigValidationService(NullLogger<ConfigValidationService>.Instance);
        using var service = new ConfigService(NullLogger<ConfigService>.Instance, validationService);
        var tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");

        try
        {
            await File.WriteAllTextAsync(
                tempPath,
                """
                {
                  "Version": 6,
                  "Settings": {
                    "AdminPasswordHash": "",
                    "OperatorPasswordHash": "",
                    "ConfigEncryption": false
                  },
                  "Groups": [],
                  "Apps": [
                    {
                      "Name": "Notepad",
                      "Path": "notepad.exe"
                    }
                  ],
                  "Layouts": []
                }
                """);

            var imported = await service.ImportAsync(tempPath);

            Assert.NotNull(imported);
            Assert.Equal(11, imported!.Version);
            Assert.NotNull(imported.Settings.OperatorScope);
            Assert.Empty(imported.Settings.OperatorScope.AllowedAppNames);
            Assert.Empty(imported.Settings.OperatorScope.AllowedGroupKeys);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    [Fact]
    public void ResolveVisibleApps_Allows_All_Apps_When_Operator_Scope_Is_Empty()
    {
        var apps = new[]
        {
            new AppItem { Name = "A", Path = "a.exe", Group = "GroupOne" },
            new AppItem { Name = "B", Path = "b.exe", Group = "GroupTwo" }
        };

        var result = MainViewModel.ResolveVisibleApps(apps, AccessLevel.Operator, new OperatorScopeSettings());

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void ResolveVisibleApps_Uses_Union_Of_Allowed_App_Names_And_Group_Keys()
    {
        var apps = new[]
        {
            new AppItem { Name = "Alpha", Path = "alpha.exe", Group = "GroupOne" },
            new AppItem { Name = "Beta", Path = "beta.exe", Group = "GroupTwo" },
            new AppItem { Name = "Gamma", Path = "gamma.exe", Group = "GroupTwo" }
        };

        var scope = new OperatorScopeSettings
        {
            AllowedAppNames = [" Alpha "],
            AllowedGroupKeys = [" GroupTwo "]
        };
        scope.Normalize();

        var result = MainViewModel.ResolveVisibleApps(apps, AccessLevel.Operator, scope);

        Assert.Equal(["Alpha", "Beta", "Gamma"], result.Select(app => app.Name).OrderBy(name => name));
    }

    [Fact]
    public void ResolveVisibleGroups_Includes_Explicit_Allowed_Groups_And_Groups_Of_Visible_Apps()
    {
        GroupDefinition[] groups =
        [
            new GroupDefinition { Key = "GroupOne" },
            new GroupDefinition { Key = "GroupTwo" },
            new GroupDefinition { Key = "EmptyAllowedGroup" }
        ];
        var apps = new[]
        {
            new AppItem { Name = "Alpha", Path = "alpha.exe", Group = "GroupOne" }
        };
        var scope = new OperatorScopeSettings
        {
            AllowedAppNames = ["Alpha"],
            AllowedGroupKeys = ["EmptyAllowedGroup"]
        };
        scope.Normalize();

        var visibleApps = MainViewModel.ResolveVisibleApps(apps, AccessLevel.Operator, scope);
        var visibleGroups = MainViewModel.ResolveVisibleGroups(groups, visibleApps, AccessLevel.Operator, scope);

        Assert.Equal(
            ["EmptyAllowedGroup", "GroupOne"],
            visibleGroups.Select(group => group.Key).OrderBy(key => key));
    }

    [Fact]
    public void OperatorScopeSettings_Rename_And_Remove_Entries_Keep_Normalized_Sets()
    {
        var scope = new OperatorScopeSettings
        {
            AllowedAppNames = [" App One ", "App Two", "app two"],
            AllowedGroupKeys = [" GroupA ", "GroupB"]
        };

        scope.Normalize();
        scope.RenameAppName("App One", "Renamed App");
        scope.RemoveGroupKey("GroupB");

        Assert.Equal(["App Two", "Renamed App"], scope.AllowedAppNames.OrderBy(name => name));
        Assert.Equal(["GroupA"], scope.AllowedGroupKeys);
    }
}
