using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using Kitopia.Desktop.Features.Services.Config;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;

namespace Kitopia.Desktop.Features.Search.Semantic;

internal delegate int TokenCounter(ReadOnlySpan<char> text);

internal static partial class DocumentTextExtractor
{
    public static bool TryCreateSource(string path, out DocumentContentSource source)
    {
        source = default!;
        if (!File.Exists(path) || !IsSupported(path))
        {
            return false;
        }

        try
        {
            var fullPath = Path.GetFullPath(path);
            var file = new FileInfo(fullPath);
            source = new DocumentContentSource(
                fullPath,
                $"{fullPath}|{file.Length}|{file.LastWriteTimeUtc.Ticks}");
            return true;
        }
        catch (Exception exception) when (exception is IOException
                                         or UnauthorizedAccessException
                                         or NotSupportedException
                                         or ArgumentException
                                         or System.Security.SecurityException)
        {
            return false;
        }
    }

    public static async Task<DocumentContentSource?> TryComputeContentHashAsync(
        DocumentContentSource source,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(
                source.Path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 64 * 1024,
                useAsync: true);
            var contentHash = await SHA256.HashDataAsync(stream, cancellationToken);
            return source with { ContentHash = Convert.ToHexString(contentHash) };
        }
        catch (Exception exception) when (exception is IOException
                                         or UnauthorizedAccessException
                                         or NotSupportedException
                                         or ArgumentException
                                         or System.Security.SecurityException)
        {
            return null;
        }
    }

    public static async IAsyncEnumerable<string> ExtractChunksAsync(
        DocumentContentSource source,
        TokenCounter countTokens,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(source.Path);
        if (IsPlainTextExtension(extension))
        {
            await foreach (var chunk in ReadPlainTextChunksAsync(source.Path, countTokens, cancellationToken))
            {
                yield return chunk;
            }

            yield break;
        }

        if (extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var chunk in ReadPdfChunks(source.Path, countTokens, cancellationToken))
            {
                yield return chunk;
            }

            yield break;
        }

        await foreach (var chunk in ReadOpenXmlChunksAsync(source.Path, extension, countTokens, cancellationToken))
        {
            yield return chunk;
        }
    }

    private static bool IsSupported(string path)
    {
        var extension = Path.GetExtension(path);
        return IsPlainTextExtension(extension)
               || extension.Equals(".docx", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".pptx", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPlainTextExtension(string extension)
    {
        return ConfigManger.Config.plainTextExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    private static async IAsyncEnumerable<string> ReadPlainTextChunksAsync(
        string path,
        TokenCounter countTokens,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var chunker = new TextChunker(countTokens);
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 16 * 1024,
            useAsync: true);
        using var reader = new StreamReader(stream, DetectPlainTextEncoding(stream), detectEncodingFromByteOrderMarks: true);
        var buffer = new char[4096];
        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0)
            {
                break;
            }

            for (var index = 0; index < read; index++)
            {
                if (chunker.Append(buffer[index]) is { } chunk)
                {
                    yield return chunk;
                }
            }
        }

        var finalChunk = chunker.Flush();
        if (finalChunk is not null)
        {
            yield return finalChunk;
        }
    }

    private static IEnumerable<string> ReadPdfChunks(string path, TokenCounter countTokens, CancellationToken cancellationToken)
    {
        var chunker = new TextChunker(countTokens);
        using var document = TryOpenPdf(path);
        if (document is null)
        {
            yield break;
        }

        var cmapCache = new Dictionary<PdfDictionary, Dictionary<ushort, string>>(ReferenceEqualityComparer.Instance);
        foreach (var page in document.Pages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var decoder = new PdfPageTextDecoder(page, chunker, cmapCache);
            foreach (var content in page.Contents)
            {
                if (content.Stream?.UnfilteredValue is not { } contentStream)
                {
                    continue;
                }

                foreach (var chunk in decoder.Decode(contentStream))
                {
                    yield return chunk;
                }
            }

            if (chunker.Append('\n') is { } pageBreakChunk)
            {
                yield return pageBreakChunk;
            }
        }

        var finalChunk = chunker.Flush();
        if (finalChunk is not null)
        {
            yield return finalChunk;
        }
    }

    private static PdfDocument? TryOpenPdf(string path)
    {
        try
        {
            return PdfReader.Open(path, PdfDocumentOpenMode.Import);
        }
        catch (Exception exception) when (IsRecoverableDocumentFormatException(exception))
        {
            return null;
        }
    }

    private sealed class PdfPageTextDecoder
    {
        private readonly IReadOnlyDictionary<string, Dictionary<ushort, string>> _fontMaps;
        private readonly TextChunker _chunker;
        private string _activeFont = string.Empty;

        public PdfPageTextDecoder(
            PdfPage page,
            TextChunker chunker,
            Dictionary<PdfDictionary, Dictionary<ushort, string>> cmapCache)
        {
            _fontMaps = ReadPdfFontMaps(page, cmapCache);
            _chunker = chunker;
        }

        public IEnumerable<string> Decode(byte[] content)
        {
            var position = 0;
            var latestName = default(PdfContentToken);
            var pendingHexString = default(PdfContentToken);
            var pendingLiteralString = default(PdfContentToken);
            while (TryReadPdfContentToken(content, ref position, out var token))
            {
                switch (token.Kind)
                {
                    case PdfContentTokenKind.Name:
                        latestName = token;
                        pendingHexString = default;
                        pendingLiteralString = default;
                        break;

                    case PdfContentTokenKind.HexString:
                        pendingHexString = token;
                        pendingLiteralString = default;
                        break;

                    case PdfContentTokenKind.LiteralString:
                        pendingLiteralString = token;
                        pendingHexString = default;
                        break;

                    case PdfContentTokenKind.ArrayStart:
                    {
                        var arrayStart = position;
                        if (!TryFindPdfArrayEnd(content, ref position, out var arrayEnd)
                            || !TryReadPdfContentToken(content, ref position, out var operation))
                        {
                            yield break;
                        }

                        if (IsPdfToken(content, operation, "TJ"))
                        {
                            _fontMaps.TryGetValue(_activeFont, out var fontMap);
                            foreach (var chunk in DecodePdfArrayStrings(content, arrayStart, arrayEnd, fontMap))
                            {
                                yield return chunk;
                            }
                        }

                        latestName = default;
                        pendingHexString = default;
                        break;
                    }

                    case PdfContentTokenKind.Word:
                        if (IsPdfToken(content, token, "Tf") && latestName.Kind == PdfContentTokenKind.Name)
                        {
                            _activeFont = "/" + Encoding.ASCII.GetString(content, latestName.Start, latestName.Length);
                            latestName = default;
                        }
                        else if (IsPdfToken(content, token, "Tj"))
                        {
                            if (pendingHexString.Kind == PdfContentTokenKind.HexString
                                && _fontMaps.TryGetValue(_activeFont, out var fontMap))
                            {
                                foreach (var chunk in DecodePdfHexString(content, pendingHexString, fontMap))
                                {
                                    yield return chunk;
                                }
                            }
                            else if (pendingLiteralString.Kind == PdfContentTokenKind.LiteralString)
                            {
                                foreach (var chunk in DecodePdfLiteralString(content, pendingLiteralString))
                                {
                                    yield return chunk;
                                }
                            }

                            latestName = default;
                        }
                        else if (!IsPdfNumber(content, token))
                        {
                            latestName = default;
                        }

                        pendingHexString = default;
                        pendingLiteralString = default;
                        break;

                    default:
                        pendingHexString = default;
                        pendingLiteralString = default;
                        break;
                }
            }
        }

        private IEnumerable<string> DecodePdfArrayStrings(
            byte[] content,
            int start,
            int end,
            IReadOnlyDictionary<ushort, string>? fontMap)
        {
            var position = start;
            while (position < end)
            {
                if (content[position] == (byte)'(')
                {
                    var literalStart = position + 1;
                    SkipPdfLiteralString(content, ref position);
                    foreach (var chunk in DecodePdfLiteralString(
                                 content,
                                 new PdfContentToken(PdfContentTokenKind.LiteralString, literalStart, position - literalStart - 1)))
                    {
                        yield return chunk;
                    }

                    continue;
                }

                if (content[position] != (byte)'<' || position + 1 >= end || content[position + 1] == (byte)'<')
                {
                    position++;
                    continue;
                }

                var hexStart = ++position;
                while (position < end && content[position] != (byte)'>')
                {
                    position++;
                }

                if (position >= end)
                {
                    yield break;
                }

                if (fontMap is not null)
                {
                    foreach (var chunk in DecodePdfHexString(content, new PdfContentToken(PdfContentTokenKind.HexString, hexStart, position - hexStart), fontMap))
                    {
                        yield return chunk;
                    }
                }

                position++;
            }
        }

        private IEnumerable<string> DecodePdfLiteralString(byte[] content, PdfContentToken token)
        {
            var end = token.Start + token.Length;
            for (var index = token.Start; index < end; index++)
            {
                var value = content[index];
                if (value == (byte)'\\' && ++index < end)
                {
                    value = content[index];
                    if (value is (byte)'\r' or (byte)'\n')
                    {
                        if (value == (byte)'\r' && index + 1 < end && content[index + 1] == (byte)'\n')
                        {
                            index++;
                        }

                        continue;
                    }

                    if (value is >= (byte)'0' and <= (byte)'7')
                    {
                        var octalValue = value - (byte)'0';
                        for (var digits = 1; digits < 3 && index + 1 < end && content[index + 1] is >= (byte)'0' and <= (byte)'7'; digits++)
                        {
                            octalValue = (octalValue * 8) + content[++index] - (byte)'0';
                        }

                        value = (byte)octalValue;
                    }
                    else
                    {
                        value = value switch
                        {
                            (byte)'n' => (byte)'\n',
                            (byte)'r' => (byte)'\r',
                            (byte)'t' => (byte)'\t',
                            (byte)'b' => (byte)'\b',
                            (byte)'f' => (byte)'\f',
                            _ => value
                        };
                    }
                }

                if (_chunker.Append((char)value) is { } chunk)
                {
                    yield return chunk;
                }
            }
        }

        private IEnumerable<string> DecodePdfHexString(
            byte[] content,
            PdfContentToken token,
            IReadOnlyDictionary<ushort, string> fontMap)
        {
            var nibbleCount = 0;
            for (var index = token.Start; index < token.Start + token.Length; index++)
            {
                if (IsPdfWhitespace(content[index]))
                {
                    continue;
                }

                if (GetHexValue(content[index]) < 0)
                {
                    yield break;
                }

                nibbleCount++;
            }

            if (nibbleCount == 0 || nibbleCount % 4 != 0)
            {
                yield break;
            }

            var glyph = 0;
            var glyphNibbleCount = 0;
            for (var index = token.Start; index < token.Start + token.Length; index++)
            {
                var value = GetHexValue(content[index]);
                if (value < 0)
                {
                    continue;
                }

                glyph = (glyph << 4) | value;
                glyphNibbleCount++;
                if (glyphNibbleCount < 4)
                {
                    continue;
                }

                if (fontMap.TryGetValue((ushort)glyph, out var text))
                {
                    foreach (var character in text)
                    {
                        if (_chunker.Append(character) is { } chunk)
                        {
                            yield return chunk;
                        }
                    }
                }

                glyph = 0;
                glyphNibbleCount = 0;
            }
        }
    }

    private static Dictionary<string, Dictionary<ushort, string>> ReadPdfFontMaps(
        PdfPage page,
        Dictionary<PdfDictionary, Dictionary<ushort, string>> cmapCache)
    {
        var fontMaps = new Dictionary<string, Dictionary<ushort, string>>(StringComparer.Ordinal);
        var fonts = page.Resources?.Elements.GetDictionary("/Font");
        if (fonts is null)
        {
            return fontMaps;
        }

        foreach (var (fontName, item) in fonts)
        {
            var fontItem = item is PdfReference reference ? reference.Value : item;
            if (fontItem is not PdfDictionary font
                || font.Elements.GetReference("/ToUnicode")?.Value is not PdfDictionary cmap
                || cmap.Stream?.UnfilteredValue is not { } cmapStream)
            {
                continue;
            }

            if (!cmapCache.TryGetValue(cmap, out var map))
            {
                if (cmapCache.Count == 16)
                {
                    cmapCache.Clear();
                }

                map = ReadCMap(cmapStream);
                cmapCache[cmap] = map;
            }

            if (map.Count > 0)
            {
                fontMaps[fontName] = map;
            }
        }

        return fontMaps;
    }

    private static Dictionary<ushort, string> ReadCMap(byte[] content)
    {
        var map = new Dictionary<ushort, string>();
        var position = 0;
        var block = CMapBlock.None;
        while (TryReadCMapToken(content, ref position, out var token))
        {
            if (token.Kind == CMapTokenKind.Word)
            {
                if (IsCMapToken(content, token, "beginbfchar"))
                {
                    block = CMapBlock.Characters;
                    continue;
                }

                if (IsCMapToken(content, token, "beginbfrange"))
                {
                    block = CMapBlock.Ranges;
                    continue;
                }

                if (IsCMapToken(content, token, "endbfchar") || IsCMapToken(content, token, "endbfrange"))
                {
                    block = CMapBlock.None;
                    continue;
                }
            }

            if (block == CMapBlock.Characters && token.Kind == CMapTokenKind.HexString)
            {
                if (TryReadCMapToken(content, ref position, out var target)
                    && target.Kind == CMapTokenKind.HexString
                    && TryParseCMapCode(content, token, out var source))
                {
                    map[source] = DecodeCMapValue(content, target);
                }

                continue;
            }

            if (block != CMapBlock.Ranges || token.Kind != CMapTokenKind.HexString
                || !TryParseCMapCode(content, token, out var rangeStart)
                || !TryReadCMapToken(content, ref position, out var rangeEndToken)
                || !TryParseCMapCode(content, rangeEndToken, out var rangeEnd)
                || !TryReadCMapToken(content, ref position, out var targetToken))
            {
                continue;
            }

            if (targetToken.Kind == CMapTokenKind.HexString)
            {
                AddCMapRange(map, rangeStart, rangeEnd, DecodeCMapValue(content, targetToken));
                continue;
            }

            if (targetToken.Kind != CMapTokenKind.ArrayStart)
            {
                continue;
            }

            for (var source = (int)rangeStart; source <= rangeEnd; source++)
            {
                if (!TryReadCMapToken(content, ref position, out var value) || value.Kind == CMapTokenKind.ArrayEnd)
                {
                    break;
                }

                if (value.Kind == CMapTokenKind.HexString)
                {
                    map[(ushort)source] = DecodeCMapValue(content, value);
                }
            }
        }

        return map;
    }

    private static bool TryReadCMapToken(byte[] content, ref int position, out CMapToken token)
    {
        SkipPdfWhitespaceAndComments(content, ref position);
        if (position >= content.Length)
        {
            token = default;
            return false;
        }

        if (content[position] == (byte)'<')
        {
            if (position + 1 < content.Length && content[position + 1] == (byte)'<')
            {
                position += 2;
                token = new CMapToken(CMapTokenKind.Other, 0, 0);
                return true;
            }

            var start = ++position;
            while (position < content.Length && content[position] != (byte)'>')
            {
                position++;
            }

            token = new CMapToken(CMapTokenKind.HexString, start, position - start);
            if (position < content.Length)
            {
                position++;
            }

            return true;
        }

        if (content[position] == (byte)'[')
        {
            position++;
            token = new CMapToken(CMapTokenKind.ArrayStart, 0, 0);
            return true;
        }

        if (content[position] == (byte)']')
        {
            position++;
            token = new CMapToken(CMapTokenKind.ArrayEnd, 0, 0);
            return true;
        }

        if (IsPdfDelimiter(content[position]))
        {
            position++;
            token = new CMapToken(CMapTokenKind.Other, 0, 0);
            return true;
        }

        var wordStart = position;
        while (position < content.Length && !IsPdfDelimiter(content[position]))
        {
            position++;
        }

        token = new CMapToken(CMapTokenKind.Word, wordStart, position - wordStart);
        return true;
    }

    private static bool IsCMapToken(byte[] content, CMapToken token, string value)
    {
        if (token.Length != value.Length)
        {
            return false;
        }

        for (var index = 0; index < value.Length; index++)
        {
            var character = content[token.Start + index];
            if (character is >= (byte)'A' and <= (byte)'Z')
            {
                character |= 0x20;
            }

            if (character != (byte)value[index])
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryParseCMapCode(byte[] content, CMapToken token, out ushort value)
    {
        value = 0;
        if (token.Kind != CMapTokenKind.HexString || token.Length != 4)
        {
            return false;
        }

        for (var index = 0; index < token.Length; index++)
        {
            var hex = GetHexValue(content[token.Start + index]);
            if (hex < 0)
            {
                return false;
            }

            value = (ushort)((value << 4) | hex);
        }

        return true;
    }

    private static bool TryReadPdfContentToken(byte[] content, ref int position, out PdfContentToken token)
    {
        SkipPdfWhitespaceAndComments(content, ref position);
        if (position >= content.Length)
        {
            token = default;
            return false;
        }

        switch (content[position])
        {
            case (byte)'/':
            {
                var start = ++position;
                while (position < content.Length && !IsPdfDelimiter(content[position]))
                {
                    position++;
                }

                token = new PdfContentToken(PdfContentTokenKind.Name, start, position - start);
                return true;
            }

            case (byte)'<':
            {
                if (position + 1 < content.Length && content[position + 1] == (byte)'<')
                {
                    SkipPdfDictionary(content, ref position);
                    token = new PdfContentToken(PdfContentTokenKind.Other, 0, 0);
                    return true;
                }

                var start = ++position;
                while (position < content.Length && content[position] != (byte)'>')
                {
                    position++;
                }

                token = new PdfContentToken(PdfContentTokenKind.HexString, start, position - start);
                if (position < content.Length)
                {
                    position++;
                }

                return true;
            }

            case (byte)'[':
                position++;
                token = new PdfContentToken(PdfContentTokenKind.ArrayStart, 0, 0);
                return true;

            case (byte)'(':
            {
                var start = position + 1;
                SkipPdfLiteralString(content, ref position);
                token = new PdfContentToken(PdfContentTokenKind.LiteralString, start, position - start - 1);
                return true;
            }

            default:
            {
                var start = position;
                while (position < content.Length && !IsPdfDelimiter(content[position]))
                {
                    position++;
                }

                if (start == position)
                {
                    position++;
                    token = new PdfContentToken(PdfContentTokenKind.Other, 0, 0);
                    return true;
                }

                token = new PdfContentToken(PdfContentTokenKind.Word, start, position - start);
                return true;
            }
        }
    }

    private static bool TryFindPdfArrayEnd(byte[] content, ref int position, out int end)
    {
        var depth = 1;
        while (position < content.Length)
        {
            switch (content[position])
            {
                case (byte)'%':
                    SkipPdfComment(content, ref position);
                    break;

                case (byte)'(':
                    SkipPdfLiteralString(content, ref position);
                    break;

                case (byte)'<':
                    if (position + 1 < content.Length && content[position + 1] == (byte)'<')
                    {
                        SkipPdfDictionary(content, ref position);
                    }
                    else
                    {
                        SkipPdfHexString(content, ref position);
                    }

                    break;

                case (byte)'[':
                    depth++;
                    position++;
                    break;

                case (byte)']':
                    if (--depth == 0)
                    {
                        end = position;
                        position++;
                        return true;
                    }

                    position++;
                    break;

                default:
                    position++;
                    break;
            }
        }

        end = 0;
        return false;
    }

    private static void SkipPdfWhitespaceAndComments(byte[] content, ref int position)
    {
        while (position < content.Length)
        {
            if (IsPdfWhitespace(content[position]))
            {
                position++;
                continue;
            }

            if (content[position] == (byte)'%')
            {
                SkipPdfComment(content, ref position);
                continue;
            }

            break;
        }
    }

    private static void SkipPdfComment(byte[] content, ref int position)
    {
        while (position < content.Length && content[position] is not (byte)'\r' and not (byte)'\n')
        {
            position++;
        }
    }

    private static void SkipPdfHexString(byte[] content, ref int position)
    {
        position++;
        while (position < content.Length && content[position] != (byte)'>')
        {
            position++;
        }

        if (position < content.Length)
        {
            position++;
        }
    }

    private static void SkipPdfDictionary(byte[] content, ref int position)
    {
        var depth = 0;
        while (position < content.Length)
        {
            if (content[position] == (byte)'<' && position + 1 < content.Length && content[position + 1] == (byte)'<')
            {
                depth++;
                position += 2;
                continue;
            }

            if (content[position] == (byte)'>' && position + 1 < content.Length && content[position + 1] == (byte)'>')
            {
                depth--;
                position += 2;
                if (depth == 0)
                {
                    return;
                }

                continue;
            }

            if (content[position] == (byte)'(')
            {
                SkipPdfLiteralString(content, ref position);
                continue;
            }

            position++;
        }
    }

    private static void SkipPdfLiteralString(byte[] content, ref int position)
    {
        var depth = 0;
        while (position < content.Length)
        {
            if (content[position] == (byte)'\\')
            {
                position += Math.Min(2, content.Length - position);
                continue;
            }

            if (content[position] == (byte)'(')
            {
                depth++;
            }
            else if (content[position] == (byte)')' && --depth == 0)
            {
                position++;
                return;
            }

            position++;
        }
    }

    private static bool IsPdfWhitespace(byte value)
    {
        return value is 0 or 9 or 10 or 12 or 13 or 32;
    }

    private static bool IsPdfDelimiter(byte value)
    {
        return IsPdfWhitespace(value) || value is (byte)'(' or (byte)')' or (byte)'<' or (byte)'>' or (byte)'[' or (byte)']' or (byte)'{' or (byte)'}' or (byte)'/' or (byte)'%';
    }

    private static int GetHexValue(byte value)
    {
        return value switch
        {
            >= (byte)'0' and <= (byte)'9' => value - (byte)'0',
            >= (byte)'A' and <= (byte)'F' => value - (byte)'A' + 10,
            >= (byte)'a' and <= (byte)'f' => value - (byte)'a' + 10,
            _ => -1
        };
    }

    private static bool IsPdfToken(byte[] content, PdfContentToken token, string value)
    {
        if (token.Length != value.Length)
        {
            return false;
        }

        for (var index = 0; index < value.Length; index++)
        {
            if (content[token.Start + index] != (byte)value[index])
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsPdfNumber(byte[] content, PdfContentToken token)
    {
        if (token.Length == 0)
        {
            return false;
        }

        for (var index = 0; index < token.Length; index++)
        {
            var value = content[token.Start + index];
            if (value is not ((byte)'+' or (byte)'-' or (byte)'.') && (value < (byte)'0' || value > (byte)'9'))
            {
                return false;
            }
        }

        return true;
    }

    private enum PdfContentTokenKind
    {
        Other,
        Name,
        HexString,
        LiteralString,
        ArrayStart,
        Word
    }

    private readonly record struct PdfContentToken(PdfContentTokenKind Kind, int Start, int Length);

    private enum CMapBlock
    {
        None,
        Characters,
        Ranges
    }

    private enum CMapTokenKind
    {
        Other,
        Word,
        HexString,
        ArrayStart,
        ArrayEnd
    }

    private readonly record struct CMapToken(CMapTokenKind Kind, int Start, int Length);

    private static void AddCMapRange(Dictionary<ushort, string> map, ushort start, ushort end, string target)
    {
        if (start == end)
        {
            map[start] = target;
            return;
        }

        if (!TryGetUnicodeScalar(target, out var targetScalar))
        {
            return;
        }

        for (var source = (int)start; source <= end; source++)
        {
            var scalar = targetScalar + source - start;
            if (scalar > 0x10FFFF || scalar is >= 0xD800 and <= 0xDFFF)
            {
                return;
            }

            map[(ushort)source] = char.ConvertFromUtf32(scalar);
        }
    }

    private static string DecodeCMapValue(byte[] content, CMapToken token)
    {
        var builder = new StringBuilder(token.Length / 4);
        var codeUnit = 0;
        var nibbleCount = 0;
        for (var index = 0; index < token.Length; index++)
        {
            var nibble = GetHexValue(content[token.Start + index]);
            if (nibble < 0)
            {
                if (IsPdfWhitespace(content[token.Start + index]))
                {
                    continue;
                }

                return string.Empty;
            }

            codeUnit = (codeUnit << 4) | nibble;
            if (++nibbleCount != 4)
            {
                continue;
            }

            builder.Append((char)codeUnit);
            codeUnit = 0;
            nibbleCount = 0;
        }

        return nibbleCount == 0 ? builder.ToString() : string.Empty;
    }

    private static bool TryGetUnicodeScalar(string value, out int scalar)
    {
        scalar = 0;
        if (value.Length == 1 && !char.IsSurrogate(value[0]))
        {
            scalar = value[0];
            return true;
        }

        if (value.Length == 2 && char.IsHighSurrogate(value[0]) && char.IsLowSurrogate(value[1]))
        {
            scalar = char.ConvertToUtf32(value[0], value[1]);
            return true;
        }

        return false;
    }

    private static async IAsyncEnumerable<string> ReadOpenXmlChunksAsync(
        string path,
        string extension,
        TokenCounter countTokens,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var archive = TryOpenArchive(path);
        if (archive is null)
        {
            yield break;
        }

        var entries = extension.ToLowerInvariant() switch
        {
            ".docx" => archive.Entries.Where(entry => entry.FullName.Equals("word/document.xml", StringComparison.OrdinalIgnoreCase)
                                                        || entry.FullName.StartsWith("word/header", StringComparison.OrdinalIgnoreCase)
                                                        || entry.FullName.StartsWith("word/footer", StringComparison.OrdinalIgnoreCase)),
            ".xlsx" => archive.Entries.Where(entry => entry.FullName.Equals("xl/sharedStrings.xml", StringComparison.OrdinalIgnoreCase)
                                                        || (entry.FullName.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase)
                                                            && entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))),
            ".pptx" => archive.Entries.Where(entry => entry.FullName.StartsWith("ppt/slides/slide", StringComparison.OrdinalIgnoreCase)
                                                        && entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)),
            _ => []
        };

        var chunker = new TextChunker(countTokens);
        var textBuffer = new char[4096];
        foreach (var entry in entries.OrderBy(entry => entry.FullName, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var chunkerCheckpoint = chunker.CreateCheckpoint();
            var entryProducedChunk = false;
            var retryWithTolerantReader = false;
            await using (var entryEnumerator = ReadOpenXmlEntryChunksAsync(
                             entry,
                             chunker,
                             textBuffer,
                             cancellationToken).GetAsyncEnumerator(cancellationToken))
            {
                while (true)
                {
                    bool hasNext;
                    try
                    {
                        hasNext = await entryEnumerator.MoveNextAsync();
                    }
                    catch (XmlException)
                    {
                        retryWithTolerantReader = !entryProducedChunk;
                        break;
                    }
                    catch (Exception exception) when (IsRecoverableDocumentFormatException(exception))
                    {
                        break;
                    }

                    if (!hasNext)
                    {
                        break;
                    }

                    entryProducedChunk = true;
                    yield return entryEnumerator.Current;
                }
            }

            if (retryWithTolerantReader)
            {
                chunker.RestoreCheckpoint(chunkerCheckpoint);
                using var tolerantEnumerator = ReadOpenXmlEntryChunksTolerantly(
                    entry,
                    chunker,
                    cancellationToken).GetEnumerator();
                while (true)
                {
                    bool hasNext;
                    try
                    {
                        hasNext = tolerantEnumerator.MoveNext();
                    }
                    catch (Exception exception) when (IsRecoverableDocumentFormatException(exception))
                    {
                        break;
                    }

                    if (!hasNext)
                    {
                        break;
                    }

                    yield return tolerantEnumerator.Current;
                }
            }
        }

        var finalChunk = chunker.Flush();
        if (finalChunk is not null)
        {
            yield return finalChunk;
        }
    }

    private static ZipArchive? TryOpenArchive(string path)
    {
        try
        {
            return ZipFile.OpenRead(path);
        }
        catch (Exception exception) when (IsRecoverableDocumentFormatException(exception))
        {
            return null;
        }
    }

    private static async IAsyncEnumerable<string> ReadOpenXmlEntryChunksAsync(
        ZipArchiveEntry entry,
        TextChunker chunker,
        char[] textBuffer,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var stream = entry.Open();
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            Async = true,
            DtdProcessing = DtdProcessing.Prohibit,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true
        });

        while (await reader.ReadAsync())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reader.NodeType != XmlNodeType.Element)
            {
                continue;
            }

            if (reader.LocalName is "p" or "br" or "tab"
                && chunker.Append('\n') is { } paragraphChunk)
            {
                yield return paragraphChunk;
            }

            if (reader.LocalName is not ("t" or "instrText") || reader.IsEmptyElement)
            {
                continue;
            }

            while (await reader.ReadAsync())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (reader.NodeType == XmlNodeType.EndElement)
                {
                    break;
                }

                if (reader.NodeType is not (XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace))
                {
                    continue;
                }

                int read;
                while ((read = await reader.ReadValueChunkAsync(textBuffer, 0, textBuffer.Length)) > 0)
                {
                    for (var index = 0; index < read; index++)
                    {
                        if (chunker.Append(textBuffer[index]) is { } chunk)
                        {
                            yield return chunk;
                        }
                    }
                }
            }
        }
    }

    private static IEnumerable<string> ReadOpenXmlEntryChunksTolerantly(
        ZipArchiveEntry entry,
        TextChunker chunker,
        CancellationToken cancellationToken)
    {
        using var stream = entry.Open();
        using var reader = new XmlTextReader(stream)
        {
            Namespaces = false,
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            WhitespaceHandling = WhitespaceHandling.All
        };

        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reader.NodeType != XmlNodeType.Element)
            {
                continue;
            }

            var localName = GetXmlLocalName(reader);
            if (localName is "p" or "br" or "tab"
                && chunker.Append('\n') is { } paragraphChunk)
            {
                yield return paragraphChunk;
            }

            if (localName is not ("t" or "instrText") || reader.IsEmptyElement)
            {
                continue;
            }

            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (reader.NodeType == XmlNodeType.EndElement)
                {
                    break;
                }

                if (reader.NodeType is not (XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace))
                {
                    continue;
                }

                var value = reader.Value;
                for (var index = 0; index < value.Length; index++)
                {
                    if (chunker.Append(value[index]) is { } chunk)
                    {
                        yield return chunk;
                    }
                }
            }
        }
    }

    private static string GetXmlLocalName(XmlReader reader)
    {
        var name = reader.LocalName;
        var separator = name.LastIndexOf(':');
        return separator >= 0 ? name[(separator + 1)..] : name;
    }

    internal static bool IsRecoverableDocumentFormatException(Exception exception)
    {
        return exception is InvalidDataException
            or InvalidOperationException
            or IOException
            or PdfReaderException
            or XmlException;
    }

    private static Encoding DetectPlainTextEncoding(FileStream stream)
    {
        Span<byte> prefix = stackalloc byte[4];
        var read = stream.Read(prefix);
        stream.Position = 0;
        if (read >= 3 && prefix[0] == 0xEF && prefix[1] == 0xBB && prefix[2] == 0xBF)
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        }

        if (read >= 2 && prefix[0] == 0xFF && prefix[1] == 0xFE)
        {
            return Encoding.Unicode;
        }

        if (read >= 2 && prefix[0] == 0xFE && prefix[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode;
        }

        var sampleLength = (int)Math.Min(stream.Length, 16 * 1024);
        var sample = new byte[sampleLength];
        if (sampleLength > 0)
        {
            stream.ReadExactly(sample);
            stream.Position = 0;
        }

        if (IsValidUtf8(sample, sampleLength < stream.Length))
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        }

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding("GB18030");
    }

    private static bool IsValidUtf8(ReadOnlySpan<byte> bytes, bool allowIncompleteTrailingSequence)
    {
        for (var index = 0; index < bytes.Length; index++)
        {
            var first = bytes[index];
            if (first <= 0x7F)
            {
                continue;
            }

            var sequenceLength = first switch
            {
                >= 0xC2 and <= 0xDF => 2,
                >= 0xE0 and <= 0xEF => 3,
                >= 0xF0 and <= 0xF4 => 4,
                _ => 0
            };
            if (sequenceLength == 0)
            {
                return false;
            }

            if (index + sequenceLength > bytes.Length)
            {
                return allowIncompleteTrailingSequence;
            }

            var second = bytes[index + 1];
            if (!IsContinuationByte(second)
                || first == 0xE0 && second < 0xA0
                || first == 0xED && second >= 0xA0
                || first == 0xF0 && second < 0x90
                || first == 0xF4 && second > 0x8F)
            {
                return false;
            }

            for (var continuationIndex = 2; continuationIndex < sequenceLength; continuationIndex++)
            {
                if (!IsContinuationByte(bytes[index + continuationIndex]))
                {
                    return false;
                }
            }

            index += sequenceLength - 1;
        }

        return true;
    }

    private static bool IsContinuationByte(byte value)
    {
        return value is >= 0x80 and <= 0xBF;
    }

    private sealed class TextChunker
    {
        // The BGE input adds [CLS] and [SEP], leaving 254 payload WordPiece tokens.
        // DocumentMaximumTokens is 256. The BGE sequence reserves [CLS] and [SEP].
        private const int MaximumPayloadTokens = 254;
        private const int OverlapTokens = 48;
        private const int MaximumBufferedCharacters = 16 * 1024;
        private const int ForcedOverlapCharacters = 512;
        private readonly TokenCounter _countTokens;
        private readonly StringBuilder _text = new();
        private char[] _tokenBuffer = [];
        private bool _previousWasWhitespace;
        private int _nextTokenCheckLength = MaximumPayloadTokens;

        public TextChunker(TokenCounter countTokens)
        {
            _countTokens = countTokens;
        }

        public string? Append(char character)
        {
            if (char.IsWhiteSpace(character))
            {
                if (!_previousWasWhitespace)
                {
                    _text.Append(' ');
                    _previousWasWhitespace = true;
                }
            }
            else if (!char.IsControl(character))
            {
                _text.Append(character);
                _previousWasWhitespace = false;
            }

            if (_text.Length < _nextTokenCheckLength)
            {
                return null;
            }

            var tokenCount = CountTokens(0, _text.Length);
            if (tokenCount >= MaximumPayloadTokens)
            {
                return TakeChunk(forceCharacterLimit: false);
            }

            if (_text.Length >= MaximumBufferedCharacters)
            {
                return TakeChunk(forceCharacterLimit: true);
            }

            // One Chinese character can be one token. Check at the earliest character
            // position that could reach the payload limit.
            _nextTokenCheckLength = Math.Min(
                MaximumBufferedCharacters,
                _text.Length + Math.Max(1, MaximumPayloadTokens - tokenCount));
            return null;
        }

        public string? Flush()
        {
            var start = FindTrimmedStart(0, _text.Length);
            var end = FindTrimmedEnd(start, _text.Length);
            var value = start == end ? null : _text.ToString(start, end - start);
            _text.Clear();
            return value;
        }

        public TextChunkerCheckpoint CreateCheckpoint()
        {
            return new TextChunkerCheckpoint(_text.Length, _previousWasWhitespace, _nextTokenCheckLength);
        }

        public void RestoreCheckpoint(TextChunkerCheckpoint checkpoint)
        {
            _text.Length = checkpoint.Length;
            _previousWasWhitespace = checkpoint.PreviousWasWhitespace;
            _nextTokenCheckLength = checkpoint.NextTokenCheckLength;
        }

        private string TakeChunk(bool forceCharacterLimit)
        {
            var breakIndex = forceCharacterLimit ? FindForcedBreakIndex() : FindBreakIndex();
            var chunkStart = FindTrimmedStart(0, breakIndex);
            var chunkEnd = FindTrimmedEnd(chunkStart, breakIndex);
            var chunk = _text.ToString(chunkStart, chunkEnd - chunkStart);
            var overlapStart = forceCharacterLimit
                ? FindForcedOverlapStart(chunk)
                : FindOverlapStart(chunk);

            _text.Remove(0, chunkStart + overlapStart);
            var remainderStart = FindTrimmedStart(0, _text.Length);
            if (remainderStart > 0)
            {
                _text.Remove(0, remainderStart);
            }

            _previousWasWhitespace = _text.Length > 0 && char.IsWhiteSpace(_text[^1]);
            var tokenCount = CountTokens(0, _text.Length);
            _nextTokenCheckLength = Math.Min(
                MaximumBufferedCharacters,
                _text.Length + Math.Max(1, MaximumPayloadTokens - tokenCount));
            return chunk;
        }

        private int FindBreakIndex()
        {
            var breakIndex = FindMaximumPrefixLength();
            var preferredStart = Math.Max(0, breakIndex - 48);
            for (var index = breakIndex - 1; index >= preferredStart; index--)
            {
                if (IsBreakCharacter(_text[index]))
                {
                    return index + 1;
                }
            }

            return breakIndex;
        }

        private int FindForcedBreakIndex()
        {
            var breakIndex = Math.Min(MaximumBufferedCharacters, _text.Length);
            var preferredStart = Math.Max(0, breakIndex - ForcedOverlapCharacters);
            for (var index = breakIndex - 1; index >= preferredStart; index--)
            {
                if (IsBreakCharacter(_text[index]))
                {
                    return index + 1;
                }
            }

            return breakIndex;
        }

        private int FindMaximumPrefixLength()
        {
            var low = 1;
            var high = _text.Length;
            while (low < high)
            {
                var middle = low + (high - low + 1) / 2;
                if (CountTokens(0, middle) <= MaximumPayloadTokens)
                {
                    low = middle;
                }
                else
                {
                    high = middle - 1;
                }
            }

            return low;
        }

        private static int FindForcedOverlapStart(string chunk)
        {
            var start = Math.Max(0, chunk.Length - ForcedOverlapCharacters);
            for (var index = start; index < chunk.Length; index++)
            {
                if (char.IsWhiteSpace(chunk[index]) && index + 1 < chunk.Length)
                {
                    return index + 1;
                }
            }

            return start;
        }

        private int FindOverlapStart(string chunk)
        {
            var low = 0;
            var high = chunk.Length;
            while (low < high)
            {
                var middle = low + (high - low) / 2;
                if (_countTokens(chunk.AsSpan(middle)) > OverlapTokens)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle;
                }
            }

            for (var index = low; index < chunk.Length; index++)
            {
                if (char.IsWhiteSpace(chunk[index]) && index + 1 < chunk.Length)
                {
                    return index + 1;
                }
            }

            return low;
        }

        private int CountTokens(int start, int length)
        {
            if (_tokenBuffer.Length < length)
            {
                _tokenBuffer = new char[length];
            }

            _text.CopyTo(start, _tokenBuffer, 0, length);
            return _countTokens(_tokenBuffer.AsSpan(0, length));
        }

        private int FindTrimmedStart(int start, int end)
        {
            while (start < end && char.IsWhiteSpace(_text[start]))
            {
                start++;
            }

            return start;
        }

        private int FindTrimmedEnd(int start, int end)
        {
            while (end > start && char.IsWhiteSpace(_text[end - 1]))
            {
                end--;
            }

            return end;
        }

        private static bool IsBreakCharacter(char character)
        {
            return char.IsWhiteSpace(character)
                   || character is '.' or '!' or '?' or '\u3002' or '\uFF01' or '\uFF1F';
        }

        public readonly record struct TextChunkerCheckpoint(
            int Length,
            bool PreviousWasWhitespace,
            int NextTokenCheckLength);
    }
}

internal sealed record DocumentContentSource(
    string Path,
    string SourceFingerprint,
    string? ContentHash = null);
