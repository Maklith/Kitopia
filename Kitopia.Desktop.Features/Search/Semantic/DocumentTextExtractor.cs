using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using UglyToad.PdfPig;

namespace Kitopia.Desktop.Features.Search.Semantic;

internal static class DocumentTextExtractor
{
    private static readonly HashSet<string> PlainTextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md"
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
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(source.Path);
        if (PlainTextExtensions.Contains(extension))
        {
            await foreach (var chunk in ReadPlainTextChunksAsync(source.Path, cancellationToken))
            {
                yield return chunk;
            }

            yield break;
        }

        if (extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var chunk in ReadPdfChunks(source.Path, cancellationToken))
            {
                yield return chunk;
            }

            yield break;
        }

        await foreach (var chunk in ReadOpenXmlChunksAsync(source.Path, extension, cancellationToken))
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
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var chunker = new TextChunker();
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

    private static IEnumerable<string> ReadPdfChunks(string path, CancellationToken cancellationToken)
    {
        var chunker = new TextChunker();
        using var document = PdfDocument.Open(path);
        foreach (var page in document.GetPages())
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var chunk in chunker.Append(page.Text))
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

    private static async IAsyncEnumerable<string> ReadOpenXmlChunksAsync(
        string path,
        string extension,
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

        var chunker = new TextChunker();
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
        // BGE accepts 512 WordPiece tokens for document content. A WordPiece cannot
        // outnumber source characters, so 510 characters leave room for [CLS] and [SEP].
        private const int ChunkLength = 510;
        private const int OverlapLength = 64;
        private const int BreakSearchLength = 48;
        private readonly StringBuilder _text = new(ChunkLength + OverlapLength);
        private bool _previousWasWhitespace;

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

                if (_text.Length >= ChunkLength)
                {
                    yield return TakeChunk();
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
            var overlapStart = Math.Max(0, breakIndex - OverlapLength);
            if (overlapStart > 0 && !char.IsWhiteSpace(_text[overlapStart - 1]))
            {
                // Move forward to a word boundary when one is close. Moving backwards
                // without a bound retains an entire whitespace-free chunk indefinitely.
                var nextWhitespace = overlapStart;
                while (nextWhitespace < breakIndex && !char.IsWhiteSpace(_text[nextWhitespace]))
                {
                    nextWhitespace++;
                }

                if (nextWhitespace < breakIndex)
                {
                    overlapStart = nextWhitespace + 1;
                }
            }

            var remainderLength = _text.Length - overlapStart;
            var remainder = _text.ToString(overlapStart, remainderLength).TrimStart();
            _text.Clear();
            _text.Append(remainder);
            _previousWasWhitespace = _text.Length > 0 && char.IsWhiteSpace(_text[^1]);
            return chunk;
        }

        private int FindBreakIndex()
        {
            var minimumBreakIndex = ChunkLength - BreakSearchLength;
            for (var index = Math.Min(ChunkLength, _text.Length) - 1; index >= minimumBreakIndex; index--)
            {
                if (char.IsWhiteSpace(_text[index])
                    || _text[index] is '.' or '!' or '?' or '\u3002' or '\uFF01' or '\uFF1F')
                {
                    return index + 1;
                }
            }

            return Math.Min(ChunkLength, _text.Length);
        }
    }
}

internal sealed record DocumentContentSource(
    string Path,
    string SourceFingerprint,
    string? ContentHash = null);
