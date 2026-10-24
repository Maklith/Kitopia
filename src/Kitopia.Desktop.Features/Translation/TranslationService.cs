using Kitopia.Desktop.Features.Services.Interfaces;
using PluginCore;

namespace Kitopia.Desktop.Features.Translation;

public sealed class TranslationService(
    IConfigService configService,
    IndexTranslationService indexTranslationService,
    BingTranslationService bingTranslationService) : ITranslationService, ISelectionTextTranslator
{
    public Task<string> TranslateAsync(
        string text,
        TranslationSourceLanguage sourceLanguage = TranslationSourceLanguage.Auto,
        TranslationTargetLanguage targetLanguage = TranslationTargetLanguage.SimplifiedChinese,
        CancellationToken cancellationToken = default)
    {
        ITranslationService service = configService.Config.translationProvider switch
        {
            TranslationProvider.IndexTranslate => indexTranslationService,
            TranslationProvider.Microsoft => bingTranslationService,
            var provider => throw new ArgumentOutOfRangeException(nameof(provider), provider, null)
        };
        return service.TranslateAsync(text, sourceLanguage, targetLanguage, cancellationToken);
    }

    public Task<string> TranslateSelectionAsync(
        string text,
        TranslationSourceLanguage sourceLanguage = TranslationSourceLanguage.Auto,
        TranslationTargetLanguage targetLanguage = TranslationTargetLanguage.SimplifiedChinese,
        CancellationToken cancellationToken = default)
        => configService.Config.translationProvider switch
        {
            TranslationProvider.IndexTranslate => indexTranslationService.TranslateSelectionAsync(
                text, sourceLanguage, targetLanguage, cancellationToken),
            TranslationProvider.Microsoft => bingTranslationService.TranslateAsync(
                text, sourceLanguage, targetLanguage, cancellationToken),
            var provider => throw new ArgumentOutOfRangeException(nameof(provider), provider, null)
        };
}
