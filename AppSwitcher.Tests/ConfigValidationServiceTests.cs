using Microsoft.Extensions.Logging.Abstractions;

namespace AppSwitcher.Tests;

public class ConfigValidationServiceTests
{
    private readonly ConfigValidationService _service = new(NullLogger<ConfigValidationService>.Instance);

    [Fact]
    public void Validate_Rejects_Duplicate_App_Names()
    {
        var config = new ConfigRoot
        {
            Apps =
            [
                new AppItem { Name = "Notepad", Path = "notepad.exe" },
                new AppItem { Name = "notepad", Path = "calc.exe" }
            ]
        };

        var result = _service.Validate(config);

        Assert.True(result.HasErrors);
        Assert.Contains(result.Errors, issue => issue.Message.Contains("Diag_DuplicateAppName", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_Rejects_Circular_And_Self_Dependencies()
    {
        var config = new ConfigRoot
        {
            Apps =
            [
                new AppItem { Name = "A", Path = "a.exe", DependsOn = "B" },
                new AppItem { Name = "B", Path = "b.exe", DependsOn = "A" },
                new AppItem { Name = "Self", Path = "self.exe", DependsOn = "Self" }
            ]
        };

        var result = _service.Validate(config);

        Assert.True(result.HasErrors);
        Assert.Contains(result.Errors, issue => issue.Message.Contains("Diag_CircularDependency", StringComparison.Ordinal));
        Assert.Contains(result.Errors, issue => issue.Message.Contains("Diag_SelfDependency", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_Rejects_Invalid_Schedule_Format()
    {
        var config = new ConfigRoot
        {
            Apps =
            [
                new AppItem { Name = "Broken", Path = "broken.exe", ScheduleTime = "7" }
            ]
        };

        var result = _service.Validate(config);

        Assert.True(result.HasErrors);
        Assert.Contains(result.Errors, issue => issue.Message.Contains("Diag_InvalidTimeFormat", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_Rejects_Empty_Group_Keys_And_Negative_Values()
    {
        var config = new ConfigRoot
        {
            Settings = new AppSettings
            {
                SessionTimeoutMinutes = -1,
                MaxConfigBackups = -2,
                PreferredMonitor = -1
            },
            Groups =
            [
                new GroupDefinition { Key = "" }
            ],
            Apps =
            [
                new AppItem
                {
                    Name = "Broken",
                    Path = "broken.exe",
                    MaxRestarts = -1,
                    LaunchDelay = -5,
                    MemoryThresholdMB = -10,
                    ForcedMonitor = -1,
                    PlacementMode = (WindowPlacementMode)99
                }
            ]
        };

        var result = _service.Validate(config);

        Assert.True(result.HasErrors);
        Assert.Contains(result.Errors, issue => issue.Message.Contains("Diag_EmptyGroupKey", StringComparison.Ordinal));
        Assert.Contains(result.Errors, issue => issue.Message.Contains("Diag_NegativeValue", StringComparison.Ordinal));
        Assert.Contains(result.Errors, issue => issue.Message.Contains("Diag_InvalidPreferredMonitor", StringComparison.Ordinal));
        Assert.Contains(result.Errors, issue => issue.Message.Contains("Diag_InvalidForcedMonitor", StringComparison.Ordinal));
        Assert.Contains(result.Errors, issue => issue.Message.Contains("Diag_InvalidWindowPlacementMode", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_Rejects_Missing_Executable_Path()
    {
        var config = new ConfigRoot
        {
            Apps =
            [
                new AppItem { Name = "NoPath" }
            ]
        };

        var result = _service.Validate(config);

        Assert.True(result.HasErrors);
        Assert.Contains(result.Errors, issue => issue.Message.Contains("Diag_NoExePath", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_Treats_Missing_File_On_Disk_As_Warning()
    {
        var config = new ConfigRoot
        {
            Apps =
            [
                new AppItem { Name = "MissingFile", Path = @"Z:\definitely-not-present\missing.exe" }
            ]
        };

        var result = _service.Validate(config);

        Assert.False(result.HasErrors);
        Assert.Contains(result.Warnings, issue => issue.Message.Contains("Diag_ExeNotFound", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_Rejects_Hook_Arguments_Without_Path()
    {
        var config = new ConfigRoot
        {
            Apps =
            [
                new AppItem
                {
                    Name = "Worker",
                    Path = "worker.exe",
                    PreLaunchHookArgs = "-arg"
                }
            ]
        };

        var result = _service.Validate(config);

        Assert.True(result.HasErrors);
        Assert.Contains(result.Errors, issue => issue.Message.Contains("Diag_HookArgsWithoutPath", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_Warns_For_Missing_Local_Hook_Path()
    {
        var config = new ConfigRoot
        {
            Apps =
            [
                new AppItem
                {
                    Name = "Worker",
                    Path = "worker.exe",
                    PreLaunchHookPath = "missing-hook.ps1"
                }
            ]
        };

        var result = _service.Validate(config);

        Assert.False(result.HasErrors);
        Assert.Contains(result.Warnings, issue => issue.Message.Contains("Diag_HookNotFound", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_Rejects_Unknown_Operator_Scope_References()
    {
        var config = new ConfigRoot
        {
            Settings = new AppSettings
            {
                OperatorScope = new OperatorScopeSettings
                {
                    AllowedAppNames = ["Known App", " Missing App "],
                    AllowedGroupKeys = ["KnownGroup", " MissingGroup "]
                }
            },
            Groups =
            [
                new GroupDefinition { Key = "KnownGroup" }
            ],
            Apps =
            [
                new AppItem { Name = "Known App", Path = "known.exe" }
            ]
        };

        config.Settings.OperatorScope.Normalize();
        var result = _service.Validate(config);

        Assert.True(result.HasErrors);
        Assert.Contains(result.Errors, issue => issue.Message.Contains("Diag_OperatorScopeAppNotFound", StringComparison.Ordinal));
        Assert.Contains(result.Errors, issue => issue.Message.Contains("Diag_OperatorScopeGroupNotFound", StringComparison.Ordinal));
    }
}
