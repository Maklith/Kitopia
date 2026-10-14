using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Kitopia.Feature.Localization;

public sealed class Lang : ObservableObject
{
    public const string DefaultLanguage = "zh-CN";
    private readonly Dictionary<string, Dictionary<string, Dictionary<string, string>>> _sources = new(StringComparer.Ordinal);
    private Dictionary<string, string> _fallback = new(StringComparer.Ordinal);
    private Dictionary<string, string> _translations = new(StringComparer.Ordinal);

    public static Lang Current { get; } = new();

    public IReadOnlyList<string> Languages => _sources.Values.SelectMany(source => source.Keys)
        .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToArray();

    public string Language { get; private set; } = DefaultLanguage;
    public CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo(DefaultLanguage);
    public IEnumerable<string> Keys => _fallback.Keys.Union(_translations.Keys);
    public string this[string key] => _translations.TryGetValue(key, out var value)
        ? value : _fallback.GetValueOrDefault(key, key);

    public Lang()
    {
        RegisterAssembly(typeof(Lang).Assembly);
        UseLanguage(null);
    }

    public static string Get(string key) => Current[key];

    public static string Get(Enum value) => Get(value.GetType().GetField(value.ToString())
        ?.GetCustomAttribute<DescriptionAttribute>()?.Description ?? value.ToString());

    public static string Format(string key, params object?[] args) =>
        string.Format(Current.Culture, Current[key], args);

    public void UseLanguage(string? language)
    {
        var culture = string.IsNullOrWhiteSpace(language) ? CultureInfo.InstalledUICulture : GetCulture(language);
        var selected = Languages.FirstOrDefault(code => string.Equals(code, culture.Name, StringComparison.OrdinalIgnoreCase))
            ?? Languages.FirstOrDefault(code => GetCulture(code).TwoLetterISOLanguageName == culture.TwoLetterISOLanguageName)
            ?? DefaultLanguage;
        if (Language == selected)
            return;

        Language = selected;
        Culture = CultureInfo.GetCultureInfo(selected);
        CultureInfo.CurrentUICulture = Culture;
        CultureInfo.DefaultThreadCurrentUICulture = Culture;
        RefreshTranslations();
        OnPropertyChanged(nameof(Language));
    }

    public void RegisterAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        var name = assembly.GetName().Name!;
        var prefix = name + ".lang.";
        var dictionaries = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var resource in assembly.GetManifestResourceNames()
                     .Where(resource => resource.StartsWith(prefix, StringComparison.Ordinal) && resource.EndsWith(".json", StringComparison.Ordinal)))
        {
            var language = resource[prefix.Length..^5];
            _ = CultureInfo.GetCultureInfo(language);
            using var stream = assembly.GetManifestResourceStream(resource)!;
            var dictionary = JsonSerializer.Deserialize(stream, LangJsonContext.Default.DictionaryStringString)
                ?? throw new InvalidOperationException($"Invalid language dictionary: {resource}.");
            var keyPrefix = name == typeof(Lang).Assembly.GetName().Name ? "lang.kitopia." : "lang." + name.ToLowerInvariant() + ".";
            if (keyPrefix == "lang.kitopia." && assembly != typeof(Lang).Assembly)
                throw new InvalidOperationException("The lang.kitopia namespace is reserved for the client.");
            foreach (var key in dictionary.Keys)
                if (!key.StartsWith(keyPrefix, StringComparison.Ordinal) || key.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '.' or '_')))
                    throw new InvalidOperationException($"Language key '{key}' must use the namespace '{keyPrefix}'.");
            dictionaries.Add(language, dictionary);
        }
        if (dictionaries.Count == 0) return;
        if (!dictionaries.ContainsKey(DefaultLanguage))
            throw new InvalidOperationException($"Assembly '{name}' needs a {DefaultLanguage} fallback dictionary.");
        // Retain strings only, so localization cannot keep a collectible plugin assembly alive.
        _sources[name] = dictionaries;
        RefreshTranslations();
        OnPropertyChanged(nameof(Languages));
    }

    public void UnregisterAssembly(string assemblyName)
    {
        if (assemblyName == typeof(Lang).Assembly.GetName().Name || !_sources.Remove(assemblyName)) return;
        RefreshTranslations();
        OnPropertyChanged(nameof(Languages));
    }

    private void RefreshTranslations()
    {
        var fallback = new Dictionary<string, string>(StringComparer.Ordinal);
        var translations = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var source in _sources.Values)
        {
            foreach (var entry in source[DefaultLanguage]) fallback.Add(entry.Key, entry.Value);
            var selected = source.GetValueOrDefault(Language)
                ?? source.FirstOrDefault(pair => GetCulture(pair.Key).TwoLetterISOLanguageName == Culture.TwoLetterISOLanguageName).Value;
            if (selected is not null)
                foreach (var entry in selected) translations.Add(entry.Key, entry.Value);
        }
        _fallback = fallback;
        _translations = translations;
        OnPropertyChanged(nameof(Keys));
        OnPropertyChanged("Item");
        OnPropertyChanged("Item[]");
    }

    private static CultureInfo GetCulture(string language)
    {
        try { return CultureInfo.GetCultureInfo(language); }
        catch (CultureNotFoundException) { return CultureInfo.GetCultureInfo(DefaultLanguage); }
    }

}

[JsonSerializable(typeof(Dictionary<string, string>))]
internal partial class LangJsonContext : JsonSerializerContext;
