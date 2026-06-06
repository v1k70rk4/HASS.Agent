namespace HASS.Agent.Companion.Localization;

internal static partial class Strings
{
    private static readonly Lazy<Dictionary<string, Dictionary<string, string>>> TranslationSource = new(() => new()
    {
        ["hu"] = Hu!,
        ["en"] = En!
    });

    private static Dictionary<string, Dictionary<string, string>> Translations => TranslationSource.Value;

    private static string _language = "hu";
    private static string _haLanguage = "hu";

    public static string Language
    {
        get => _language;
        set => _language = Translations.ContainsKey(value) ? value : "hu";
    }

    public static string HaLanguage
    {
        get => _haLanguage;
        set => _haLanguage = Translations.ContainsKey(value) ? value : "hu";
    }

    public static IReadOnlyList<string> AvailableLanguages => ["hu", "en"];

    public static string GetDisplayName(string langCode) => langCode switch
    {
        "hu" => "Magyar",
        "en" => "English",
        _ => langCode
    };

    public static string Get(string key)
    {
        if (Translations.TryGetValue(_language, out var dict) && dict.TryGetValue(key, out var value))
            return value;
        if (Translations.TryGetValue("en", out var fallback) && fallback.TryGetValue(key, out var fb))
            return fb;
        return key;
    }

    public static string GetHa(string key)
    {
        if (Translations.TryGetValue(_haLanguage, out var dict) && dict.TryGetValue(key, out var value))
            return value;
        if (Translations.TryGetValue("en", out var fallback) && fallback.TryGetValue(key, out var fb))
            return fb;
        return key;
    }
}
