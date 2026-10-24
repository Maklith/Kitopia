using PluginCore;

namespace Kitopia.Desktop.Features.Translation;

public interface ISelectionTextTranslator
{
    Task<string> TranslateSelectionAsync(
        string text,
        TranslationSourceLanguage sourceLanguage = TranslationSourceLanguage.Auto,
        TranslationTargetLanguage targetLanguage = TranslationTargetLanguage.SimplifiedChinese,
        CancellationToken cancellationToken = default);
}
