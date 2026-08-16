using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using BenchmarkDotNet.Attributes;
using Kitopia.Desktop.Features.Search.Semantic;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace KitopiaBenchmark;

[Config(typeof(InProcessConfig))]
[MemoryDiagnoser]
public class DocumentTextExtractorBenchmark
{
    private const int PayloadLength = 2 * 1024 * 1024;
    private const int PdfPayloadLength = 256 * 1024;
    private string _directory = null!;
    private string _markdownPath = null!;
    private string _docxPath = null!;
    private string _pdfPath = null!;

    [GlobalSetup]
    public async Task Setup()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"kitopia-document-extractor-benchmark-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);

        var payload = CreatePayload(PayloadLength);
        _markdownPath = Path.Combine(_directory, "payload.md");
        _docxPath = Path.Combine(_directory, "payload.docx");
        _pdfPath = Path.Combine(_directory, "payload.pdf");

        await File.WriteAllTextAsync(_markdownPath, payload);
        await CreateDocxAsync(_docxPath, payload);
        await File.WriteAllTextAsync(_pdfPath, CreatePdf(CreatePayload(PdfPayloadLength)), Encoding.ASCII);

        await VerifyEquivalentChunkCountsAsync(_markdownPath, LegacyDocumentTextExtractor.ExtractMarkdownAsync);
        await VerifyEquivalentChunkCountsAsync(_docxPath, LegacyDocumentTextExtractor.ExtractDocxAsync);
        await VerifyEquivalentChunkCountsAsync(_pdfPath, LegacyDocumentTextExtractor.ExtractPdfAsync);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Benchmark(Description = "Legacy Markdown")]
    public Task<int> LegacyMarkdown()
    {
        return LegacyDocumentTextExtractor.ExtractMarkdownAsync(_markdownPath, CountCharactersString);
    }

    [Benchmark(Description = "Streaming Markdown")]
    public Task<int> StreamingMarkdown()
    {
        return ExtractCurrentAsync(_markdownPath);
    }

    [Benchmark(Description = "Legacy OpenXML")]
    public Task<int> LegacyOpenXml()
    {
        return LegacyDocumentTextExtractor.ExtractDocxAsync(_docxPath, CountCharactersString);
    }

    [Benchmark(Description = "Streaming OpenXML")]
    public Task<int> StreamingOpenXml()
    {
        return ExtractCurrentAsync(_docxPath);
    }

    [Benchmark(Description = "Legacy PDF extraction")]
    public Task<int> LegacyPdf()
    {
        return LegacyDocumentTextExtractor.ExtractPdfAsync(_pdfPath, CountCharactersString);
    }

    [Benchmark(Description = "Streaming PDF")]
    public Task<int> StreamingPdf()
    {
        return ExtractCurrentAsync(_pdfPath);
    }

    private static async Task<int> ExtractCurrentAsync(string path)
    {
        if (!DocumentTextExtractor.TryCreateSource(path, out var source))
        {
            throw new InvalidOperationException($"Could not create a source for {path}.");
        }

        var chunks = 0;
        await foreach (var _ in DocumentTextExtractor.ExtractChunksAsync(source, CountCharacters, CancellationToken.None))
        {
            chunks++;
        }

        return chunks;
    }

    private static async Task VerifyEquivalentChunkCountsAsync(
        string path,
        Func<string, Func<string, int>, Task<int>> legacyExtractor)
    {
        var legacyChunkCount = await legacyExtractor(path, CountCharactersString);
        var streamingChunkCount = await ExtractCurrentAsync(path);
        if (legacyChunkCount != streamingChunkCount)
        {
            throw new InvalidOperationException($"Chunk count differs for {path}: legacy={legacyChunkCount}, streaming={streamingChunkCount}.");
        }
    }

    private static int CountCharacters(ReadOnlySpan<char> text)
    {
        return text.Length;
    }

    private static int CountCharactersString(string text)
    {
        return text.Length;
    }

    private static string CreatePayload(int length)
    {
        const string text = "streaming semantic search benchmark ";
        var builder = new StringBuilder(length);
        while (builder.Length < length)
        {
            builder.Append(text);
        }

        return builder.ToString(0, length);
    }

    private static async Task CreateDocxAsync(string path, string payload)
    {
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        var entry = archive.CreateEntry("word/document.xml", CompressionLevel.Fastest);
        await using var entryStream = entry.Open();
        await using var writer = new StreamWriter(entryStream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        await writer.WriteAsync("<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p><w:r><w:t>");
        await writer.WriteAsync(payload);
        await writer.WriteAsync("</w:t></w:r></w:p></w:body></w:document>");
    }

    private static string CreatePdf(string payload)
    {
        var encodedText = new StringBuilder(payload.Length * 4);
        for (var index = 0; index < payload.Length; index++)
        {
            encodedText.Append("0001");
        }

        var stream = $"BT /F1 12 Tf 72 720 Td <{encodedText}> Tj ET";
        const string cmap = "/CIDInit /ProcSet findresource begin\n12 dict begin\nbegincmap\n/CMapName /Test-UCS def\n/CMapType 2 def\n1 begincodespacerange\n<0000> <FFFF>\nendcodespacerange\n1 beginbfchar\n<0001> <0061>\nendbfchar\nendcmap\nCMapName currentdict /CMap defineresource pop\nend\nend";
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>",
            $"<< /Length {Encoding.ASCII.GetByteCount(stream)} >>\nstream\n{stream}\nendstream",
            "<< /Type /Font /Subtype /Type0 /BaseFont /TestFont /Encoding /Identity-H /ToUnicode 6 0 R >>",
            $"<< /Length {Encoding.ASCII.GetByteCount(cmap)} >>\nstream\n{cmap}\nendstream"
        };
        var builder = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int> { 0 };
        for (var index = 0; index < objects.Length; index++)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(builder.ToString()));
            builder.Append(index + 1).Append(" 0 obj\n");
            builder.Append(objects[index]).Append("\nendobj\n");
        }

        var xrefOffset = Encoding.ASCII.GetByteCount(builder.ToString());
        builder.Append("xref\n0 ").Append(objects.Length + 1).Append("\n");
        builder.Append("0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1))
        {
            builder.Append(offset.ToString("D10")).Append(" 00000 n \n");
        }

        builder.Append("trailer\n<< /Size ").Append(objects.Length + 1).Append(" /Root 1 0 R >>\n");
        builder.Append("startxref\n").Append(xrefOffset).Append("\n%%EOF\n");
        return builder.ToString();
    }
}

