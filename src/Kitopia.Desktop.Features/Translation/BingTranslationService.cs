using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using PluginCore;

namespace Kitopia.Desktop.Features.Translation;

public sealed class BingTranslationService : ITranslationService
{
    private static readonly TimeSpan SessionLifetimeLimit = TimeSpan.FromHours(1);
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _sessionGate = new(1, 1);
    private TranslationSession? _session;

    public BingTranslationService()
        : this(new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        })
    {
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/140.0.0.0 Safari/537.36 Edg/140.0.0.0");
    }

    internal BingTranslationService(HttpClient httpClient)
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

        var from = GetSourceLanguage(sourceLanguage);
        var to = GetTargetLanguage(targetLanguage);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var session = await GetSessionAsync(attempt > 0, cancellationToken).ConfigureAwait(false);
            using var request = new HttpRequestMessage(HttpMethod.Post,
                new Uri(session.BaseUri,
                    $"/ttranslatev3?isVertical=1&IG={Uri.EscapeDataString(session.ImpressionId)}&IID={Uri.EscapeDataString(session.InstanceId)}.1"))
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["fromLang"] = from,
                    ["to"] = to,
                    ["text"] = text,
                    ["key"] = session.Key,
                    ["token"] = session.Token
                })
            };
            request.Headers.Referrer = new Uri(session.BaseUri, "/translator");

            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (attempt == 0 && response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                InvalidateSession(session);
                continue;
            }

            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("statusCode", out var status) &&
                status.ValueKind == JsonValueKind.Number && status.TryGetInt32(out var statusCode) && statusCode >= 400)
            {
                if (attempt == 0 && statusCode is 401 or 403)
                {
                    InvalidateSession(session);
                    continue;
                }

                var message = root.TryGetProperty("errorMessage", out var error) &&
                              error.ValueKind == JsonValueKind.String
                    ? error.GetString()
                    : "Microsoft Translator request failed.";
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

    private async Task<TranslationSession> GetSessionAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        var current = Volatile.Read(ref _session);
        if (!forceRefresh && current is not null && current.ExpiresAt > DateTimeOffset.UtcNow)
            return current;

        await _sessionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            current = _session;
            if (!forceRefresh && current is not null && current.ExpiresAt > DateTimeOffset.UtcNow)
                return current;

            using var page = await _httpClient.GetAsync("https://www.bing.com/translator", cancellationToken)
                .ConfigureAwait(false);
            page.EnsureSuccessStatusCode();
            var html = await page.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
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

            var requestUri = page.RequestMessage?.RequestUri ?? new Uri("https://www.bing.com/translator");
            current = new TranslationSession(
                new Uri(requestUri.GetLeftPart(UriPartial.Authority)),
                key.ToString(CultureInfo.InvariantCulture),
                values[1].GetString()!,
                impression.Groups[1].Value,
                instance.Groups[1].Value,
                DateTimeOffset.UtcNow.AddMilliseconds(Math.Min(lifetime, (long)SessionLifetimeLimit.TotalMilliseconds)));
            Volatile.Write(ref _session, current);
            return current;
        }
        finally
        {
            _sessionGate.Release();
        }
    }

    private void InvalidateSession(TranslationSession session)
    {
        Interlocked.CompareExchange(ref _session, null, session);
    }

    private static string GetSourceLanguage(TranslationSourceLanguage language) => language switch
    {
        TranslationSourceLanguage.Auto => "auto-detect",
        TranslationSourceLanguage.SimplifiedChinese => "zh-Hans",
        TranslationSourceLanguage.TraditionalChinese => "zh-Hant",
        TranslationSourceLanguage.English => "en",
        TranslationSourceLanguage.Japanese => "ja",
        _ => throw new ArgumentOutOfRangeException(nameof(language), language, null)
    };

    private static string GetTargetLanguage(TranslationTargetLanguage language) => language switch
    {
        TranslationTargetLanguage.SimplifiedChinese => "zh-Hans",
        TranslationTargetLanguage.TraditionalChinese => "zh-Hant",
        TranslationTargetLanguage.English => "en",
        TranslationTargetLanguage.Japanese => "ja",
        _ => throw new ArgumentOutOfRangeException(nameof(language), language, null)
    };

    private sealed record TranslationSession(
        Uri BaseUri,
        string Key,
        string Token,
        string ImpressionId,
        string InstanceId,
        DateTimeOffset ExpiresAt);
}
