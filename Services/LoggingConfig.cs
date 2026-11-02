using Serilog;

namespace AppSwitcher.Services;

public static class LoggingConfig
{
    public static void Configure(ILoggingBuilder builder)
    {
        Directory.CreateDirectory(AppPaths.LogDirectory);

        var logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                path: Path.Combine(AppPaths.LogDirectory, "appswitcher-.log"),
                rollingInterval: Serilog.RollingInterval.Day,
                retainedFileCountLimit: 14,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        builder.AddSerilog(logger, dispose: true);
    }
}
