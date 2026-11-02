using Microsoft.Extensions.Logging.Abstractions;

namespace AppSwitcher.Tests;

public class ConfigDiffServiceTests
{
    [Fact]
    public void ComputeAndLog_Does_Not_Throw_On_Duplicate_App_Names()
    {
        var service = new ConfigDiffService(NullLogger<ConfigDiffService>.Instance);
        var oldConfig = new ConfigRoot
        {
            Apps =
            [
                new AppItem { Name = "Duplicate", Path = "old.exe" },
                new AppItem { Name = "duplicate", Path = "other-old.exe" }
            ]
        };
        var newConfig = new ConfigRoot
        {
            Apps =
            [
                new AppItem { Name = "Duplicate", Path = "new.exe" },
                new AppItem { Name = "duplicate", Path = "other-new.exe" }
            ]
        };

        var summary = service.ComputeAndLog(oldConfig, newConfig);

        Assert.False(string.IsNullOrWhiteSpace(summary));
    }

    [Fact]
    public void Compute_Includes_Window_Policy_Field_Changes()
    {
        var service = new ConfigDiffService(NullLogger<ConfigDiffService>.Instance);
        var oldConfig = new ConfigRoot
        {
            Apps =
            [
                new AppItem
                {
                    Name = "Worker",
                    Path = "worker.exe",
                    PlacementMode = WindowPlacementMode.None,
                    ForcedMonitor = 0,
                    AlwaysOnTop = false,
                    ReapplyPlacementContinuously = false
                }
            ]
        };
        var newConfig = new ConfigRoot
        {
            Apps =
            [
                new AppItem
                {
                    Name = "Worker",
                    Path = "worker.exe",
                    PlacementMode = WindowPlacementMode.Fullscreen,
                    ForcedMonitor = 2,
                    AlwaysOnTop = true,
                    ReapplyPlacementContinuously = true
                }
            ]
        };

        var result = service.Compute(oldConfig, newConfig);
        var descriptions = result.Changes.Select(change => change.Description).ToArray();

        Assert.Contains(descriptions, description => description.Contains("PlacementMode", StringComparison.Ordinal));
        Assert.Contains(descriptions, description => description.Contains("ForcedMonitor", StringComparison.Ordinal));
        Assert.Contains(descriptions, description => description.Contains("AlwaysOnTop", StringComparison.Ordinal));
        Assert.Contains(descriptions, description => description.Contains("ReapplyPlacementContinuously", StringComparison.Ordinal));
    }

    [Fact]
    public void Compute_Returns_Typed_Changes_And_Summary()
    {
        var service = new ConfigDiffService(NullLogger<ConfigDiffService>.Instance);
        var oldConfig = new ConfigRoot
        {
            Settings = new AppSettings
            {
                Theme = "Dark",
                KioskMode = false
            }
        };
        var newConfig = new ConfigRoot
        {
            Settings = new AppSettings
            {
                Theme = "Light",
                KioskMode = true
            }
        };

        var result = service.Compute(oldConfig, newConfig);

        Assert.True(result.HasChanges);
        Assert.Contains(result.Changes, change => change.Key == "settings.theme");
        Assert.Contains(result.Changes, change => change.Key == "settings.kioskmode");
        Assert.Contains("Theme: Dark → Light", result.BuildSummary());
    }

    [Fact]
    public void Compute_Returns_Empty_When_No_Meaningful_Changes_Exist()
    {
        var service = new ConfigDiffService(NullLogger<ConfigDiffService>.Instance);
        var config = new ConfigRoot
        {
            Settings = new AppSettings { Theme = "Dark" },
            Apps = [new AppItem { Name = "Worker", Path = "worker.exe" }]
        };

        var result = service.Compute(config, config);

        Assert.False(result.HasChanges);
        Assert.Empty(result.BuildSummary());
        Assert.Empty(result.BuildDetails());
    }

    [Fact]
    public void Compute_Includes_Lifecycle_Hook_Field_Changes()
    {
        var service = new ConfigDiffService(NullLogger<ConfigDiffService>.Instance);
        var oldConfig = new ConfigRoot
        {
            Apps =
            [
                new AppItem
                {
                    Name = "Worker",
                    Path = "worker.exe",
                    PreLaunchHookPath = "pre.ps1",
                    PostStopHookArgs = "-old"
                }
            ]
        };
        var newConfig = new ConfigRoot
        {
            Apps =
            [
                new AppItem
                {
                    Name = "Worker",
                    Path = "worker.exe",
                    PreLaunchHookPath = "pre-new.ps1",
                    PostStopHookArgs = "-new"
                }
            ]
        };

        var result = service.Compute(oldConfig, newConfig);
        var descriptions = result.Changes.Select(change => change.Description).ToArray();

        Assert.Contains(descriptions, description => description.Contains("PreLaunchHook", StringComparison.Ordinal));
        Assert.Contains(descriptions, description => description.Contains("PostStopHook", StringComparison.Ordinal));
    }
}
