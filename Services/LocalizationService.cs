namespace AppSwitcher.Services;

public class LocalizationService
{
    private readonly ILogger<LocalizationService> _logger;

    /// <summary>
    /// Registry of all supported languages. Add new entries here when adding a language .xaml file.
    /// Key = language code, Value = (native label, resource URI).
    /// </summary>
    public static readonly List<LanguageInfo> SupportedLanguages =
    [
        new("de", "Deutsch", new Uri("Languages/de.xaml", UriKind.Relative)),
        new("en", "English", new Uri("Languages/en.xaml", UriKind.Relative)),
    ];

    public LocalizationService(ILogger<LocalizationService> logger)
    {
        _logger = logger;
    }

    public void ApplyLanguage(string langCode)
    {
        if (Application.Current == null)
            return;

        var langInfo = SupportedLanguages.FirstOrDefault(l => l.Code == langCode)
                       ?? SupportedLanguages[0]; // Fallback to first language

        var merged = Application.Current.Resources.MergedDictionaries;
        var existingIndex = FindMergedDictionaryIndex(merged, "Languages/");
        if (existingIndex >= 0 && merged[existingIndex].Source == langInfo.ResourceUri) return;
        var dict = new ResourceDictionary { Source = langInfo.ResourceUri };

        if (existingIndex >= 0)
            merged[existingIndex] = dict;
        else
            merged.Add(dict);

        _logger.LogInformation("Language applied: {Lang}", langCode);
    }

    /// <summary>
    /// Gets a localized string from application resources.
    /// Supports {0}, {1}, ... format placeholders.
    /// </summary>
    public static string T(string key, params object[] args)
    {
        var value = Application.Current?.Resources[key] as string ?? key;
        return args.Length > 0 ? string.Format(value, args) : value;
    }

    private static int FindMergedDictionaryIndex(IList<ResourceDictionary> merged, string pathPrefix)
    {
        for (var i = 0; i < merged.Count; i++)
        {
            var source = merged[i].Source?.OriginalString;
            if (!string.IsNullOrWhiteSpace(source)
                && source.StartsWith(pathPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    public record LanguageInfo(string Code, string Label, Uri ResourceUri);
}
