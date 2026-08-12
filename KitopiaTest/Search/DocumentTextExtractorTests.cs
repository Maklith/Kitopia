using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using Kitopia.Desktop.Features.Search.Semantic;
using Microsoft.Data.Sqlite;

namespace KitopiaTest.Search;

[TestClass]
public sealed class DocumentTextExtractorTests
{
    [TestMethod]
    public async Task ExtractChunksAsync_TextFile_StreamsAllTextInChunks()
    {
        var path = CreateTemporaryPath(".txt");
        try
        {
            await File.WriteAllTextAsync(path, string.Concat(Enumerable.Repeat("Termius connection profile ", 80)));

            var chunks = await ExtractChunksAsync(path);

            Assert.IsTrue(chunks.Count > 1);
            Assert.IsTrue(chunks.All(chunk => chunk.Length <= 510));
            Assert.IsTrue(chunks.Any(chunk => chunk.Length >= 430));
            StringAssert.Contains(string.Join(' ', chunks), "Termius connection profile");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public async Task ExtractChunksAsync_Gb18030TextFile_PreservesChineseText()
    {
        var path = CreateTemporaryPath(".txt");
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            await File.WriteAllBytesAsync(path, Encoding.GetEncoding("GB18030").GetBytes("Termius 服务器连接配置"));

            var chunks = await ExtractChunksAsync(path);

            StringAssert.Contains(string.Join(' ', chunks), "服务器连接配置");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public async Task ExtractChunksAsync_TextWithoutWhitespace_KeepsAStableChunkWindow()
    {
        var path = CreateTemporaryPath(".txt");
        try
        {
            await File.WriteAllTextAsync(path, new string('a', 2_000));

            var chunks = await ExtractChunksAsync(path);

            Assert.AreEqual(5, chunks.Count);
            Assert.IsTrue(chunks.All(chunk => chunk.Length <= 510));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public async Task CreateSource_UsesFileMetadataBeforeComputingContentHash()
    {
        var path = CreateTemporaryPath(".txt");
        var duplicatePath = CreateTemporaryPath(".txt");
        try
        {
            var preservedWriteTime = DateTime.UtcNow.AddMinutes(-5);
            await File.WriteAllTextAsync(path, "first");
            File.SetLastWriteTimeUtc(path, preservedWriteTime);
            Assert.IsTrue(DocumentTextExtractor.TryCreateSource(path, out var originalSource));
            var original = await DocumentTextExtractor.TryComputeContentHashAsync(originalSource, CancellationToken.None);
            await File.WriteAllTextAsync(duplicatePath, "first");
            File.SetLastWriteTimeUtc(duplicatePath, preservedWriteTime);
            Assert.IsTrue(DocumentTextExtractor.TryCreateSource(duplicatePath, out var duplicateSource));
            var duplicate = await DocumentTextExtractor.TryComputeContentHashAsync(duplicateSource, CancellationToken.None);

            await File.WriteAllTextAsync(path, "other");
            File.SetLastWriteTimeUtc(path, preservedWriteTime);
            Assert.IsTrue(DocumentTextExtractor.TryCreateSource(path, out var changedSource));
            var changed = await DocumentTextExtractor.TryComputeContentHashAsync(changedSource, CancellationToken.None);

            Assert.IsNotNull(original);
            Assert.IsNotNull(duplicate);
            Assert.IsNotNull(changed);
            Assert.AreEqual(
                $"{Path.GetFullPath(path)}|5|{new FileInfo(path).LastWriteTimeUtc.Ticks}",
                originalSource.SourceFingerprint);
            Assert.AreNotEqual(originalSource.SourceFingerprint, duplicateSource.SourceFingerprint);
            Assert.AreEqual(originalSource.SourceFingerprint, changedSource.SourceFingerprint);
            Assert.AreEqual(original.ContentHash, duplicate.ContentHash);
            Assert.AreNotEqual(original.ContentHash, changed.ContentHash);
        }
        finally
        {
            File.Delete(path);
            File.Delete(duplicatePath);
        }
    }

    [TestMethod]
    public async Task ExtractChunksAsync_Docx_ExtractsDocumentText()
    {
        var path = CreateTemporaryPath(".docx");
        try
        {
            using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                await WriteZipEntryAsync(
                    archive,
                    "word/document.xml",
                    "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p><w:r><w:t>Termius SSH host</w:t></w:r></w:p></w:body></w:document>");
            }

            var chunks = await ExtractChunksAsync(path);

            StringAssert.Contains(string.Join(' ', chunks), "Termius SSH host");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public async Task ExtractChunksAsync_Xlsx_ExtractsSharedStringCells()
    {
        var path = CreateTemporaryPath(".xlsx");
        try
        {
            using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                await WriteZipEntryAsync(
                    archive,
                    "xl/sharedStrings.xml",
                    "<sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><si><t>Termius server inventory</t></si></sst>");
                await WriteZipEntryAsync(
                    archive,
                    "xl/worksheets/sheet1.xml",
                    "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" />");
            }

            var chunks = await ExtractChunksAsync(path);

            StringAssert.Contains(string.Join(' ', chunks), "Termius server inventory");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public async Task ExtractChunksAsync_Pdf_ExtractsPageText()
    {
        var path = CreateTemporaryPath(".pdf");
        try
        {
            await File.WriteAllTextAsync(path, CreatePdf("Termius PDF connection notes"), Encoding.ASCII);

            var chunks = await ExtractChunksAsync(path);

            StringAssert.Contains(string.Join(' ', chunks), "Termius PDF connection notes");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public async Task SemanticVectorStore_ContentVectors_AreSeparateFromEntryVectors()
    {
        var databasePath = CreateTemporaryPath(".db");
        try
        {
            var store = new SqliteSemanticVectorStore(databasePath);
            var baseVector = CreateUnitVector(0);
            var contentVector = CreateUnitVector(1);
            await store.UpsertBatchAsync(
                [new EmbeddingWrite("entry:base", "base-hash", "test-model", baseVector)],
                CancellationToken.None);
            await store.UpsertContentBatchAsync(
                [new ContentEmbeddingWrite("entry:document", "content-v1", 0, "test-model", contentVector)],
                CancellationToken.None);
            await store.CompleteContentIndexAsync(
                "entry:document",
                "source-fingerprint-v1",
                "content-v1",
                chunkCount: 1,
                "test-model",
                dimensions: 512,
                CancellationToken.None);

            var baseMatches = await store.SearchAsync("test-model", baseVector, 10, CancellationToken.None);
            var contentMatches = await store.SearchContentAsync("test-model", contentVector, 10, CancellationToken.None);
            var indexedContentHashes = await store.LoadIndexedContentHashesAsync(
                ["content-v1", "missing-content"],
                "test-model",
                dimensions: 512,
                CancellationToken.None);

            CollectionAssert.AreEqual(new[] { "entry:base" }, baseMatches.Select(match => match.OnlyKey).ToArray());
            CollectionAssert.AreEqual(new[] { "entry:document" }, contentMatches.Select(match => match.OnlyKey).ToArray());
            CollectionAssert.AreEquivalent(new[] { "content-v1" }, indexedContentHashes.ToArray());

            await store.DeleteBatchAsync(new[] { "entry:document" }, CancellationToken.None);
            var matchesAfterRemoval = await store.SearchContentAsync("test-model", contentVector, 10, CancellationToken.None);
            Assert.AreEqual(0, matchesAfterRemoval.Count);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
            File.Delete(databasePath + "-shm");
            File.Delete(databasePath + "-wal");
        }
    }

    [TestMethod]
    public async Task SemanticVectorStore_SearchAsync_RanksHighDimensionalVectors()
    {
        var databasePath = CreateTemporaryPath(".db");
        try
        {
            var store = new SqliteSemanticVectorStore(databasePath);
            var query = Enumerable.Repeat(1f / MathF.Sqrt(512), 512).ToArray();
            var opposite = query.Select(value => -value).ToArray();

            await store.UpsertBatchAsync(
                [
                    new EmbeddingWrite("matching", "matching-hash", "test-model", query),
                    new EmbeddingWrite("opposite", "opposite-hash", "test-model", opposite)
                ],
                CancellationToken.None);

            var matches = await store.SearchAsync("test-model", query, 2, CancellationToken.None);

            CollectionAssert.AreEqual(new[] { "matching", "opposite" }, matches.Select(match => match.OnlyKey).ToArray());
            Assert.IsTrue(matches[0].Score > 0.99d);
            Assert.IsTrue(matches[1].Score < -0.99d);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
            File.Delete(databasePath + "-shm");
            File.Delete(databasePath + "-wal");
        }
    }

    [TestMethod]
    public async Task SemanticVectorStore_SearchAsync_FiltersOtherModelsBeforeLimitingResults()
    {
        var databasePath = CreateTemporaryPath(".db");
        try
        {
            var query = CreateUnitVector(0);
            var currentModelVector = CreateUnitVector(1);
            currentModelVector[0] = 0.5f;
            var store = new SqliteSemanticVectorStore(databasePath);
            await store.UpsertBatchAsync(
                [
                    new EmbeddingWrite("stale", "stale-hash", "retired-model", query),
                    new EmbeddingWrite("current", "current-hash", "test-model", currentModelVector)
                ],
                CancellationToken.None);

            var matches = await store.SearchAsync("test-model", query, 1, CancellationToken.None);

            CollectionAssert.AreEqual(new[] { "current" }, matches.Select(match => match.OnlyKey).ToArray());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
            File.Delete(databasePath + "-shm");
            File.Delete(databasePath + "-wal");
        }
    }

    private static async Task<List<string>> ExtractChunksAsync(string path)
    {
        Assert.IsTrue(DocumentTextExtractor.TryCreateSource(path, out var source));
        var chunks = new List<string>();
        await foreach (var chunk in DocumentTextExtractor.ExtractChunksAsync(source, CancellationToken.None))
        {
            chunks.Add(chunk);
        }

        return chunks;
    }

    private static float[] CreateUnitVector(int index)
    {
        var vector = new float[512];
        vector[index] = 1f;
        return vector;
    }

    private static async Task WriteZipEntryAsync(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name);
        await using var stream = entry.Open();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        await writer.WriteAsync(content);
    }

    private static string CreateTemporaryPath(string extension)
    {
        return Path.Combine(Path.GetTempPath(), $"kitopia-rag-{Guid.NewGuid():N}{extension}");
    }

    private static string CreatePdf(string text)
    {
        var stream = $"BT /F1 16 Tf 72 720 Td ({text}) Tj ET";
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>",
            $"<< /Length {Encoding.ASCII.GetByteCount(stream)} >>\nstream\n{stream}\nendstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"
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
