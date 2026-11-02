using Microsoft.Extensions.Logging.Abstractions;

namespace AppSwitcher.Tests;

public class ExampleConfigurationTests
{
    [Theory]
    [InlineData("desktop-starter.json")]
    [InlineData("touch-workstation.json")]
    [InlineData("browser-console.json")]
    public async Task ExampleImportsWithSafeDefaultsAndValidReferences(string filename)
    {
        var validation = new ConfigValidationService(NullLogger<ConfigValidationService>.Instance);
        using var service = new ConfigService(NullLogger<ConfigService>.Instance, validation);
        var config = await service.ImportAsync(Path.Combine(AppContext.BaseDirectory, "examples", filename));
        Assert.NotNull(config);
        Assert.False(validation.Validate(config).HasErrors);
        Assert.False(config.Settings.KioskMode);
        Assert.Empty(config.Settings.AdminPasswordHash);
        Assert.Empty(config.Settings.OperatorPasswordHash);
        Assert.All(config.Apps, app =>
        {
            Assert.False(app.AutoLaunch);
            Assert.False(app.WatchdogEnabled);
            Assert.True(app.RequireConfirmation);
            Assert.All(new[] { app.ScheduleTime, app.ScheduleRestartTime, app.ScheduleShutdownTime,
                app.PreLaunchHookPath, app.PostLaunchHookPath, app.PreStopHookPath, app.PostStopHookPath, app.Args }, Assert.Empty);
            if (app.IsWebsite)
            {
                Assert.Equal("example.com", new Uri(app.WebsiteUrl).Host);
                Assert.Equal(Guid.Empty, Guid.Parse(app.BrowserProfileId));
            }
            else Assert.Equal(Path.GetFileName(app.Path), app.Path);
        });
        Assert.All(config.Layouts, layout =>
        {
            Assert.Empty(layout.Positions);
            Assert.All(layout.AppIds, id => Assert.Contains(config.Apps, app => app.AppId == id));
        });
        Assert.All(config.Settings.OperatorScope.AllowedAppNames, name => Assert.Contains(config.Apps, app => app.Name == name));
    }
}
