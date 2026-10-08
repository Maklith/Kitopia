using Kitopia.Desktop.Features.Indexing;
using Microsoft.Data.Sqlite;

namespace KitopiaTest.Indexing;

[TestClass]
public sealed class IndexVectorStoreTests
{
    private static readonly IReadOnlySet<string> NoProtectedKeys = new HashSet<string>();
    private string _directory = null!;
    private IndexVectorStore _store = null!;

    [TestInitialize]
    public void Initialize()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"kitopia-index-store-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
        _store = new IndexVectorStore(Path.Combine(_directory, "index.db"));
    }

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task SynchronizeFileSourceAsync_StreamsChangesAndDeletesOnlyRemovedPaths()
    {
        var first = Path.Combine(_directory, "first.txt");
        var shared = Path.Combine(_directory, "shared.txt");
        var second = Path.Combine(_directory, "second.txt");

        Assert.IsTrue(await _store.SynchronizeFileSourceAsync(
            IndexSource.Manual, [first, shared, first], NoProtectedKeys, CancellationToken.None));
        Assert.IsTrue(await _store.SynchronizeFileSourceAsync(
            IndexSource.EverythingManaged, [shared, second], NoProtectedKeys, CancellationToken.None));
        Assert.IsFalse(await _store.SynchronizeFileSourceAsync(
            IndexSource.Manual, [first, shared], NoProtectedKeys, CancellationToken.None));

        Assert.IsTrue(await _store.SynchronizeFileSourceAsync(
            IndexSource.Manual, [first], NoProtectedKeys, CancellationToken.None));
        var paths = await ReadPathsAsync();

        CollectionAssert.AreEquivalent(new[] { first, shared, second }, paths);
    }

    [TestMethod]
    public async Task SynchronizeFileSourceAsync_KeepsPreviousManifestWhenEnumerationFails()
    {
        var existing = Path.Combine(_directory, "existing.txt");
        var partial = Path.Combine(_directory, "partial.txt");
        Assert.IsTrue(await _store.SynchronizeFileSourceAsync(
            IndexSource.Manual, [existing], NoProtectedKeys, CancellationToken.None));

        await Assert.ThrowsExactlyAsync<IOException>(async () =>
            await _store.SynchronizeFileSourceAsync(
                IndexSource.Manual, ThrowAfterFirst(partial), NoProtectedKeys, CancellationToken.None));

        CollectionAssert.AreEquivalent(new[] { existing }, await ReadPathsAsync());
    }

    [TestMethod]
    public async Task SynchronizeFileSourceAsync_RemovedPathDeletesFileVectorsOcrAndState()
    {
        var document = Path.Combine(_directory, "document.txt");
        var image = Path.Combine(_directory, "image.png");
        const string textModel = "text-model";
        const string imageModel = "image-model";

        await _store.SynchronizeFileSourceAsync(
            IndexSource.Manual,
            [document, image],
            NoProtectedKeys,
            CancellationToken.None);
        await _store.UpsertDocumentTextAsync(document, textModel, [new float[768], new float[768]], CancellationToken.None);
        await _store.UpsertOcrTextAsync(image, textModel, new float[768], CancellationToken.None);
        await _store.UpsertImageAsync(image, "1:1", imageModel, new float[768], CancellationToken.None);
        await _store.UpsertFileStateAsync(
            new FileIndexState(document, IndexFileKind.Document, 1, 1, "document", false, null),
            CancellationToken.None);
        await _store.UpsertFileStateAsync(
            new FileIndexState(image, IndexFileKind.Image, 1, 1, "image", true, textModel),
            CancellationToken.None);

        Assert.IsTrue(await _store.SynchronizeFileSourceAsync(
            IndexSource.Manual,
            [],
            NoProtectedKeys,
            CancellationToken.None));

        Assert.AreEqual((0, 0), await _store.GetCountsAsync(CancellationToken.None));
        Assert.IsNull(await _store.GetFileStateAsync(document, IndexFileKind.Document, CancellationToken.None));
        Assert.IsNull(await _store.GetFileStateAsync(image, IndexFileKind.Image, CancellationToken.None));
    }

    [TestMethod]
    public async Task SynchronizeFileSourceAsync_RemovedPathDeletesFallbackEntryVector()
    {
        var document = Path.Combine(_directory, "unreadable.pdf");
        const string model = "text-model";
        await _store.SynchronizeFileSourceAsync(
            IndexSource.Manual, [document], NoProtectedKeys, CancellationToken.None);
        await _store.UpsertTextAsync(document, model, new float[768], CancellationToken.None);

        await _store.SynchronizeFileSourceAsync(
            IndexSource.Manual, [], NoProtectedKeys, CancellationToken.None);

        Assert.AreEqual((0, 0), await _store.GetCountsAsync(CancellationToken.None));
    }

    [TestMethod]
    public async Task RemoveMatchingPathsAsync_DeletesIgnoredFilesAcrossSourcesAndKeepsSibling()
    {
        var ignoredRoot = Path.Combine(_directory, "nt_qq");
        var ignoredDocument = Path.Combine(ignoredRoot, "report.txt");
        var ignoredImage = Path.Combine(ignoredRoot, "image.png");
        var sibling = Path.Combine(_directory, "nt_qq2", "report.txt");
        await _store.SynchronizeFileSourceAsync(
            IndexSource.Manual, [ignoredDocument, sibling], NoProtectedKeys, CancellationToken.None);
        await _store.SynchronizeFileSourceAsync(
            IndexSource.EverythingManaged, [ignoredDocument, ignoredImage], NoProtectedKeys, CancellationToken.None);
        await _store.UpsertDocumentTextAsync(ignoredDocument, "text-model", [new float[768], new float[768]], CancellationToken.None);
        await _store.UpsertImageAsync(ignoredImage, "1:1", "image-model", new float[768], CancellationToken.None);
        await _store.UpsertDocumentTextAsync(sibling, "text-model", [new float[768]], CancellationToken.None);
        await _store.UpsertFileStateAsync(
            new FileIndexState(ignoredDocument, IndexFileKind.Document, 1, 1, "ignored", false, null),
            CancellationToken.None);

        await _store.RemoveMatchingPathsAsync(
            path => IndexService.IsIgnoredPath(path, [ignoredRoot]), CancellationToken.None);

        CollectionAssert.AreEquivalent(new[] { sibling }, await ReadPathsAsync());
        Assert.AreEqual((1, 0), await _store.GetCountsAsync(CancellationToken.None));
        Assert.IsNull(await _store.GetFileStateAsync(
            ignoredDocument, IndexFileKind.Document, CancellationToken.None));
    }

    [TestMethod]
    public async Task ResetAsync_DeletesPersistentFileIndexData()
    {
        var document = Path.Combine(_directory, "document.txt");
        var image = Path.Combine(_directory, "image.png");
        const string textModel = "text-model";
        const string imageModel = "image-model";
        await _store.SynchronizeFileSourceAsync(
            IndexSource.Manual,
            [document, image],
            NoProtectedKeys,
            CancellationToken.None);
        await _store.UpsertDocumentTextAsync(document, textModel, [new float[768], new float[768]], CancellationToken.None);
        await _store.UpsertImageAsync(image, "1:1", imageModel, new float[768], CancellationToken.None);
        await _store.UpsertFileStateAsync(
            new FileIndexState(document, IndexFileKind.Document, 1, 1, "document", false, null),
            CancellationToken.None);

        await _store.ResetAsync(CancellationToken.None);

        CollectionAssert.AreEqual(Array.Empty<string>(), await ReadPathsAsync());
        Assert.AreEqual((0, 0), await _store.GetCountsAsync(CancellationToken.None));
        Assert.IsNull(await _store.GetFileStateAsync(document, IndexFileKind.Document, CancellationToken.None));
        Assert.IsTrue(await _store.SynchronizeFileSourceAsync(
            IndexSource.Manual,
            [document],
            NoProtectedKeys,
            CancellationToken.None));
    }

    [TestMethod]
    public async Task HasCompletedOcrForContentHashAsync_RequiresAnOcrVector()
    {
        var image = Path.Combine(_directory, "image.png");
        const string contentHash = "content-hash";
        const string model = "text-model";
        await _store.UpsertFileStateAsync(
            new FileIndexState(image, IndexFileKind.Image, 1, 1, contentHash, true, model),
            CancellationToken.None);

        Assert.IsFalse(await _store.HasCompletedOcrForContentHashAsync(contentHash, model, CancellationToken.None));

        await _store.UpsertOcrTextAsync(image, model, new float[768], CancellationToken.None);

        Assert.IsTrue(await _store.HasCompletedOcrForContentHashAsync(contentHash, model, CancellationToken.None));
    }

    [TestMethod]
    public async Task OpeningLegacyDatabase_MigratesPathPrimaryKeysToNoCase()
    {
        var image = Path.Combine(_directory, "Screen.PNG");
        var alternateCasing = Path.Combine(_directory, "screen.png");
        const string model = "text-model";
        const string hash = "image-hash";
        await _store.UpsertOcrTextAsync(image, model, new float[768], CancellationToken.None);
        await _store.UpsertFileStateAsync(
            new FileIndexState(image, IndexFileKind.Image, 1, 1, hash, true, model),
            CancellationToken.None);
        await ReplacePathTablesWithLegacyDefinitionsAsync();

        var migrated = new IndexVectorStore(Path.Combine(_directory, "index.db"));

        Assert.IsNotNull(await migrated.GetFileStateAsync(alternateCasing, IndexFileKind.Image, CancellationToken.None));
        Assert.IsTrue(await migrated.HasOcrTextVectorAsync(alternateCasing, model, CancellationToken.None));
        await AssertNoCasePrimaryKeyAsync("index_text_metadata");
        await AssertNoCasePrimaryKeyAsync("index_file_states");
    }

    [TestMethod]
    public async Task OpeningOldVectorDimensions_RebuildsVectorsAndPreservesFileSourcesAndStates()
    {
        var document = Path.Combine(_directory, "report.txt");
        var image = Path.Combine(_directory, "image.png");
        var state = new FileIndexState(document, IndexFileKind.Document, 123, 456, "hash", false, null);
        await _store.SynchronizeFileSourceAsync(IndexSource.Manual, [document, image], NoProtectedKeys, CancellationToken.None);
        await _store.UpsertFileStateAsync(state, CancellationToken.None);
        await _store.UpsertTextAsync(document, "old", new float[768], CancellationToken.None);
        await _store.UpsertImageAsync(image, "1:1", "old", new float[768], CancellationToken.None);
        await using (var connection = new SqliteConnection($"Data Source={Path.Combine(_directory, "index.db")}"))
        {
            await connection.OpenAsync();
            connection.EnableExtensions(true);
            connection.LoadVector();
            connection.EnableExtensions(false);
            await ExecuteAsync(connection, """
                DROP TABLE index_text_vectors;
                DROP TABLE index_image_vectors;
                CREATE VIRTUAL TABLE index_text_vectors USING vec0(embedding float[512] distance_metric=cosine, model_id TEXT PARTITION KEY);
                CREATE VIRTUAL TABLE index_image_vectors USING vec0(embedding float[1024] distance_metric=cosine, model_id TEXT PARTITION KEY);
                INSERT INTO index_text_vectors(rowid, embedding, model_id) VALUES(1, zeroblob(2048), 'old');
                INSERT INTO index_image_vectors(rowid, embedding, model_id) VALUES(1, zeroblob(4096), 'old');
                UPDATE index_text_metadata SET dimensions = 512;
                UPDATE index_image_metadata SET dimensions = 1024;
                """);
        }
        _store = new IndexVectorStore(Path.Combine(_directory, "index.db"));
        Assert.AreEqual((0, 0), await _store.GetCountsAsync(CancellationToken.None));
        CollectionAssert.AreEquivalent(new[] { document, image }, await ReadPathsAsync());
        Assert.AreEqual(state, await _store.GetFileStateAsync(document, IndexFileKind.Document, CancellationToken.None));
        Assert.IsFalse(await _store.HasTextVectorAsync(document, "old", CancellationToken.None));
        Assert.IsFalse(await _store.HasImageVectorAsync(image, "old", CancellationToken.None));
        var vector = new float[768];
        vector[0] = 1;
        await _store.UpsertDocumentTextAsync(document, "eg2", [vector], CancellationToken.None);
        await _store.UpsertImageAsync(image, "1:1", "eg2", vector, CancellationToken.None);
        Assert.AreEqual(document, (await _store.SearchTextAsync("eg2", vector, 10, CancellationToken.None))[0].Key);
        Assert.AreEqual(image, (await _store.SearchImagesAsync("eg2", vector, 10, CancellationToken.None))[0].Key);
    }

    [TestMethod]
    public async Task TryCopyDocumentTextForContentHashAsync_RequiresTheSameTitle()
    {
        var original = Path.Combine(_directory, "report.txt");
        var renamed = Path.Combine(_directory, "renamed.txt");
        var sameTitle = Path.Combine(_directory, "copy", "report.txt");
        var vector = new float[768];
        vector[0] = 1;
        var otherChunk = new float[768];
        otherChunk[1] = 1;
        await _store.UpsertDocumentTextAsync(original, "eg2", [vector, otherChunk], CancellationToken.None);
        await _store.UpsertFileStateAsync(new FileIndexState(original, IndexFileKind.Document, 1, 1, "hash", false, null), CancellationToken.None);
        Assert.IsFalse(await _store.TryCopyDocumentTextForContentHashAsync(renamed, "hash", "eg2", CancellationToken.None));
        Assert.IsTrue(await _store.TryCopyDocumentTextForContentHashAsync(sameTitle, "hash", "eg2", CancellationToken.None));
        Assert.IsTrue(await _store.HasTextVectorAsync(sameTitle, "eg2", CancellationToken.None));
        await AssertStoredTextVectorsAsync(4);
        await _store.DeleteAsync(original, CancellationToken.None);
        await AssertStoredTextVectorsAsync(2);
        var matches = await _store.SearchTextAsync("eg2", otherChunk, 10, CancellationToken.None);
        Assert.HasCount(1, matches);
        Assert.AreEqual(sameTitle, matches[0].Key);
        Assert.AreEqual(1d, matches[0].Score, 1e-6);
    }

    [TestMethod]
    public async Task SearchTextAsync_UsesTheBestChunkAndReturnsDistinctFiles()
    {
        var document = Path.Combine(_directory, "long.txt");
        var sibling = Path.Combine(_directory, "other.txt");
        var query = new float[768];
        query[0] = 1;
        var opposite = new float[768];
        opposite[0] = -1;
        var other = new float[768];
        other[0] = 0.8f;
        other[1] = 0.6f;
        await _store.UpsertDocumentTextAsync(document, "eg2", [opposite, query, query, query], CancellationToken.None);
        await _store.UpsertDocumentTextAsync(sibling, "eg2", [other], CancellationToken.None);
        await _store.UpsertTextAsync("another-model", "old", query, CancellationToken.None);

        var matches = await _store.SearchTextAsync("eg2", query, 2, CancellationToken.None);

        Assert.HasCount(2, matches);
        Assert.AreEqual(document, matches[0].Key);
        Assert.AreEqual(1d, matches[0].Score, 1e-6);
        Assert.AreEqual(sibling, matches[1].Key);
        Assert.AreEqual(0.8d, matches[1].Score, 1e-6);
    }

    [TestMethod]
    public async Task SearchTextAsync_ChunksExceedingCandidateBudget_DoesNotHideOtherFiles()
    {
        var query = new float[768];
        query[0] = 1;
        var other = new float[768];
        other[1] = 1;
        await _store.UpsertDocumentTextAsync("long", "eg2", Enumerable.Repeat(query, 4097).ToArray(), CancellationToken.None);
        await _store.UpsertDocumentTextAsync("other", "eg2", [other], CancellationToken.None);
        var matches = await _store.SearchTextAsync("eg2", query, 2, CancellationToken.None);
        Assert.HasCount(2, matches);
        Assert.AreEqual("long", matches[0].Key);
        Assert.AreEqual("other", matches[1].Key);
    }

    [TestMethod]
    public async Task UpsertDocumentTextAsync_ShorterDocument_ReplacesAllOldChunks()
    {
        var first = new float[768];
        first[0] = 1;
        var last = new float[768];
        last[1] = 1;
        await _store.UpsertDocumentTextAsync("document", "eg2", [first, last, last], CancellationToken.None);
        await _store.UpsertDocumentTextAsync("document", "eg2", [first], CancellationToken.None);

        await AssertStoredTextVectorsAsync(1);
        var matches = await _store.SearchTextAsync("eg2", last, 10, CancellationToken.None);
        Assert.HasCount(1, matches);
        Assert.AreEqual(0d, matches[0].Score, 1e-6);
    }

    [TestMethod]
    public async Task UpsertDocumentTextAsync_FailedReplacement_RollsBackEveryChunk()
    {
        var vector = new float[768];
        vector[0] = 1;
        await _store.UpsertDocumentTextAsync("document", "eg2", [vector, vector], CancellationToken.None);

        await Assert.ThrowsAsync<SqliteException>(() => _store.UpsertDocumentTextAsync(
            "document", "replacement", [vector, new float[3]], CancellationToken.None));

        await AssertStoredTextVectorsAsync(2);
        Assert.IsTrue(await _store.HasTextVectorAsync("document", "eg2", CancellationToken.None));
        Assert.IsFalse(await _store.HasTextVectorAsync("document", "replacement", CancellationToken.None));
    }

    [TestMethod]
    [DataRow("delete")]
    [DataRow("unreferenced")]
    [DataRow("documents")]
    [DataRow("all")]
    [DataRow("source")]
    [DataRow("ignored")]
    [DataRow("reset")]
    public async Task RemovingDocument_DeletesEveryChunkAndVector(string operation)
    {
        var document = Path.Combine(_directory, "document.txt");
        await _store.SynchronizeFileSourceAsync(IndexSource.Manual, [document], NoProtectedKeys, CancellationToken.None);
        await _store.UpsertDocumentTextAsync(document, "eg2", [new float[768], new float[768]], CancellationToken.None);
        switch (operation)
        {
            case "delete":
                await _store.DeleteAsync(document, CancellationToken.None);
                break;
            case "unreferenced":
                await _store.SynchronizeFileSourceAsync(IndexSource.Manual, [], new HashSet<string> { document }, CancellationToken.None);
                _store.DeleteIfUnreferenced(document);
                break;
            case "documents":
                await _store.ClearAsync(IndexRebuildScope.Documents, CancellationToken.None);
                break;
            case "all":
                await _store.ClearAsync(IndexRebuildScope.All, CancellationToken.None);
                break;
            case "source":
                await _store.SynchronizeFileSourceAsync(IndexSource.Manual, [], NoProtectedKeys, CancellationToken.None);
                break;
            case "ignored":
                await _store.RemoveMatchingPathsAsync(path => path == document, CancellationToken.None);
                break;
            case "reset":
                await _store.ResetAsync(CancellationToken.None);
                break;
        }
        await AssertStoredTextVectorsAsync(0);
    }

    [TestMethod]
    public async Task UpsertTextAsync_MetadataFallback_RemovesDocumentChunks()
    {
        await _store.UpsertDocumentTextAsync("document", "eg2", [new float[768], new float[768]], CancellationToken.None);
        await _store.UpsertTextAsync("document", "eg2", new float[768], CancellationToken.None);
        await AssertStoredTextVectorsAsync(1);
    }

    [TestMethod]
    public async Task OpeningAveragedDocumentSchema_ReindexesDocumentsAndPreservesOtherData()
    {
        var document = Path.Combine(_directory, "report.txt");
        var image = Path.Combine(_directory, "image.png");
        var state = new FileIndexState(document, IndexFileKind.Document, 123, 456, "hash", false, null);
        await _store.SynchronizeFileSourceAsync(IndexSource.Manual, [document, image], NoProtectedKeys, CancellationToken.None);
        await _store.UpsertDocumentTextAsync(document, "eg2", [new float[768]], CancellationToken.None);
        await _store.UpsertTextAsync("application", "eg2", new float[768], CancellationToken.None);
        await _store.UpsertOcrTextAsync(image, "eg2", new float[768], CancellationToken.None);
        await _store.UpsertImageAsync(image, "1:1", "eg2", new float[768], CancellationToken.None);
        await _store.UpsertFileStateAsync(state, CancellationToken.None);
        await ReplacePathTablesWithLegacyDefinitionsAsync();
        _store = new IndexVectorStore(Path.Combine(_directory, "index.db"));

        Assert.IsFalse(await _store.HasTextVectorAsync(document, "eg2", CancellationToken.None));
        Assert.IsTrue(await _store.HasTextVectorAsync("application", "eg2", CancellationToken.None));
        Assert.IsTrue(await _store.HasOcrTextVectorAsync(image, "eg2", CancellationToken.None));
        Assert.IsTrue(await _store.HasImageVectorAsync(image, "eg2", CancellationToken.None));
        Assert.AreEqual(state, await _store.GetFileStateAsync(document, IndexFileKind.Document, CancellationToken.None));
        CollectionAssert.AreEquivalent(new[] { document, image }, await ReadPathsAsync());
        await AssertStoredTextVectorsAsync(2);
        await _store.UpsertDocumentTextAsync(document, "eg2", [new float[768], new float[768]], CancellationToken.None);
        _store = new IndexVectorStore(Path.Combine(_directory, "index.db"));
        await AssertStoredTextVectorsAsync(4);
    }

    private async Task AssertStoredTextVectorsAsync(int expected)
    {
        Assert.AreEqual(expected, (await _store.GetCountsAsync(CancellationToken.None)).TextVectors);
        await using var connection = new SqliteConnection($"Data Source={Path.Combine(_directory, "index.db")}");
        await connection.OpenAsync();
        connection.EnableExtensions(true);
        connection.LoadVector();
        connection.EnableExtensions(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM index_text_vectors;";
        Assert.AreEqual(expected, Convert.ToInt32(await command.ExecuteScalarAsync()));
    }

    private async Task<string[]> ReadPathsAsync()
    {
        var paths = new List<string>();
        await foreach (var path in _store.EnumerateManagedFilePathsAsync())
        {
            paths.Add(path);
        }

        return paths.ToArray();
    }

    private static IEnumerable<string> ThrowAfterFirst(string path)
    {
        yield return path;
        throw new IOException("Simulated discovery failure.");
    }

    private async Task ReplacePathTablesWithLegacyDefinitionsAsync()
    {
        await using var connection = new SqliteConnection($"Data Source={Path.Combine(_directory, "index.db")}");
        await connection.OpenAsync();
        await ExecuteAsync(connection, """
            ALTER TABLE index_text_metadata RENAME TO index_text_metadata_current;
            CREATE TABLE index_text_metadata (
                key TEXT NOT NULL PRIMARY KEY,
                model_id TEXT NOT NULL,
                dimensions INTEGER NOT NULL,
                vector_rowid INTEGER NOT NULL,
                content_kind INTEGER NOT NULL DEFAULT 0,
                updated_at INTEGER NOT NULL
            );
            INSERT INTO index_text_metadata
            SELECT key, model_id, dimensions, vector_rowid, content_kind, updated_at
            FROM index_text_metadata_current;
            DROP TABLE index_text_metadata_current;
            ALTER TABLE index_file_states RENAME TO index_file_states_current;
            CREATE TABLE index_file_states (
                path TEXT NOT NULL PRIMARY KEY,
                file_kind INTEGER NOT NULL,
                length INTEGER NOT NULL,
                last_write_utc_ticks INTEGER NOT NULL,
                content_hash TEXT NOT NULL,
                ocr_completed INTEGER NOT NULL DEFAULT 0,
                ocr_model_id TEXT NULL,
                updated_at INTEGER NOT NULL
            );
            INSERT INTO index_file_states SELECT * FROM index_file_states_current;
            DROP TABLE index_file_states_current;
            """);
    }

    private async Task AssertNoCasePrimaryKeyAsync(string table)
    {
        await using var connection = new SqliteConnection($"Data Source={Path.Combine(_directory, "index.db")}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = $table;";
        command.Parameters.AddWithValue("$table", table);
        var definition = (string)(await command.ExecuteScalarAsync() ?? string.Empty);
        StringAssert.Contains(definition, "COLLATE NOCASE", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
