using System.Globalization;
using Kitopia.Desktop.Features.Services.Config;
using Kitopia.Feature.Localization;
using PluginCore;

namespace Kitopia.Desktop.Features.Translation;

public static class TranslationLanguageResolver
{
    public static bool TryMapCulture(CultureInfo culture, out TranslationTargetLanguage language)
    {
        ArgumentNullException.ThrowIfNull(culture);

        var name = culture.Name;
        if (name.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
        {
            language = name.Contains("TW", StringComparison.OrdinalIgnoreCase) ||
                       name.Contains("HK", StringComparison.OrdinalIgnoreCase) ||
                       name.Contains("MO", StringComparison.OrdinalIgnoreCase) ||
                       name.Contains("Hant", StringComparison.OrdinalIgnoreCase) ||
                       name.Contains("CHT", StringComparison.OrdinalIgnoreCase)
                ? TranslationTargetLanguage.TraditionalChinese
                : TranslationTargetLanguage.SimplifiedChinese;
            return true;
        }

        if (culture.TwoLetterISOLanguageName.Equals("ja", StringComparison.OrdinalIgnoreCase))
        {
            language = TranslationTargetLanguage.Japanese;
            return true;
        }

        if (culture.TwoLetterISOLanguageName.Equals("en", StringComparison.OrdinalIgnoreCase))
        {
            language = TranslationTargetLanguage.English;
            return true;
        }

        language = default;
        return false;
    }

    public static bool TryResolveCurrentLanguage(
        string? configuredLanguage,
        CultureInfo? fallbackCulture,
        out TranslationTargetLanguage language)
    {
        var lang = configuredLanguage;
        if (string.IsNullOrWhiteSpace(lang))
        {
            lang = ConfigManger.Config?.language;
        }

        CultureInfo culture;
        if (!string.IsNullOrWhiteSpace(lang))
        {
            try
            {
                culture = CultureInfo.GetCultureInfo(lang);
            }
            catch (CultureNotFoundException)
            {
                culture = fallbackCulture ?? Lang.Current.Culture;
            }
        }
        else
        {
            culture = fallbackCulture ?? Lang.Current.Culture;
        }

        return TryMapCulture(culture, out language);
    }

    public static TranslationTargetLanguage ResolveTargetLanguageOrDefault(
        string? configuredLanguage = null,
        CultureInfo? fallbackCulture = null)
    {
        return TryResolveCurrentLanguage(configuredLanguage, fallbackCulture, out var language)
            ? language
            : TranslationTargetLanguage.SimplifiedChinese;
    }
}
