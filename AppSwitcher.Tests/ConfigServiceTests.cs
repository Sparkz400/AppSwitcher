using Microsoft.Extensions.Logging.Abstractions;

namespace AppSwitcher.Tests;

public class ConfigServiceTests
{
    [Fact]
    public async Task ImportAsync_Migrates_V8_Config_To_V11()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(
                path,
                """
                {
                  "Version": 8,
                  "Settings": {
                    "Theme": "Dark",
                    "Language": "en",
                    "OperatorScope": {
                      "AllowedAppNames": [],
                      "AllowedGroupKeys": []
                    }
                  },
                  "Apps": [
                    {
                      "Name": "Worker",
                      "Path": "worker.exe"
                    }
                  ],
                  "Groups": [],
                  "Layouts": []
                }
                """);

            var service = CreateService();
            var config = await service.ImportAsync(path);

            Assert.NotNull(config);
            Assert.Equal(11, config!.Version);
            Assert.Equal(ReadinessMode.None, config.Apps[0].Readiness);
            Assert.False(config.Settings.GlobalShortcutEnabled);
            Assert.Equal(string.Empty, config.Apps[0].PreLaunchHookPath);
            Assert.Equal(string.Empty, config.Apps[0].PostStopHookArgs);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Json_RoundTrip_Preserves_Lifecycle_Hook_Fields()
    {
        var config = new ConfigRoot
        {
            Apps =
            [
                new AppItem
                {
                    Name = "Worker",
                    Path = "worker.exe",
                    PreLaunchHookPath = "pre.ps1",
                    PreLaunchHookArgs = "-One",
                    PostLaunchHookPath = "post.cmd",
                    PostLaunchHookArgs = "-Two",
                    PreStopHookPath = "stop.ps1",
                    PreStopHookArgs = "-Three",
                    PostStopHookPath = "after.cmd",
                    PostStopHookArgs = "-Four"
                }
            ]
        };

        var json = JsonSerializer.Serialize(config, AppJsonOptions.Default);
        var roundTrip = JsonSerializer.Deserialize<ConfigRoot>(json, AppJsonOptions.Default);

        Assert.NotNull(roundTrip);
        var app = Assert.Single(roundTrip!.Apps);
        Assert.Equal("pre.ps1", app.PreLaunchHookPath);
        Assert.Equal("-Two", app.PostLaunchHookArgs);
        Assert.Equal("stop.ps1", app.PreStopHookPath);
        Assert.Equal("-Four", app.PostStopHookArgs);
    }

    private static ConfigService CreateService() =>
        new(
            NullLogger<ConfigService>.Instance,
            new ConfigValidationService(NullLogger<ConfigValidationService>.Instance));
}