// Mirrors the pre-streaming implementation for benchmark-only comparison.
internal static class LegacyDocumentTextExtractor
{
    private static readonly Regex PdfTextOperatorPattern = new(
        @"/(\w+)\s+[-+.\d]+\s+Tf|<([0-9A-F]+)>\s*Tj|\[([^]]*)\]\s*TJ",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static async Task<int> ExtractMarkdownAsync(string path, Func<string, int> countTokens)
    {
        var chunker = new LegacyTextChunker(countTokens);
        var chunks = 0;
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 16 * 1024, useAsync: true);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var buffer = new char[4096];
        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory());
            if (read == 0)
            {
                break;
            }

            foreach (var _ in chunker.Append(new string(buffer, 0, read)))
            {
                chunks++;
            }
        }

        return chunks + (chunker.Flush() is null ? 0 : 1);
    }

    public static async Task<int> ExtractDocxAsync(string path, Func<string, int> countTokens)
    {
        var chunker = new LegacyTextChunker(countTokens);
        var chunks = 0;
        using var archive = ZipFile.OpenRead(path);
        var entry = archive.GetEntry("word/document.xml")!;
        await using var stream = entry.Open();
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { Async = true });
        while (await reader.ReadAsync())
        {
            if (reader.NodeType != XmlNodeType.Element)
            {
                continue;
            }

            if (reader.LocalName is "p" or "br" or "tab")
            {
                foreach (var _ in chunker.Append("\n"))
                {
                    chunks++;
                }
            }

            if (reader.LocalName is "t" or "instrText")
            {
                var text = await reader.ReadElementContentAsStringAsync();
                foreach (var _ in chunker.Append(text))
                {
                    chunks++;
                }
            }
        }

        return chunks + (chunker.Flush() is null ? 0 : 1);
    }

    public static Task<int> ExtractPdfAsync(string path, Func<string, int> countTokens)
    {
        var chunker = new LegacyTextChunker(countTokens);
        var chunks = 0;
        using var document = PdfReader.Open(path, PdfDocumentOpenMode.Import);
        foreach (var page in document.Pages)
        {
            foreach (var _ in chunker.Append(DecodePdfPageText(page)))
            {
                chunks++;
            }
        }

        return Task.FromResult(chunks + (chunker.Flush() is null ? 0 : 1));
    }

    private static string DecodePdfPageText(PdfPage page)
    {
        var builder = new StringBuilder();
        var activeFont = string.Empty;
        foreach (var content in page.Contents)
        {
            if (content.Stream?.UnfilteredValue is not { } stream)
            {
                continue;
            }

            var contentStream = Encoding.ASCII.GetString(stream);
            foreach (Match match in PdfTextOperatorPattern.Matches(contentStream))
            {
                if (match.Groups[1].Success)
                {
                    activeFont = "/" + match.Groups[1].Value;
                    continue;
                }

                if (!activeFont.Equals("/F1", StringComparison.Ordinal))
                {
                    continue;
                }

                if (match.Groups[2].Success)
                {
                    AppendDecodedGlyphs(builder, match.Groups[2].Value);
                }
            }
        }

        return builder.Append('\n').ToString();
    }

    private static void AppendDecodedGlyphs(StringBuilder builder, string hex)
    {
        for (var index = 0; index < hex.Length; index += 4)
        {
            if (Convert.ToUInt16(hex.Substring(index, 4), 16) == 1)
            {
                builder.Append('a');
            }
        }
    }

    private sealed class LegacyTextChunker
    {
        private const int MaximumPayloadTokens = 254;
        private const int OverlapTokens = 48;
        private readonly Func<string, int> _countTokens;
        private readonly StringBuilder _text = new();
        private bool _previousWasWhitespace;
        private int _nextTokenCheckLength = MaximumPayloadTokens;

        public LegacyTextChunker(Func<string, int> countTokens)
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

                var tokenCount = _countTokens(_text.ToString());
                if (tokenCount >= MaximumPayloadTokens)
                {
                    yield return TakeChunk();
                }
                else
                {
                    _nextTokenCheckLength = _text.Length + Math.Max(1, MaximumPayloadTokens - tokenCount);
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
            var remainder = _text.ToString(overlapStart, _text.Length - overlapStart).TrimStart();
            _text.Clear();
            _text.Append(remainder);
            _previousWasWhitespace = _text.Length > 0 && char.IsWhiteSpace(_text[^1]);
            var tokenCount = _countTokens(_text.ToString());
            _nextTokenCheckLength = _text.Length + Math.Max(1, MaximumPayloadTokens - tokenCount);
            return chunk;
        }

        private int FindBreakIndex()
        {
            var value = _text.ToString();
            var breakIndex = FindMaximumPrefixLength(value);
            for (var index = breakIndex - 1; index >= Math.Max(0, breakIndex - 48); index--)
            {
                if (char.IsWhiteSpace(value[index]) || value[index] is '.' or '!' or '?')
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
    }
}
