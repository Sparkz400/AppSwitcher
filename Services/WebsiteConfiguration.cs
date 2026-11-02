using System.Text.RegularExpressions;

namespace AppSwitcher.Services;

public static class WebsiteConfiguration
{
    public static bool IsHttpUrl(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https" && string.IsNullOrEmpty(uri.UserInfo);
    public static bool IsLegacyWebsite(AppItem app) => !app.IsWebsite &&
        (IsHttpUrl(app.Path) || (IsBrowserExecutable(app.Path) && Regex.IsMatch(app.Args, @"https?://", RegexOptions.IgnoreCase)));
    public static string? ExtractUrl(AppItem app)
    {
        if (IsHttpUrl(app.Path)) return app.Path;
        if (!IsBrowserExecutable(app.Path)) return null;
        var matches = Regex.Matches(app.Args, "https?://[^\\s\"']+", RegexOptions.IgnoreCase);
        return matches.Count == 1 && IsHttpUrl(matches[0].Value) ? matches[0].Value : null;
    }
    private static bool IsBrowserExecutable(string path) => System.IO.Path.GetFileNameWithoutExtension(path).ToLowerInvariant() is "chrome" or "msedge" or "firefox";
    public static IEnumerable<string> Validate(AppItem app, AppSettings settings)
    {
        if (!Guid.TryParse(app.AppId, out _)) yield return LocalizationService.T("Browser_InvalidIdentity");
        if (!Enum.IsDefined(app.TargetKind)) yield return LocalizationService.T("Browser_InvalidIdentity");
        if (!app.IsWebsite) yield break;
        if (!IsHttpUrl(app.WebsiteUrl)) yield return LocalizationService.T("Browser_InvalidUrl");
        if (!Enum.IsDefined(app.Browser) || !Guid.TryParse(app.BrowserProfileId, out _)) yield return LocalizationService.T("Browser_ProfileRequired");
        if (settings.KioskMode) yield return LocalizationService.T("Browser_KioskUnsupported");
        if (app.PlacementMode != WindowPlacementMode.None || app.ForcedMonitor != 0 || !string.IsNullOrEmpty(app.ForcedMonitorId) || app.AlwaysOnTop || app.ReapplyPlacementContinuously)
            yield return LocalizationService.T("Browser_PlacementUnsupported");
    }
}
