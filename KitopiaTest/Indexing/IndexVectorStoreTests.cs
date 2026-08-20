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
        await _store.UpsertDocumentTextAsync(document, textModel, new float[512], CancellationToken.None);
        await _store.UpsertOcrTextAsync(image, textModel, new float[512], CancellationToken.None);
        await _store.UpsertImageAsync(image, "1:1", imageModel, new float[1024], CancellationToken.None);
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
        await _store.UpsertTextAsync(document, model, new float[512], CancellationToken.None);

        await _store.SynchronizeFileSourceAsync(
            IndexSource.Manual, [], NoProtectedKeys, CancellationToken.None);

        Assert.AreEqual((0, 0), await _store.GetCountsAsync(CancellationToken.None));
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
        await _store.UpsertDocumentTextAsync(document, textModel, new float[512], CancellationToken.None);
        await _store.UpsertImageAsync(image, "1:1", imageModel, new float[1024], CancellationToken.None);
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

        await _store.UpsertOcrTextAsync(image, model, new float[512], CancellationToken.None);

        Assert.IsTrue(await _store.HasCompletedOcrForContentHashAsync(contentHash, model, CancellationToken.None));
    }

    [TestMethod]
    public async Task OpeningLegacyDatabase_MigratesPathPrimaryKeysToNoCase()
    {
        var image = Path.Combine(_directory, "Screen.PNG");
        var alternateCasing = Path.Combine(_directory, "screen.png");
        const string model = "text-model";
        const string hash = "image-hash";
        await _store.UpsertOcrTextAsync(image, model, new float[512], CancellationToken.None);
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
            INSERT INTO index_text_metadata SELECT * FROM index_text_metadata_current;
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
