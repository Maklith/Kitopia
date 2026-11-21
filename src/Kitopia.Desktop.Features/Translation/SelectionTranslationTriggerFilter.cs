using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using PluginCore;

namespace Kitopia.Desktop.Features.Translation;

public static partial class SelectionTranslationTriggerFilter
{
    public enum SkipReason
    {
        None,
        Empty,
        TooLong,
        NoTranslatableText,
        NonNaturalLanguage,
        MatchesTargetLanguage,
        MatchesApplicationLanguage,
        SourceLanguageMismatch,
        SameSourceAndTarget
    }

    public enum DetectedLanguage
    {
        Unknown,
        Chinese,
        English,
        Japanese
    }

    public readonly record struct Decision(bool ShouldTrigger, SkipReason Reason);

    private static readonly string[] UrlPrefixes =
    [
        "http://",
        "https://",
        "ftp://",
        "ftps://",
        "file://",
        "mailto:",
        "ws://",
        "wss://"
    ];

    private static readonly string[] CodeMarkers =
    [
        "{",
        "}",
        "=>",
        "==",
        "!=",
        "&&",
        "||",
        "::",
        "</",
        "/>",
        "/*",
        "*/"
    ];

    public static Decision Evaluate(
        string? text,
        int maximumLength,
        TranslationSourceLanguage sourceLanguage,
        TranslationTargetLanguage targetLanguage,
        string? configuredAppLanguage = null,
        CultureInfo? fallbackAppCulture = null)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new Decision(false, SkipReason.Empty);

        var trimmed = text.Trim();
        if (trimmed.Length > maximumLength)
            return new Decision(false, SkipReason.TooLong);

        var counts = AnalyzeRunes(trimmed);
        if (counts.TotalLetters == 0)
            return new Decision(false, SkipReason.NoTranslatableText);

        if (IsNonNaturalLanguage(trimmed, counts))
            return new Decision(false, SkipReason.NonNaturalLanguage);

        if (counts.TotalLetters == 1 && counts.AsciiLatin == 1)
            return new Decision(false, SkipReason.NoTranslatableText);

        var detected = DetectLanguage(counts);
        if (sourceLanguage != TranslationSourceLanguage.Auto)
        {
            if (IsSameConfiguredLanguage(sourceLanguage, targetLanguage))
                return new Decision(false, SkipReason.SameSourceAndTarget);

            if (detected != DetectedLanguage.Unknown && !MatchesSource(detected, sourceLanguage))
                return new Decision(false, SkipReason.SourceLanguageMismatch);

            if (detected != DetectedLanguage.Unknown &&
                !IsChineseConversion(sourceLanguage, targetLanguage) &&
                MatchesTarget(detected, targetLanguage))
                return new Decision(false, SkipReason.MatchesTargetLanguage);

            return new Decision(true, SkipReason.None);
        }

        if (detected != DetectedLanguage.Unknown && MatchesTarget(detected, targetLanguage))
            return new Decision(false, SkipReason.MatchesTargetLanguage);

        if (detected != DetectedLanguage.Unknown &&
            TranslationLanguageResolver.TryResolveCurrentLanguage(
                configuredAppLanguage, fallbackAppCulture, out var appLanguage) &&
            MatchesTarget(detected, appLanguage))
            return new Decision(false, SkipReason.MatchesApplicationLanguage);

