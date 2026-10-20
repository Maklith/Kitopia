using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using PluginCore;

namespace Kitopia.Desktop.Features.Translation;

public sealed class IndexTranslationService : ITranslationService
{
    private readonly HttpClient _httpClient;

    public IndexTranslationService() : this(new HttpClient { Timeout = TimeSpan.FromSeconds(120) })
    {
    }

    internal IndexTranslationService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<string> TranslateAsync(
        string text,
        TranslationSourceLanguage sourceLanguage = TranslationSourceLanguage.Auto,
        TranslationTargetLanguage targetLanguage = TranslationTargetLanguage.SimplifiedChinese,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(text)) return text;

        var source = sourceLanguage switch
        {
            TranslationSourceLanguage.Auto => "",
            TranslationSourceLanguage.SimplifiedChinese => "简体中文",
            TranslationSourceLanguage.TraditionalChinese => "繁体中文",
            TranslationSourceLanguage.English => "英语",
            TranslationSourceLanguage.Japanese => "日语",
            _ => throw new ArgumentOutOfRangeException(nameof(sourceLanguage), sourceLanguage, null)
        };
        var target = targetLanguage switch
        {
            TranslationTargetLanguage.SimplifiedChinese => "简体中文",
            TranslationTargetLanguage.TraditionalChinese => "繁体中文",
            TranslationTargetLanguage.English => "英语",
            TranslationTargetLanguage.Japanese => "日语",
            _ => throw new ArgumentOutOfRangeException(nameof(targetLanguage), targetLanguage, null)
        };
        using var request = new HttpRequestMessage(HttpMethod.Post,
            "https://index-translate.bilibili.com/v1/chat/completions")
        {
            Content = JsonContent.Create(new
            {
                model = "Index-Translate-35B-A3B",
                messages = new[]
                {
                    new { role = "user", content = $"请将以下{source}文本翻译为{target}，直接输出翻译结果，不要进行任何解释。\n\n{text}" }
                },
                temperature = 0,
                max_tokens = 4096,
                stream = false,
                chat_template_kwargs = new { enable_thinking = false }
            })
        };
        request.Headers.UserAgent.ParseAdd("Index-Translate-Client/1.0");

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array ||
            choices.GetArrayLength() == 0 || choices[0].ValueKind != JsonValueKind.Object ||
            !choices[0].TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object ||
            !message.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.String)
            throw new JsonException("The Index-Translate response contains no translated text.");

        if (choices[0].TryGetProperty("finish_reason", out var finishReason) &&
            finishReason.ValueKind == JsonValueKind.String && finishReason.GetString() == "length")
            throw new HttpRequestException("Index-Translate exceeded the translation output limit.");

        var translation = content.GetString()!;
        // The official client also removes thinking blocks even when thinking is disabled.
        var thinkingEnd = translation.IndexOf("</think>", StringComparison.Ordinal);
        if (thinkingEnd >= 0)
            translation = translation[(thinkingEnd + "</think>".Length)..];
        translation = translation.Trim();
        if (translation.Length == 0 || translation.StartsWith("<think>", StringComparison.Ordinal))
            throw new JsonException("The Index-Translate response contains no completed translation.");
        return translation;
    }
}
