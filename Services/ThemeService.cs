namespace AppSwitcher.Services;

public class ThemeService
{
    private readonly ILogger<ThemeService> _logger;

    public ThemeService(ILogger<ThemeService> logger)
    {
        _logger = logger;
    }

    public void ApplyTheme(string theme)
        => ApplyTheme(theme, SystemParameters.HighContrast);

    internal void ApplyTheme(string theme, bool highContrast)
    {
        if (Application.Current == null)
            return;

        Application.Current.Resources["HighContrastEnabled"] = highContrast;
        var uri = highContrast ? new Uri("Themes/HighContrastTheme.xaml", UriKind.Relative) : theme == "Light"
            ? new Uri("Themes/LightTheme.xaml", UriKind.Relative)
            : new Uri("Themes/DarkTheme.xaml", UriKind.Relative);

        var merged = Application.Current.Resources.MergedDictionaries;
        var existingIndex = FindThemeDictionaryIndex(merged);
        if (existingIndex >= 0 && merged[existingIndex].Source == uri) return;
        var dict = new ResourceDictionary { Source = uri };
        if (existingIndex >= 0)
            merged[existingIndex] = dict;
        else
            merged.Insert(0, dict);

        _logger.LogInformation("Theme applied: {Theme}", highContrast ? "Windows high contrast" : theme);
    }

    private static int FindThemeDictionaryIndex(IList<ResourceDictionary> merged)
    {
        for (var i = 0; i < merged.Count; i++)
        {
            var source = merged[i].Source?.OriginalString;
            if (!string.IsNullOrWhiteSpace(source)
                && source.StartsWith("Themes/", StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }
}
