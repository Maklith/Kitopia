using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;

namespace KitopiaEx.Translate;

public static class TranslateApi
{
    private static HttpClient httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30),
        DefaultRequestHeaders =
        {
            { "User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/140.0.0.0 Safari/537.36 Edg/140.0.0.0" }
        }
    };

    private sealed record TranslationSession(Uri BaseUri, string Key, string Token, string ImpressionId,
        string InstanceId, DateTimeOffset ExpiresAt);

    private static TranslationSession? session;

    public static string TargetTranslateLangToName(TargetTranslateLang lang)
    {
        return lang switch
        {
            TargetTranslateLang.简体中文 => "zh-Hans",
            TargetTranslateLang.繁體中文 => "zh-Hant",
            TargetTranslateLang.English => "en",
            TargetTranslateLang.日本語 => "ja",
            _ => throw new ArgumentOutOfRangeException(nameof(lang), lang, null)
        };
    }

    public static string SourceTranslateLangToName(SourceTranslateLang lang)
    {
        return lang switch
        {
            SourceTranslateLang.自动检测 => "auto-detect",
            SourceTranslateLang.简体中文 => "zh-Hans",
            SourceTranslateLang.繁體中文 => "zh-Hant",
            SourceTranslateLang.English => "en",
            SourceTranslateLang.日本語 => "ja",
            _ => throw new ArgumentOutOfRangeException(nameof(lang), lang, null)
        };
    }

    public static async Task<string> GetTranslation(string text, SourceTranslateLang from, TargetTranslateLang to,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(text)) return text;

        if (Kitopia.ServiceProvider?.GetService<ITranslationService>() is { } translationService)
        {
            return await translationService.TranslateAsync(
                    text,
                    ToSourceLanguage(from),
                    ToTargetLanguage(to),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var sourceLanguage = SourceTranslateLangToName(from);
        var targetLanguage = TargetTranslateLangToName(to);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var authentication = session;
            if (attempt > 0 || authentication is null || authentication.ExpiresAt <= DateTimeOffset.UtcNow)
            {
                using var page = await httpClient.GetAsync("https://www.bing.com/translator", cancellationToken)
                    .ConfigureAwait(false);
                page.EnsureSuccessStatusCode();
                var html = await page.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                // Bing embeds its authentication data as a JSON array in the translator page.
                var config = Regex.Match(html, @"\bparams_AbusePreventionHelper\s*=\s*(\[[^\r\n;]+\])",
                    RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
                var impression = Regex.Match(html, "\\bIG:\\s*\"([^\"]+)\"",
                    RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
                var instance = Regex.Match(html, "id=\"rich_tta\"\\s+data-iid=\"([^\"]+)\"",
                    RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
                if (!config.Success || !impression.Success || !instance.Success)
                    throw new JsonException("Microsoft Translator authentication data is missing from the response.");

                using var document = JsonDocument.Parse(config.Groups[1].Value);
                var values = document.RootElement;
                if (values.ValueKind != JsonValueKind.Array || values.GetArrayLength() < 3 ||
                    values[0].ValueKind != JsonValueKind.Number || !values[0].TryGetInt64(out var key) ||
                    values[1].ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(values[1].GetString()) ||
                    values[2].ValueKind != JsonValueKind.Number || !values[2].TryGetInt64(out var lifetime) ||
                    lifetime <= 0)
                    throw new JsonException("Microsoft Translator returned invalid authentication data.");

                var baseUri = new Uri(page.RequestMessage!.RequestUri!.GetLeftPart(UriPartial.Authority));
                authentication = new TranslationSession(baseUri, key.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    values[1].GetString()!, impression.Groups[1].Value, instance.Groups[1].Value,
                    DateTimeOffset.UtcNow.AddMilliseconds(Math.Min(lifetime, 3_600_000)));
                session = authentication;
            }

            var current = authentication;
            var uri = new Uri(current.BaseUri,
                $"/ttranslatev3?isVertical=1&IG={Uri.EscapeDataString(current.ImpressionId)}&IID={Uri.EscapeDataString(current.InstanceId)}.1");
            using var request = new HttpRequestMessage(HttpMethod.Post, uri)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["fromLang"] = sourceLanguage,
                    ["to"] = targetLanguage,
                    ["text"] = text,
                    ["key"] = current.Key,
                    ["token"] = current.Token
                })
            };
            request.Headers.Referrer = new Uri(current.BaseUri, "/translator");
            using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (attempt == 0 && response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                continue;

            response.EnsureSuccessStatusCode();
            using var result = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            var root = result.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("statusCode", out var status) &&
                status.ValueKind == JsonValueKind.Number && status.TryGetInt32(out var statusCode) && statusCode >= 400)
            {
                if (attempt == 0 && statusCode is 401 or 403) continue;
                var message = root.TryGetProperty("errorMessage", out var error) && error.ValueKind == JsonValueKind.String
                    ? error.GetString() : "Microsoft Translator request failed.";
                throw new HttpRequestException(message, null, (HttpStatusCode)statusCode);
            }
            if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0 &&
                root[0].ValueKind == JsonValueKind.Object &&
                root[0].TryGetProperty("translations", out var translations) &&
                translations.ValueKind == JsonValueKind.Array && translations.GetArrayLength() > 0 &&
                translations[0].ValueKind == JsonValueKind.Object &&
                translations[0].TryGetProperty("text", out var translation) &&
                translation.ValueKind == JsonValueKind.String)
                return translation.GetString()!;

            throw new JsonException("The translation response contains no translated text.");
        }

        throw new HttpRequestException("Microsoft Translator authentication failed.");
    }

    private static TranslationSourceLanguage ToSourceLanguage(SourceTranslateLang language) => language switch
    {
        SourceTranslateLang.自动检测 => TranslationSourceLanguage.Auto,
        SourceTranslateLang.简体中文 => TranslationSourceLanguage.SimplifiedChinese,
        SourceTranslateLang.繁體中文 => TranslationSourceLanguage.TraditionalChinese,
        SourceTranslateLang.English => TranslationSourceLanguage.English,
        SourceTranslateLang.日本語 => TranslationSourceLanguage.Japanese,
        _ => throw new ArgumentOutOfRangeException(nameof(language), language, null)
    };

    private static TranslationTargetLanguage ToTargetLanguage(TargetTranslateLang language) => language switch
    {
        TargetTranslateLang.简体中文 => TranslationTargetLanguage.SimplifiedChinese,
        TargetTranslateLang.繁體中文 => TranslationTargetLanguage.TraditionalChinese,
        TargetTranslateLang.English => TranslationTargetLanguage.English,
        TargetTranslateLang.日本語 => TranslationTargetLanguage.Japanese,
        _ => throw new ArgumentOutOfRangeException(nameof(language), language, null)
    };
}