        return new Decision(true, SkipReason.None);
    }

    internal static DetectedLanguage DetectLanguage(string text)
    {
        return DetectLanguage(AnalyzeRunes(text));
    }

    private static DetectedLanguage DetectLanguage(in RuneCounts counts)
    {
        var latin = counts.AsciiLatin + counts.NonAsciiLatin;
        if (counts.Kana > 0 &&
            (counts.Kana + counts.Han) * 4 >= latin + counts.OtherLetters + counts.Hangul)
            return DetectedLanguage.Japanese;

        if (counts.Han > 0 &&
            counts.Kana == 0 &&
            counts.Hangul == 0 &&
            counts.Han * 4 >= latin + counts.OtherLetters)
            return DetectedLanguage.Chinese;

        if (counts.AsciiLatin > 0 &&
            counts.NonAsciiLatin == 0 &&
            counts.Han == 0 &&
            counts.Kana == 0 &&
            counts.Hangul == 0 &&
            counts.OtherLetters == 0)
            return DetectedLanguage.English;

        return DetectedLanguage.Unknown;
    }

    private static RuneCounts AnalyzeRunes(string text)
    {
        var nonWhitespace = 0;
        var han = 0;
        var kana = 0;
        var hangul = 0;
        var asciiLatin = 0;
        var nonAsciiLatin = 0;
        var otherLetters = 0;

        foreach (var rune in text.EnumerateRunes())
        {
            if (!Rune.IsWhiteSpace(rune))
                nonWhitespace++;

            var value = rune.Value;
            if (IsAsciiLatin(value))
            {
                asciiLatin++;
            }
            else if (IsHan(value))
            {
                han++;
            }
            else if (IsKana(value))
            {
                kana++;
            }
            else if (IsHangul(value))
            {
                hangul++;
            }
            else if (Rune.IsLetter(rune))
            {
                if (IsNonAsciiLatin(value))
                    nonAsciiLatin++;
                else
                    otherLetters++;
            }
        }

        return new RuneCounts(nonWhitespace, han, kana, hangul, asciiLatin, nonAsciiLatin, otherLetters);
    }

    private static bool IsNonNaturalLanguage(string trimmed, in RuneCounts counts)
    {
        if (counts.TotalLetters * 5 < counts.NonWhitespace * 2)
            return true;

        foreach (var marker in CodeMarkers)
        {
            if (trimmed.Contains(marker, StringComparison.Ordinal))
                return true;
        }

        if (ContainsWhitespace(trimmed))
            return false;

        return IsNonNaturalSingleToken(trimmed);
    }

    private static bool IsNonNaturalSingleToken(string token)
    {
        foreach (var prefix in UrlPrefixes)
        {
            if (token.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        if (token.StartsWith("www.", StringComparison.OrdinalIgnoreCase) && token.Length > 4)
            return true;

        if (HostPortRegex().IsMatch(token))
            return true;

        if (IsPathToken(token))
            return true;

        if (IsEmailToken(token))
            return true;

        if ((token.Contains('.') || token.Contains(':')) && IPAddress.TryParse(token, out _))
            return true;

        if (Guid.TryParse(token, out _))
            return true;

        if (HexColorRegex().IsMatch(token) || HexLiteralRegex().IsMatch(token) || IsHexHash(token))
            return true;

        if (VersionTokenRegex().IsMatch(token) || TargetFrameworkRegex().IsMatch(token))
            return true;

        if (token.StartsWith('-') || token.StartsWith('$') || token.StartsWith('%'))
            return true;

        if (token.Contains('_') ||
            token.Contains("->", StringComparison.Ordinal) ||
            token.Contains("()", StringComparison.Ordinal))
            return true;

        if (IsDottedIdentifierOrBareDomain(token))
            return true;

        return false;
    }

    private static bool IsPathToken(string token)
    {
        if (token.Length >= 3 && char.IsAsciiLetter(token[0]) && token[1] == ':' && token[2] is '\\' or '/')
            return true;

        if (token.StartsWith(@"\\", StringComparison.Ordinal) ||
            token.StartsWith("~/", StringComparison.Ordinal) ||
            token.StartsWith("./", StringComparison.Ordinal) ||
            token.StartsWith("../", StringComparison.Ordinal) ||
            token.StartsWith(".\\", StringComparison.Ordinal) ||
            token.StartsWith("..\\", StringComparison.Ordinal) ||
            (token.StartsWith('/') && token.Length > 1) ||
            token.Contains('\\') ||
            token.Contains("//", StringComparison.Ordinal))
            return true;

        var firstSlash = token.IndexOf('/');
        if (firstSlash <= 0 || firstSlash >= token.Length - 1)
            return false;

        // Multi-segment slash tokens (a/b/c) or path-like single slashes with dots/underscores (src/Program.cs).
        if (token.IndexOf('/', firstSlash + 1) >= 0)
            return true;

        var left = token[..firstSlash];
        var right = token[(firstSlash + 1)..];
        return IsDottedIdentifierOrBareDomain(left) ||
               IsDottedIdentifierOrBareDomain(right) ||
               left.StartsWith('.') ||
               right.StartsWith('.');
    }

    private static bool IsEmailToken(string token)
    {
        var at = token.IndexOf('@');
        if (at <= 0 || at >= token.Length - 1 || token.IndexOf('@', at + 1) >= 0)
            return false;

        var local = token[..at];
        var domain = token[(at + 1)..];
        var dot = domain.IndexOf('.');
        return dot > 0 && dot < domain.Length - 1 &&
               IsSimpleAsciiToken(local, allowDot: true) &&
               IsSimpleAsciiToken(domain, allowDot: true);
    }

    private static bool IsHexHash(string token)
    {
        if (token.Length < 6)
            return false;

        var hasDigit = false;
        foreach (var ch in token)
        {
            if (!char.IsAsciiHexDigit(ch))
                return false;
            if (char.IsAsciiDigit(ch))
                hasDigit = true;
        }

        return hasDigit;
    }

    private static bool IsDottedIdentifierOrBareDomain(string token)
    {
        var host = token;
        var delimiterIndex = host.IndexOfAny(['/', '?', '#']);
        if (delimiterIndex > 0)
            host = host[..delimiterIndex];

        var portIndex = host.LastIndexOf(':');
        if (portIndex > 0 && int.TryParse(host[(portIndex + 1)..], out _))
            host = host[..portIndex];

        if (host.Length < 3 || host.StartsWith('.') || host.EndsWith('.'))
            return false;

        var firstDot = host.IndexOf('.');
        if (firstDot <= 0)
            return false;

        var segments = host.Split('.');
        if (segments.Length < 2)
            return false;

        var allSingleUppercase = true;
        foreach (var segment in segments)
        {
            if (segment.Length == 0 || !IsIdentifierSegment(segment))
                return false;
            if (segment.Length != 1 || !char.IsAsciiLetterUpper(segment[0]))
                allSingleUppercase = false;
        }

        return !allSingleUppercase;
    }

    private static bool IsIdentifierSegment(string segment)
    {
        foreach (var ch in segment)
        {
            if (!char.IsAsciiLetterOrDigit(ch) && ch is not ('-' or '_'))
                return false;
        }

        return true;
    }

    private static bool IsSimpleAsciiToken(string value, bool allowDot)
    {
        foreach (var ch in value)
        {
            if (char.IsAsciiLetterOrDigit(ch))
                continue;
            if (ch is '-' or '_' or '+')
                continue;
            if (allowDot && ch == '.')
                continue;
            return false;
        }

        return value.Length > 0;
    }

    private static bool ContainsWhitespace(string text)
    {
        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch))
                return true;
        }

        return false;
    }

    private static bool IsAsciiLatin(int value) =>
        (uint)(value - 'A') <= ('Z' - 'A') || (uint)(value - 'a') <= ('z' - 'a');

    private static bool IsNonAsciiLatin(int value) =>
        value is (>= 0x00C0 and <= 0x024F)
            or (>= 0x1E00 and <= 0x1EFF)
            or (>= 0x2C60 and <= 0x2C7F)
            or (>= 0xA720 and <= 0xA7FF)
            or (>= 0xAB30 and <= 0xAB6F);

    private static bool IsHan(int value) =>
        value is (>= 0x3400 and <= 0x4DBF)
            or (>= 0x4E00 and <= 0x9FFF)
            or (>= 0xF900 and <= 0xFAFF)
            or (>= 0x20000 and <= 0x2FA1F)
            or 0x3005;

    private static bool IsKana(int value) =>
        value is (>= 0x3040 and <= 0x309F)
            or (>= 0x30A0 and <= 0x30FF)
            or (>= 0x31F0 and <= 0x31FF)
            or (>= 0xFF66 and <= 0xFF9F);

    private static bool IsHangul(int value) =>
        value is (>= 0x1100 and <= 0x11FF)
            or (>= 0x3130 and <= 0x318F)
            or (>= 0xA960 and <= 0xA97F)
            or (>= 0xAC00 and <= 0xD7AF)
            or (>= 0xD7B0 and <= 0xD7FF);

    private static bool MatchesTarget(DetectedLanguage detected, TranslationTargetLanguage target) =>
        (detected, target) switch
        {
            (DetectedLanguage.Chinese, TranslationTargetLanguage.SimplifiedChinese or TranslationTargetLanguage.TraditionalChinese) => true,
            (DetectedLanguage.English, TranslationTargetLanguage.English) => true,
            (DetectedLanguage.Japanese, TranslationTargetLanguage.Japanese) => true,
            _ => false
        };

    private static bool MatchesSource(DetectedLanguage detected, TranslationSourceLanguage source) =>
        (detected, source) switch
        {
            (DetectedLanguage.Chinese, TranslationSourceLanguage.SimplifiedChinese or TranslationSourceLanguage.TraditionalChinese) => true,
            (DetectedLanguage.English, TranslationSourceLanguage.English) => true,
            (DetectedLanguage.Japanese, TranslationSourceLanguage.Japanese) => true,
            _ => false
        };

    private static bool IsSameConfiguredLanguage(
        TranslationSourceLanguage source,
        TranslationTargetLanguage target) =>
        (source, target) switch
        {
            (TranslationSourceLanguage.SimplifiedChinese, TranslationTargetLanguage.SimplifiedChinese) => true,
            (TranslationSourceLanguage.TraditionalChinese, TranslationTargetLanguage.TraditionalChinese) => true,
            (TranslationSourceLanguage.English, TranslationTargetLanguage.English) => true,
            (TranslationSourceLanguage.Japanese, TranslationTargetLanguage.Japanese) => true,
            _ => false
        };

    private static bool IsChineseConversion(
        TranslationSourceLanguage source,
        TranslationTargetLanguage target) =>
        source is TranslationSourceLanguage.SimplifiedChinese or TranslationSourceLanguage.TraditionalChinese &&
        target is TranslationTargetLanguage.SimplifiedChinese or TranslationTargetLanguage.TraditionalChinese;

    [GeneratedRegex(@"^[A-Za-z0-9-]+:\d{2,5}(?:[/?#].*)?$", RegexOptions.CultureInvariant)]
    private static partial Regex HostPortRegex();

    [GeneratedRegex(@"^#(?:[0-9A-Fa-f]{3}|[0-9A-Fa-f]{4}|[0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$", RegexOptions.CultureInvariant)]
    private static partial Regex HexColorRegex();

    [GeneratedRegex(@"^0[xX][0-9A-Fa-f]+$", RegexOptions.CultureInvariant)]
    private static partial Regex HexLiteralRegex();

    [GeneratedRegex(@"^[vV]?\d+\.\d+(?:\.\d+)*(?:[-+][A-Za-z0-9.-]+)?$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionTokenRegex();

    [GeneratedRegex(@"^net\d+\.\d+(?:-windows[0-9.]*)?$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex TargetFrameworkRegex();

    private readonly record struct RuneCounts(
        int NonWhitespace,
        int Han,
        int Kana,
        int Hangul,
        int AsciiLatin,
        int NonAsciiLatin,
        int OtherLetters)
    {
        public int TotalLetters => Han + Kana + Hangul + AsciiLatin + NonAsciiLatin + OtherLetters;
    }
}
