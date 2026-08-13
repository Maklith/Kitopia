using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;

namespace Kitopia.Desktop.Features.Search.Semantic;

internal static partial class DocumentTextExtractor
{
    private static readonly HashSet<string> PlainTextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".md"
    };

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
        Func<string, int> countTokens,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(source.Path);
        if (PlainTextExtensions.Contains(extension))
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
        return PlainTextExtensions.Contains(extension)
               || extension.Equals(".docx", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".pptx", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase);
    }

    private static async IAsyncEnumerable<string> ReadPlainTextChunksAsync(
        string path,
        Func<string, int> countTokens,
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

            foreach (var chunk in chunker.Append(new string(buffer, 0, read)))
            {
                yield return chunk;
            }
        }

        var finalChunk = chunker.Flush();
        if (finalChunk is not null)
        {
            yield return finalChunk;
        }
    }

    private static IEnumerable<string> ReadPdfChunks(string path, Func<string, int> countTokens, CancellationToken cancellationToken)
    {
        var chunker = new TextChunker(countTokens);
        using var document = PdfReader.Open(path, PdfDocumentOpenMode.Import);
        foreach (var page in document.Pages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var chunk in chunker.Append(DecodePdfPageText(page)))
            {
                yield return chunk;
            }
        }

        var finalChunk = chunker.Flush();
        if (finalChunk is not null)
        {
            yield return finalChunk;
        }
    }

    private static string DecodePdfPageText(PdfPage page)
    {
        var fontMaps = ReadPdfFontMaps(page);
        if (fontMaps.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        var activeFont = string.Empty;
        foreach (var content in page.Contents)
        {
            if (content.Stream?.UnfilteredValue is not { } stream)
            {
                continue;
            }

            var contentStream = Encoding.ASCII.GetString(stream);
            foreach (Match match in PdfTextOperatorPattern().Matches(contentStream))
            {
                if (match.Groups[1].Success)
                {
                    activeFont = "/" + match.Groups[1].Value;
                    continue;
                }

                if (!fontMaps.TryGetValue(activeFont, out var fontMap))
                {
                    continue;
                }

                if (match.Groups[2].Success)
                {
                    AppendDecodedGlyphs(builder, match.Groups[2].Value, fontMap);
                }
                else
                {
                    foreach (Match glyphs in PdfHexStringPattern().Matches(match.Groups[3].Value))
                    {
                        AppendDecodedGlyphs(builder, glyphs.Groups[1].Value, fontMap);
                    }
                }
            }
        }

        return builder.Append('\n').ToString();
    }

    private static Dictionary<string, Dictionary<ushort, string>> ReadPdfFontMaps(PdfPage page)
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

            var map = new Dictionary<ushort, string>();
            var cmapText = Encoding.ASCII.GetString(cmapStream);
            foreach (Match block in CMapCharacterBlockPattern().Matches(cmapText))
            {
                foreach (Match match in CMapCharacterPattern().Matches(block.Groups[1].Value))
                {
                    map[Convert.ToUInt16(match.Groups[1].Value, 16)] = DecodeCMapValue(match.Groups[2].Value);
                }
            }

            foreach (Match block in CMapRangeBlockPattern().Matches(cmapText))
            {
                var entries = block.Groups[1].Value;
                foreach (Match match in CMapRangeArrayPattern().Matches(entries))
                {
                    var start = Convert.ToUInt16(match.Groups[1].Value, 16);
                    var end = Convert.ToUInt16(match.Groups[2].Value, 16);
                    var targets = PdfHexStringPattern().Matches(match.Groups[3].Value);
                    for (var source = (int)start; source <= end && source - start < targets.Count; source++)
                    {
                        map[(ushort)source] = DecodeCMapValue(targets[source - start].Groups[1].Value);
                    }
                }

                foreach (Match match in CMapRangePattern().Matches(entries))
                {
                    AddCMapRange(
                        map,
                        Convert.ToUInt16(match.Groups[1].Value, 16),
                        Convert.ToUInt16(match.Groups[2].Value, 16),
                        match.Groups[3].Value);
                }
            }

            if (map.Count > 0)
            {
                fontMaps[fontName] = map;
            }
        }

        return fontMaps;
    }

    private static void AppendDecodedGlyphs(StringBuilder builder, string hex, IReadOnlyDictionary<ushort, string> map)
    {
        if (hex.Length % 4 != 0)
        {
            return;
        }

        for (var index = 0; index < hex.Length; index += 4)
        {
            var glyph = Convert.ToUInt16(hex.Substring(index, 4), 16);
            if (map.TryGetValue(glyph, out var character))
            {
                builder.Append(character);
            }
        }
    }

    private static void AddCMapRange(Dictionary<ushort, string> map, ushort start, ushort end, string targetHex)
    {
        var target = DecodeCMapValue(targetHex);
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

    private static string DecodeCMapValue(string hex)
    {
        return Encoding.BigEndianUnicode.GetString(Convert.FromHexString(hex));
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

    [GeneratedRegex(@"/(\w+)\s+[-+.\d]+\s+Tf|<([0-9A-F]+)>\s*Tj|\[([^]]*)\]\s*TJ", RegexOptions.IgnoreCase)]
    private static partial Regex PdfTextOperatorPattern();

    [GeneratedRegex(@"<([0-9A-F]+)>", RegexOptions.IgnoreCase)]
    private static partial Regex PdfHexStringPattern();

    [GeneratedRegex(@"\d+\s+beginbfchar\s*(.*?)\s*endbfchar", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex CMapCharacterBlockPattern();

    [GeneratedRegex(@"<([0-9A-F]{4})>\s+<([0-9A-F]+)>", RegexOptions.IgnoreCase)]
    private static partial Regex CMapCharacterPattern();

    [GeneratedRegex(@"\d+\s+beginbfrange\s*(.*?)\s*endbfrange", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex CMapRangeBlockPattern();

    [GeneratedRegex(@"<([0-9A-F]{4})>\s+<([0-9A-F]{4})>\s+<([0-9A-F]+)>", RegexOptions.IgnoreCase)]
    private static partial Regex CMapRangePattern();

    [GeneratedRegex(@"<([0-9A-F]{4})>\s+<([0-9A-F]{4})>\s+\[((?:\s*<[0-9A-F]+>\s*)+)\]", RegexOptions.IgnoreCase)]
    private static partial Regex CMapRangeArrayPattern();

    private static async IAsyncEnumerable<string> ReadOpenXmlChunksAsync(
        string path,
        string extension,
        Func<string, int> countTokens,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(path);
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
        foreach (var entry in entries.OrderBy(entry => entry.FullName, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
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
                if (reader.NodeType == XmlNodeType.Element)
                {
                    if (reader.LocalName is "p" or "br" or "tab")
                    {
                        foreach (var chunk in chunker.Append("\n"))
                        {
                            yield return chunk;
                        }
                    }

                    if (reader.LocalName is "t" or "instrText")
                    {
                        var text = await reader.ReadElementContentAsStringAsync();
                        foreach (var chunk in chunker.Append(text))
                        {
                            yield return chunk;
                        }
                    }

                    continue;
                }
            }
        }

        var finalChunk = chunker.Flush();
        if (finalChunk is not null)
        {
            yield return finalChunk;
        }
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
        private readonly Func<string, int> _countTokens;
        private readonly StringBuilder _text = new();
        private bool _previousWasWhitespace;
        private int _nextTokenCheckLength = MaximumPayloadTokens;

        public TextChunker(Func<string, int> countTokens)
        {
            _countTokens = countTokens;
        }

        public IEnumerable<string> Append(string value)
        {
            foreach (var character in value)
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
                    continue;
                }

                if (CountTokens() > MaximumPayloadTokens)
                {
                    yield return TakeChunk();
                }
                else
                {
                    // A Chinese character can be one token, so the first check is at 254 chars.
                    // After that, grow the interval to avoid copying and tokenizing the complete
                    // buffer once per appended character for ordinary multi-character words.
                    _nextTokenCheckLength = _text.Length + Math.Max(64, _text.Length / 2);
                }
            }
        }

        public string? Flush()
        {
            var value = _text.ToString().Trim();
            _text.Clear();
            return value.Length == 0 ? null : value;
        }

        private string TakeChunk()
        {
            var breakIndex = FindBreakIndex();
            var chunk = _text.ToString(0, breakIndex).Trim();
            var overlapStart = FindOverlapStart(chunk);

            var remainderLength = _text.Length - overlapStart;
            var remainder = _text.ToString(overlapStart, remainderLength).TrimStart();
            _text.Clear();
            _text.Append(remainder);
            _previousWasWhitespace = _text.Length > 0 && char.IsWhiteSpace(_text[^1]);
            _nextTokenCheckLength = _text.Length + Math.Max(64, _text.Length / 2);
            return chunk;
        }

        private int FindBreakIndex()
        {
            var value = _text.ToString();
            var breakIndex = FindMaximumPrefixLength(value);
            var preferredStart = Math.Max(0, breakIndex - 48);
            for (var index = breakIndex - 1; index >= preferredStart; index--)
            {
                if (IsBreakCharacter(value[index]))
                {
                    return index + 1;
                }
            }

            return breakIndex;
        }

        private int FindMaximumPrefixLength(string value)
        {
            var low = 1;
            var high = value.Length;
            while (low < high)
            {
                var middle = low + (high - low + 1) / 2;
                if (_countTokens(value[..middle]) <= MaximumPayloadTokens)
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

        private int FindOverlapStart(string chunk)
        {
            var low = 0;
            var high = chunk.Length;
            while (low < high)
            {
                var middle = low + (high - low) / 2;
                if (_countTokens(chunk[middle..]) > OverlapTokens)
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

        private int CountTokens()
        {
            return _countTokens(_text.ToString());
        }

        private static bool IsBreakCharacter(char character)
        {
            return char.IsWhiteSpace(character)
                   || character is '.' or '!' or '?' or '\u3002' or '\uFF01' or '\uFF1F';
        }
    }
}

internal sealed record DocumentContentSource(
    string Path,
    string SourceFingerprint,
    string? ContentHash = null);
