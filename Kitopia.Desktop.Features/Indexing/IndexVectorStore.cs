using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Text;
using Kitopia.Desktop.Features.Utils;
using Microsoft.Data.Sqlite;

namespace Kitopia.Desktop.Features.Indexing;

/// <summary>
/// New, independent sqlite-vec database for the unified index. It deliberately does not
/// open, read, or migrate the legacy search-rag.db database.
/// </summary>
internal sealed class IndexVectorStore
{
    private const string TextVectorTable = "index_text_vectors";
    private const string TextMetadataTable = "index_text_metadata";
    private const string ImageVectorTable = "index_image_vectors";
    private const string ImageMetadataTable = "index_image_metadata";
    private const string FileStateTable = "index_file_states";
    private const string FileSourceTable = "index_file_sources";
    private const string FileSourceScanTable = "index_file_source_scans";
    private const string FileSourceStagingTable = "index_file_source_staging";
    private const int FileSourceBatchSize = 256;
    private const int FileSourceReadBatchSize = 512;
    private static readonly StringComparer FilePathComparer =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private static string FilePathCollation => OperatingSystem.IsWindows() ? " COLLATE NOCASE" : string.Empty;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _connectionString;
    private bool _initialized;
    private long _fileSourceGeneration;

    public IndexVectorStore(string? databasePath = null)
    {
        databasePath ??= Path.Combine(KitopiaPaths.AppRoot, "index.db");
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
    }

    public async Task<bool> SynchronizeFileSourceAsync(
        IndexSource source,
        IEnumerable<string> paths,
        IReadOnlySet<string> protectedKeys,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(protectedKeys);
        ValidateFileSource(source);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(cancellationToken);
            var generation = Math.Max(DateTime.UtcNow.Ticks, _fileSourceGeneration + 1);
            _fileSourceGeneration = generation;
            await ClearFileSourceStagingAsync(connection, source, cancellationToken);
            var batch = new List<string>(FileSourceBatchSize);
            var batchKeys = new HashSet<string>(FilePathComparer);
            foreach (var path in paths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                string normalizedPath;
                try
                {
                    normalizedPath = Path.GetFullPath(path);
                }
                catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
                {
                    continue;
                }

                if (!batchKeys.Add(normalizedPath))
                {
                    continue;
                }

                batch.Add(normalizedPath);
                if (batch.Count < FileSourceBatchSize)
                {
                    continue;
                }

                await InsertFileSourceStagingBatchAsync(connection, source, batch, cancellationToken);
                batch.Clear();
                batchKeys.Clear();
            }

            if (batch.Count > 0)
            {
                await InsertFileSourceStagingBatchAsync(connection, source, batch, cancellationToken);
            }

            return await CompleteFileSourceScanAsync(
                connection,
                source,
                generation,
                protectedKeys,
                cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async IAsyncEnumerable<string> EnumerateManagedFilePathsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        string? lastPath = null;
        while (true)
        {
            var batch = await ReadManagedFilePathBatchAsync(lastPath, cancellationToken);

            if (batch.Count == 0)
            {
                yield break;
            }

            foreach (var path in batch)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return path;
            }

            lastPath = batch[^1];
        }
    }

    public IEnumerable<string> EnumerateManagedFilePaths(CancellationToken cancellationToken)
    {
        string? lastPath = null;
        while (true)
        {
            var batch = ReadManagedFilePathBatchAsync(lastPath, cancellationToken)
                .ConfigureAwait(false)
                .GetAwaiter()
                .GetResult();
            if (batch.Count == 0)
            {
                yield break;
            }

            foreach (var path in batch)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return path;
            }

            lastPath = batch[^1];
        }
    }

    private async Task<List<string>> ReadManagedFilePathBatchAsync(
        string? lastPath,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                SELECT file.path
                FROM {FileSourceTable} AS file
                INNER JOIN {FileSourceScanTable} AS scan
                    ON scan.source = file.source
                   AND scan.generation = file.scan_generation
                WHERE $lastPath IS NULL OR file.path > $lastPath{FilePathCollation}
                GROUP BY file.path{FilePathCollation}
                ORDER BY file.path{FilePathCollation}
                LIMIT {FileSourceReadBatchSize};
                """;
            command.Parameters.AddWithValue("$lastPath", (object?)lastPath ?? DBNull.Value);
            var batch = new List<string>(FileSourceReadBatchSize);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                batch.Add(reader.GetString(0));
            }

            return batch;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> ContainsManagedFilePathAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                SELECT 1
                FROM {FileSourceTable} AS file
                INNER JOIN {FileSourceScanTable} AS scan
                    ON scan.source = file.source
                   AND scan.generation = file.scan_generation
                WHERE file.path = $path{FilePathCollation}
                LIMIT 1;
                """;
            command.Parameters.AddWithValue("$path", path);
            return await command.ExecuteScalarAsync(cancellationToken) is not null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public bool ContainsManagedFilePath(string path)
    {
        _gate.Wait();
        try
        {
            using var connection = OpenAsync(CancellationToken.None).GetAwaiter().GetResult();
            using var command = connection.CreateCommand();
            command.CommandText = $"""
                SELECT 1
                FROM {FileSourceTable} AS file
                INNER JOIN {FileSourceScanTable} AS scan
                    ON scan.source = file.source
                   AND scan.generation = file.scan_generation
                WHERE file.path = $path{FilePathCollation}
                LIMIT 1;
                """;
            command.Parameters.AddWithValue("$path", path);
            return command.ExecuteScalar() is not null;
        }
        catch (SqliteException)
        {
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<(int Total, int Documents, int Images)> GetManagedFileCountsAsync(
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                SELECT
                    COUNT(*),
                    COALESCE(SUM(CASE WHEN lower(path) LIKE '%.txt'
                                           OR lower(path) LIKE '%.md'
                                           OR lower(path) LIKE '%.pdf'
                                           OR lower(path) LIKE '%.doc'
                                           OR lower(path) LIKE '%.docx'
                                           OR lower(path) LIKE '%.xls'
                                           OR lower(path) LIKE '%.xlsx'
                                           OR lower(path) LIKE '%.ppt'
                                           OR lower(path) LIKE '%.pptx' THEN 1 ELSE 0 END), 0),
                    COALESCE(SUM(CASE WHEN lower(path) LIKE '%.jpg'
                                           OR lower(path) LIKE '%.jpeg'
                                           OR lower(path) LIKE '%.png'
                                           OR lower(path) LIKE '%.bmp'
                                           OR lower(path) LIKE '%.webp' THEN 1 ELSE 0 END), 0)
                FROM (
                    SELECT file.path
                    FROM {FileSourceTable} AS file
                    INNER JOIN {FileSourceScanTable} AS scan
                        ON scan.source = file.source
                       AND scan.generation = file.scan_generation
                    GROUP BY file.path{FilePathCollation}
                );
                """;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            return await reader.ReadAsync(cancellationToken)
                ? (Convert.ToInt32(reader.GetValue(0)), Convert.ToInt32(reader.GetValue(1)), Convert.ToInt32(reader.GetValue(2)))
                : (0, 0, 0);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task UpsertTextAsync(string key, string modelId, float[] vector, CancellationToken cancellationToken) =>
        UpsertVectorAsync(TextVectorTable, TextMetadataTable, key, null, modelId, vector, TextContentKind.Entry, cancellationToken);

    public Task UpsertOcrTextAsync(string key, string modelId, float[] vector, CancellationToken cancellationToken) =>
        UpsertVectorAsync(TextVectorTable, TextMetadataTable, key, null, modelId, vector, TextContentKind.ImageOcr, cancellationToken);

    public Task UpsertDocumentTextAsync(string key, string modelId, float[] vector, CancellationToken cancellationToken) =>
        UpsertVectorAsync(TextVectorTable, TextMetadataTable, key, null, modelId, vector, TextContentKind.Document, cancellationToken);

    public async Task DeleteOcrTextAsync(string key, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(cancellationToken);
            using var transaction = connection.BeginTransaction();
            await DeleteMappedTextVectorIfKindAsync(
                connection,
                transaction,
                key,
                TextContentKind.ImageOcr,
                cancellationToken);
            transaction.Commit();
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task UpsertImageAsync(
        string path,
        string fingerprint,
        string modelId,
        float[] vector,
        CancellationToken cancellationToken) =>
        UpsertVectorAsync(ImageVectorTable, ImageMetadataTable, path, fingerprint, modelId, vector, null, cancellationToken);

    public async Task<FileIndexState?> GetFileStateAsync(
        string path,
        IndexFileKind kind,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                SELECT length, last_write_utc_ticks, content_hash, ocr_completed, ocr_model_id
                FROM {FileStateTable}
                WHERE path = $path AND file_kind = $kind;
                """;
            command.Parameters.AddWithValue("$path", path);
            command.Parameters.AddWithValue("$kind", (int)kind);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            return new FileIndexState(
                path,
                kind,
                reader.GetInt64(0),
                reader.GetInt64(1),
                reader.GetString(2),
                reader.GetInt64(3) != 0,
                reader.IsDBNull(4) ? null : reader.GetString(4));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpsertFileStateAsync(FileIndexState state, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                INSERT INTO {FileStateTable}(
                    path, file_kind, length, last_write_utc_ticks, content_hash, ocr_completed, ocr_model_id, updated_at)
                VALUES($path, $kind, $length, $lastWriteUtcTicks, $contentHash, $ocrCompleted, $ocrModelId, unixepoch())
                ON CONFLICT(path) DO UPDATE SET
                    file_kind = excluded.file_kind,
                    length = excluded.length,
                    last_write_utc_ticks = excluded.last_write_utc_ticks,
                    content_hash = excluded.content_hash,
                    ocr_completed = excluded.ocr_completed,
                    ocr_model_id = excluded.ocr_model_id,
                    updated_at = excluded.updated_at;
                """;
            command.Parameters.AddWithValue("$path", state.Path);
            command.Parameters.AddWithValue("$kind", (int)state.Kind);
            command.Parameters.AddWithValue("$length", state.Length);
            command.Parameters.AddWithValue("$lastWriteUtcTicks", state.LastWriteUtcTicks);
            command.Parameters.AddWithValue("$contentHash", state.ContentHash);
            command.Parameters.AddWithValue("$ocrCompleted", state.OcrCompleted ? 1 : 0);
            command.Parameters.AddWithValue("$ocrModelId", (object?)state.OcrModelId ?? DBNull.Value);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<bool> HasTextVectorAsync(string key, string modelId, CancellationToken cancellationToken) =>
        HasVectorAsync(TextMetadataTable, "key", key, modelId, cancellationToken);

    public Task<bool> HasOcrTextVectorAsync(string key, string modelId, CancellationToken cancellationToken) =>
        HasTextVectorOfKindAsync(key, modelId, TextContentKind.ImageOcr, cancellationToken);

    public Task<bool> HasImageVectorAsync(string path, string modelId, CancellationToken cancellationToken) =>
        HasVectorAsync(ImageMetadataTable, "path", path, modelId, cancellationToken);

    public Task<bool> TryCopyDocumentTextForContentHashAsync(
        string destinationKey,
        string contentHash,
        string modelId,
        CancellationToken cancellationToken) =>
        TryCopyTextVectorForContentHashAsync(
            destinationKey,
            contentHash,
            modelId,
            [TextContentKind.Document, TextContentKind.Entry],
            TextContentKind.Document,
            cancellationToken);

    public Task<bool> TryCopyImageVectorForContentHashAsync(
        string destinationPath,
        string fingerprint,
        string contentHash,
        string modelId,
        CancellationToken cancellationToken) =>
        TryCopyImageVectorForContentHashCoreAsync(destinationPath, fingerprint, contentHash, modelId, cancellationToken);

    public Task<bool> TryCopyOcrTextForContentHashAsync(
        string destinationKey,
        string contentHash,
        string modelId,
        CancellationToken cancellationToken) =>
        TryCopyTextVectorForContentHashAsync(
            destinationKey,
            contentHash,
            modelId,
            [TextContentKind.ImageOcr],
            TextContentKind.ImageOcr,
            cancellationToken);

    public async Task<bool> HasCompletedOcrForContentHashAsync(
        string contentHash,
        string modelId,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(cancellationToken);
            await using var completion = connection.CreateCommand();
            completion.CommandText = $"""
                SELECT 1
                FROM {FileStateTable}
                INNER JOIN {TextMetadataTable} AS metadata
                    ON metadata.key = {FileStateTable}.path{FilePathCollation}
                   AND metadata.content_kind = $ocrKind
                   AND metadata.model_id = $modelId
                INNER JOIN {TextVectorTable} AS vector
                    ON vector.rowid = metadata.vector_rowid
                WHERE {FileStateTable}.file_kind = $kind
                  AND {FileStateTable}.content_hash = $contentHash
                  AND {FileStateTable}.ocr_completed = 1
                  AND {FileStateTable}.ocr_model_id = $modelId
                  AND vector.model_id = $modelId
                LIMIT 1;
                """;
            completion.Parameters.AddWithValue("$kind", (int)IndexFileKind.Image);
            completion.Parameters.AddWithValue("$ocrKind", (int)TextContentKind.ImageOcr);
            completion.Parameters.AddWithValue("$contentHash", contentHash);
            completion.Parameters.AddWithValue("$modelId", modelId);
            return await completion.ExecuteScalarAsync(cancellationToken) is not null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<IReadOnlyList<VectorMatch>> SearchImagesAsync(
        string modelId,
        ReadOnlyMemory<float> query,
        int maximumResults,
        CancellationToken cancellationToken) =>
        SearchAsync(ImageVectorTable, ImageMetadataTable, "path", modelId, query, maximumResults, cancellationToken);

    public Task<IReadOnlyList<VectorMatch>> SearchTextAsync(
        string modelId,
        ReadOnlyMemory<float> query,
        int maximumResults,
        CancellationToken cancellationToken) =>
        SearchAsync(TextVectorTable, TextMetadataTable, "key", modelId, query, maximumResults, cancellationToken);

    public async Task<bool> IsCurrentImageAsync(
        string path,
        string fingerprint,
        string modelId,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                SELECT 1 FROM {ImageMetadataTable}
                WHERE path = $path AND fingerprint = $fingerprint AND model_id = $modelId;
                """;
            command.Parameters.AddWithValue("$path", path);
            command.Parameters.AddWithValue("$fingerprint", fingerprint);
            command.Parameters.AddWithValue("$modelId", modelId);
            return await command.ExecuteScalarAsync(cancellationToken) is not null;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<bool> HasVectorAsync(
        string metadataTable,
        string keyColumn,
        string key,
        string modelId,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT 1 FROM {metadataTable} WHERE {keyColumn} = $key AND model_id = $modelId;";
            command.Parameters.AddWithValue("$key", key);
            command.Parameters.AddWithValue("$modelId", modelId);
            return await command.ExecuteScalarAsync(cancellationToken) is not null;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<bool> HasTextVectorOfKindAsync(
        string key,
        string modelId,
        TextContentKind contentKind,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                SELECT 1
                FROM {TextMetadataTable} AS metadata
                INNER JOIN {TextVectorTable} AS vector ON vector.rowid = metadata.vector_rowid
                WHERE metadata.key = $key
                  AND metadata.model_id = $modelId
                  AND metadata.content_kind = $contentKind
                  AND vector.model_id = $modelId
                LIMIT 1;
                """;
            command.Parameters.AddWithValue("$key", key);
            command.Parameters.AddWithValue("$modelId", modelId);
            command.Parameters.AddWithValue("$contentKind", (int)contentKind);
            return await command.ExecuteScalarAsync(cancellationToken) is not null;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<bool> TryCopyTextVectorForContentHashAsync(
        string destinationKey,
        string contentHash,
        string modelId,
        IReadOnlyList<TextContentKind> sourceKinds,
        TextContentKind destinationKind,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(cancellationToken);
            return await TryCopyTextVectorForContentHashCoreAsync(
                connection,
                destinationKey,
                contentHash,
                modelId,
                sourceKinds,
                destinationKind,
                cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static async Task<bool> TryCopyTextVectorForContentHashCoreAsync(
        SqliteConnection connection,
        string destinationKey,
        string contentHash,
        string modelId,
        IReadOnlyList<TextContentKind> sourceKinds,
        TextContentKind destinationKind,
        CancellationToken cancellationToken)
    {
        var kinds = string.Join(',', sourceKinds.Select(kind => (int)kind));
        long? sourceRowId;
        await using (var source = connection.CreateCommand())
        {
            source.CommandText = $"""
                SELECT metadata.vector_rowid
                FROM {FileStateTable} AS state
                INNER JOIN {TextMetadataTable} AS metadata ON metadata.key = state.path
                WHERE state.content_hash = $contentHash
                  AND metadata.model_id = $modelId
                  AND metadata.content_kind IN ({kinds})
                  AND metadata.key <> $destinationKey
                LIMIT 1;
                """;
            source.Parameters.AddWithValue("$contentHash", contentHash);
            source.Parameters.AddWithValue("$modelId", modelId);
            source.Parameters.AddWithValue("$destinationKey", destinationKey);
            sourceRowId = await source.ExecuteScalarAsync(cancellationToken) is { } value && value is not DBNull
                ? Convert.ToInt64(value)
                : null;
        }

        if (sourceRowId is null)
        {
            return false;
        }

        using var transaction = connection.BeginTransaction();
        await DeleteMappedVectorAsync(
            connection,
            transaction,
            TextVectorTable,
            TextMetadataTable,
            "key",
            destinationKey,
            cancellationToken);
        var vectorRowId = await CopyVectorAsync(
            connection,
            transaction,
            TextVectorTable,
            modelId,
            sourceRowId.Value,
            cancellationToken);
        await UpsertMetadataAsync(
            connection,
            transaction,
            TextMetadataTable,
            "key",
            destinationKey,
            null,
            modelId,
            512,
            vectorRowId,
            destinationKind,
            cancellationToken);
        transaction.Commit();
        return true;
    }

    private async Task<bool> TryCopyImageVectorForContentHashCoreAsync(
        string destinationPath,
        string fingerprint,
        string contentHash,
        string modelId,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(cancellationToken);
            long? sourceRowId;
            await using (var source = connection.CreateCommand())
            {
                source.CommandText = $"""
                    SELECT metadata.vector_rowid
                    FROM {FileStateTable} AS state
                    INNER JOIN {ImageMetadataTable} AS metadata ON metadata.path = state.path
                    WHERE state.file_kind = $kind
                      AND state.content_hash = $contentHash
                      AND metadata.model_id = $modelId
                      AND metadata.path <> $destinationPath
                    LIMIT 1;
                    """;
                source.Parameters.AddWithValue("$kind", (int)IndexFileKind.Image);
                source.Parameters.AddWithValue("$contentHash", contentHash);
                source.Parameters.AddWithValue("$modelId", modelId);
                source.Parameters.AddWithValue("$destinationPath", destinationPath);
                sourceRowId = await source.ExecuteScalarAsync(cancellationToken) is { } value && value is not DBNull
                    ? Convert.ToInt64(value)
                    : null;
            }

            if (sourceRowId is null)
            {
                return false;
            }

            using var transaction = connection.BeginTransaction();
            await DeleteMappedVectorAsync(
                connection,
                transaction,
                ImageVectorTable,
                ImageMetadataTable,
                "path",
                destinationPath,
                cancellationToken);
            var vectorRowId = await CopyVectorAsync(
                connection,
                transaction,
                ImageVectorTable,
                modelId,
                sourceRowId.Value,
                cancellationToken);
            await UpsertMetadataAsync(
                connection,
                transaction,
                ImageMetadataTable,
                "path",
                destinationPath,
                fingerprint,
                modelId,
                1024,
                vectorRowId,
                null,
                cancellationToken);
            transaction.Commit();
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(cancellationToken);
            using var transaction = connection.BeginTransaction();
            await DeleteMappedVectorAsync(connection, transaction, TextVectorTable, TextMetadataTable, "key", key, cancellationToken);
            await DeleteMappedVectorAsync(connection, transaction, ImageVectorTable, ImageMetadataTable, "path", key, cancellationToken);
            await using var stateDelete = connection.CreateCommand();
            stateDelete.Transaction = transaction;
            stateDelete.CommandText = $"DELETE FROM {FileStateTable} WHERE path = $path;";
            stateDelete.Parameters.AddWithValue("$path", key);
            await stateDelete.ExecuteNonQueryAsync(cancellationToken);
            transaction.Commit();
        }
        finally
        {
            _gate.Release();
        }
    }

    public void DeleteIfUnreferenced(string key)
    {
        _gate.Wait();
        try
        {
            using var connection = OpenAsync(CancellationToken.None).GetAwaiter().GetResult();
            using var transaction = connection.BeginTransaction();
            using (var managed = connection.CreateCommand())
            {
                managed.Transaction = transaction;
                managed.CommandText = $"""
                    SELECT 1
                    FROM {FileSourceTable} AS file
                    INNER JOIN {FileSourceScanTable} AS scan
                        ON scan.source = file.source
                       AND scan.generation = file.scan_generation
                    WHERE file.path = $path{FilePathCollation}
                    LIMIT 1;
                    """;
                managed.Parameters.AddWithValue("$path", key);
                if (managed.ExecuteScalar() is not null)
                {
                    transaction.Rollback();
                    return;
                }
            }

            using var delete = connection.CreateCommand();
            delete.Transaction = transaction;
            delete.CommandText = $"""
                DELETE FROM {TextVectorTable}
                WHERE rowid IN (SELECT vector_rowid FROM {TextMetadataTable} WHERE key = $key);
                DELETE FROM {TextMetadataTable} WHERE key = $key;
                DELETE FROM {ImageVectorTable}
                WHERE rowid IN (SELECT vector_rowid FROM {ImageMetadataTable} WHERE path = $key);
                DELETE FROM {ImageMetadataTable} WHERE path = $key;
                DELETE FROM {FileStateTable} WHERE path = $key;
                """;
            delete.Parameters.AddWithValue("$key", key);
            delete.ExecuteNonQuery();
            transaction.Commit();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ClearAsync(IndexRebuildScope scope, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(cancellationToken);
            if (scope is IndexRebuildScope.All or IndexRebuildScope.Documents)
            {
                await DeleteTextByKindAsync(connection, TextContentKind.Entry, cancellationToken);
                await DeleteTextByKindAsync(connection, TextContentKind.Document, cancellationToken);
                await DeleteFileStatesAsync(connection, IndexFileKind.Document, cancellationToken);
            }

            if (scope is IndexRebuildScope.All or IndexRebuildScope.Images)
            {
                await ExecuteAsync(connection, $"DELETE FROM {ImageMetadataTable};", cancellationToken);
                await ExecuteAsync(connection, $"DELETE FROM {ImageVectorTable};", cancellationToken);
                await DeleteTextByKindAsync(connection, TextContentKind.ImageOcr, cancellationToken);
                await DeleteFileStatesAsync(connection, IndexFileKind.Image, cancellationToken);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ResetAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(cancellationToken);
            await ExecuteAsync(connection, $"""
                DELETE FROM {TextVectorTable};
                DELETE FROM {TextMetadataTable};
                DELETE FROM {ImageMetadataTable};
                DELETE FROM {ImageVectorTable};
                DELETE FROM {FileStateTable};
                DELETE FROM {FileSourceStagingTable};
                DELETE FROM {FileSourceScanTable};
                DELETE FROM {FileSourceTable};
                """, cancellationToken);
            _fileSourceGeneration = 0;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<(int TextVectors, int ImageVectors)> GetCountsAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(cancellationToken);
            return (
                await CountAsync(connection, TextMetadataTable, cancellationToken),
                await CountAsync(connection, ImageMetadataTable, cancellationToken));
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task UpsertVectorAsync(
        string vectorTable,
        string metadataTable,
        string key,
        string? fingerprint,
        string modelId,
        float[] vector,
        TextContentKind? textContentKind,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(cancellationToken);
            using var transaction = connection.BeginTransaction();
            var keyColumn = metadataTable == ImageMetadataTable ? "path" : "key";
            await DeleteMappedVectorAsync(connection, transaction, vectorTable, metadataTable, keyColumn, key, cancellationToken);
            var vectorRowId = await InsertVectorAsync(connection, transaction, vectorTable, modelId, vector, cancellationToken);
            await UpsertMetadataAsync(
                connection,
                transaction,
                metadataTable,
                keyColumn,
                key,
                fingerprint,
                modelId,
                vector.Length,
                vectorRowId,
                textContentKind,
                cancellationToken);
            transaction.Commit();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IReadOnlyList<VectorMatch>> SearchAsync(
        string vectorTable,
        string metadataTable,
        string keyColumn,
        string modelId,
        ReadOnlyMemory<float> query,
        int maximumResults,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                SELECT metadata.{keyColumn}, vectors.distance
                FROM {vectorTable} AS vectors
                INNER JOIN {metadataTable} AS metadata ON metadata.vector_rowid = vectors.rowid
                WHERE vectors.embedding MATCH $queryVector
                  AND k = $maximumResults
                  AND vectors.model_id = $modelId
                  AND metadata.dimensions = $dimensions
                ORDER BY vectors.distance;
                """;
            command.Parameters.Add("$queryVector", SqliteType.Blob).Value = ToBlob(query.Span);
            command.Parameters.AddWithValue("$maximumResults", Math.Max(1, maximumResults));
            command.Parameters.AddWithValue("$modelId", modelId);
            command.Parameters.AddWithValue("$dimensions", query.Length);
            var matches = new List<VectorMatch>(maximumResults);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                matches.Add(new VectorMatch(reader.GetString(0), 1d - reader.GetDouble(1)));
            }

            return matches;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            connection.EnableExtensions(true);
            connection.LoadVector();
            connection.EnableExtensions(false);
            if (!_initialized)
            {
                await ExecuteAsync(connection, "PRAGMA journal_mode = WAL;", cancellationToken);
                await ExecuteAsync(connection, $"CREATE VIRTUAL TABLE IF NOT EXISTS {TextVectorTable} USING vec0(embedding float[512] distance_metric=cosine, model_id TEXT PARTITION KEY);", cancellationToken);
                await ExecuteAsync(connection, $"CREATE VIRTUAL TABLE IF NOT EXISTS {ImageVectorTable} USING vec0(embedding float[1024] distance_metric=cosine, model_id TEXT PARTITION KEY);", cancellationToken);
                await ExecuteAsync(connection, $"""
                    CREATE TABLE IF NOT EXISTS {TextMetadataTable} (
                        key TEXT NOT NULL PRIMARY KEY{FilePathCollation},
                        model_id TEXT NOT NULL,
                        dimensions INTEGER NOT NULL,
                        vector_rowid INTEGER NOT NULL,
                        content_kind INTEGER NOT NULL DEFAULT 0,
                        updated_at INTEGER NOT NULL
                    );
                    """, cancellationToken);
                await EnsureTextContentKindColumnAsync(connection, cancellationToken);
                await ExecuteAsync(connection, $"""
                    CREATE TABLE IF NOT EXISTS {ImageMetadataTable} (
                        path TEXT NOT NULL PRIMARY KEY{FilePathCollation},
                        fingerprint TEXT NOT NULL,
                        model_id TEXT NOT NULL,
                        dimensions INTEGER NOT NULL,
                        vector_rowid INTEGER NOT NULL,
                        updated_at INTEGER NOT NULL
                    );
                    """, cancellationToken);
                await ExecuteAsync(connection, $"""
                    CREATE TABLE IF NOT EXISTS {FileStateTable} (
                        path TEXT NOT NULL PRIMARY KEY{FilePathCollation},
                        file_kind INTEGER NOT NULL,
                        length INTEGER NOT NULL,
                        last_write_utc_ticks INTEGER NOT NULL,
                        content_hash TEXT NOT NULL,
                        ocr_completed INTEGER NOT NULL DEFAULT 0,
                        ocr_model_id TEXT NULL,
                        updated_at INTEGER NOT NULL
                    );
                    """, cancellationToken);
                await ExecuteAsync(connection, $"""
                    CREATE TABLE IF NOT EXISTS {FileSourceTable} (
                        source INTEGER NOT NULL,
                        path TEXT NOT NULL{FilePathCollation},
                        scan_generation INTEGER NOT NULL,
                        PRIMARY KEY(source, path)
                    );
                    """, cancellationToken);
                await ExecuteAsync(connection, $"""
                    CREATE TABLE IF NOT EXISTS {FileSourceScanTable} (
                        source INTEGER NOT NULL PRIMARY KEY,
                        generation INTEGER NOT NULL
                    );
                    """, cancellationToken);
                await ExecuteAsync(connection, $"""
                    CREATE TABLE IF NOT EXISTS {FileSourceStagingTable} (
                        source INTEGER NOT NULL,
                        path TEXT NOT NULL{FilePathCollation},
                        PRIMARY KEY(source, path)
                    );
                    """, cancellationToken);
                await ExecuteAsync(connection,
                    $"CREATE INDEX IF NOT EXISTS idx_{FileSourceTable}_path ON {FileSourceTable}(path);",
                    cancellationToken);
                if (OperatingSystem.IsWindows())
                {
                    await EnsureCaseInsensitiveTableAsync(connection, TextMetadataTable, cancellationToken);
                    await EnsureCaseInsensitiveTableAsync(connection, ImageMetadataTable, cancellationToken);
                    await EnsureCaseInsensitiveTableAsync(connection, FileStateTable, cancellationToken);
                }
                _initialized = true;
            }

            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private static void ValidateFileSource(IndexSource source)
    {
        if (source is not (IndexSource.Document or IndexSource.Image or IndexSource.Manual or IndexSource.EverythingManaged))
        {
            throw new ArgumentOutOfRangeException(nameof(source), "Only managed file sources are file-backed.");
        }
    }

    private static async Task ClearFileSourceStagingAsync(
        SqliteConnection connection,
        IndexSource source,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"DELETE FROM {FileSourceStagingTable} WHERE source = $source;";
        command.Parameters.AddWithValue("$source", (int)source);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertFileSourceStagingBatchAsync(
        SqliteConnection connection,
        IndexSource source,
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken)
    {
        using var transaction = connection.BeginTransaction();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        var values = new StringBuilder(paths.Count * 16);
        for (var index = 0; index < paths.Count; index++)
        {
            if (index > 0)
            {
                values.Append(',');
            }

            values.Append($"($source, $path{index})");
            command.Parameters.AddWithValue($"$path{index}", paths[index]);
        }

        command.CommandText = $"""
            INSERT OR IGNORE INTO {FileSourceStagingTable}(source, path)
            VALUES {values}
            """;
        command.Parameters.AddWithValue("$source", (int)source);
        await command.ExecuteNonQueryAsync(cancellationToken);
        transaction.Commit();
    }

    private static async Task<bool> CompleteFileSourceScanAsync(
        SqliteConnection connection,
        IndexSource source,
        long generation,
        IReadOnlySet<string> protectedKeys,
        CancellationToken cancellationToken)
    {
        using var transaction = connection.BeginTransaction();
        var changed = await HasFileSourceChangesAsync(connection, transaction, source, cancellationToken);
        if (!changed)
        {
            await ExecuteInTransactionAsync(connection, transaction,
                $"DELETE FROM {FileSourceStagingTable} WHERE source = $source;",
                cancellationToken,
                ("$source", (int)source));
            await ExecuteInTransactionAsync(connection, transaction, $"""
                DELETE FROM {FileSourceTable}
                WHERE source = $source
                  AND NOT EXISTS (
                      SELECT 1 FROM {FileSourceScanTable} AS scan
                      WHERE scan.source = {FileSourceTable}.source
                        AND scan.generation = {FileSourceTable}.scan_generation);
                """, cancellationToken, ("$source", (int)source));
            transaction.Commit();
            return false;
        }

        await ExecuteInTransactionAsync(connection, transaction,
            $"CREATE TEMP TABLE IF NOT EXISTS index_stale_file_paths(path TEXT{FilePathCollation} PRIMARY KEY);",
            cancellationToken);
        await ExecuteInTransactionAsync(connection, transaction,
            $"CREATE TEMP TABLE IF NOT EXISTS index_protected_file_paths(path TEXT{FilePathCollation} PRIMARY KEY);",
            cancellationToken);
        await ExecuteInTransactionAsync(connection, transaction,
            "DELETE FROM index_stale_file_paths; DELETE FROM index_protected_file_paths;",
            cancellationToken);

        await using (var stale = connection.CreateCommand())
        {
            stale.Transaction = transaction;
            stale.CommandText = $"""
                INSERT OR IGNORE INTO index_stale_file_paths(path)
                SELECT current.path
                FROM {FileSourceTable} AS current
                INNER JOIN {FileSourceScanTable} AS scan
                    ON scan.source = current.source
                   AND scan.generation = current.scan_generation
                WHERE current.source = $source
                  AND NOT EXISTS (
                      SELECT 1
                      FROM {FileSourceStagingTable} AS incoming
                      WHERE incoming.source = $source
                        AND incoming.path = current.path{FilePathCollation});
                """;
            stale.Parameters.AddWithValue("$source", (int)source);
            await stale.ExecuteNonQueryAsync(cancellationToken);
        }

        await InsertProtectedKeysAsync(connection, transaction, protectedKeys, cancellationToken);

        await using (var replaceSource = connection.CreateCommand())
        {
            replaceSource.Transaction = transaction;
            replaceSource.CommandText = $"""
                DELETE FROM {FileSourceTable} WHERE source = $source;
                INSERT INTO {FileSourceTable}(source, path, scan_generation)
                SELECT $source, path, $generation
                FROM {FileSourceStagingTable}
                WHERE source = $source;
                """;
            replaceSource.Parameters.AddWithValue("$source", (int)source);
            replaceSource.Parameters.AddWithValue("$generation", generation);
            await replaceSource.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var scan = connection.CreateCommand())
        {
            scan.Transaction = transaction;
            scan.CommandText = $"""
                INSERT INTO {FileSourceScanTable}(source, generation)
                VALUES($source, $generation)
                ON CONFLICT(source) DO UPDATE SET generation = excluded.generation;
                """;
            scan.Parameters.AddWithValue("$source", (int)source);
            scan.Parameters.AddWithValue("$generation", generation);
            await scan.ExecuteNonQueryAsync(cancellationToken);
        }

        await ExecuteInTransactionAsync(connection, transaction,
            $"DELETE FROM {FileSourceStagingTable} WHERE source = $source;",
            cancellationToken,
            ("$source", (int)source));

        // Remove only vectors belonging to paths that disappeared from every managed source.
        // Explicit application entries are protected so removing a file source cannot erase a
        // vector that is still intentionally searchable through the application index.
        await ExecuteInTransactionAsync(connection, transaction, $"""
            DELETE FROM {TextVectorTable}
            WHERE rowid IN (
                SELECT metadata.vector_rowid
                FROM {TextMetadataTable} AS metadata
                INNER JOIN index_stale_file_paths AS stale ON stale.path = metadata.key{FilePathCollation}
                WHERE metadata.content_kind IN ({(int)TextContentKind.Entry}, {(int)TextContentKind.Document}, {(int)TextContentKind.ImageOcr})
                  AND NOT EXISTS (
                      SELECT 1 FROM {FileSourceTable} AS active
                      INNER JOIN {FileSourceScanTable} AS active_scan
                          ON active_scan.source = active.source
                         AND active_scan.generation = active.scan_generation
                      WHERE active.path = metadata.key{FilePathCollation})
                  AND NOT EXISTS (
                      SELECT 1 FROM index_protected_file_paths AS protected
                      WHERE protected.path = metadata.key{FilePathCollation})
            );
            DELETE FROM {TextMetadataTable}
            WHERE content_kind IN ({(int)TextContentKind.Entry}, {(int)TextContentKind.Document}, {(int)TextContentKind.ImageOcr})
              AND key IN (SELECT path FROM index_stale_file_paths)
              AND NOT EXISTS (
                  SELECT 1 FROM {FileSourceTable} AS active
                  INNER JOIN {FileSourceScanTable} AS active_scan
                      ON active_scan.source = active.source
                     AND active_scan.generation = active.scan_generation
                  WHERE active.path = {TextMetadataTable}.key{FilePathCollation})
              AND NOT EXISTS (
                  SELECT 1 FROM index_protected_file_paths AS protected
                  WHERE protected.path = {TextMetadataTable}.key{FilePathCollation});
            DELETE FROM {ImageVectorTable}
            WHERE rowid IN (
                SELECT metadata.vector_rowid
                FROM {ImageMetadataTable} AS metadata
                INNER JOIN index_stale_file_paths AS stale ON stale.path = metadata.path{FilePathCollation}
                WHERE NOT EXISTS (
                          SELECT 1 FROM {FileSourceTable} AS active
                          INNER JOIN {FileSourceScanTable} AS active_scan
                              ON active_scan.source = active.source
                             AND active_scan.generation = active.scan_generation
                           WHERE active.path = metadata.path{FilePathCollation})
                  AND NOT EXISTS (
                          SELECT 1 FROM index_protected_file_paths AS protected
                           WHERE protected.path = metadata.path{FilePathCollation})
            );
            DELETE FROM {ImageMetadataTable}
            WHERE path IN (SELECT path FROM index_stale_file_paths)
              AND NOT EXISTS (
                  SELECT 1 FROM {FileSourceTable} AS active
                  INNER JOIN {FileSourceScanTable} AS active_scan
                      ON active_scan.source = active.source
                     AND active_scan.generation = active.scan_generation
                   WHERE active.path = {ImageMetadataTable}.path{FilePathCollation})
              AND NOT EXISTS (
                  SELECT 1 FROM index_protected_file_paths AS protected
                   WHERE protected.path = {ImageMetadataTable}.path{FilePathCollation});
            DELETE FROM {FileStateTable}
            WHERE path IN (SELECT path FROM index_stale_file_paths)
              AND NOT EXISTS (
                  SELECT 1 FROM {FileSourceTable} AS active
                  INNER JOIN {FileSourceScanTable} AS active_scan
                      ON active_scan.source = active.source
                     AND active_scan.generation = active.scan_generation
                   WHERE active.path = {FileStateTable}.path{FilePathCollation})
              AND NOT EXISTS (
                  SELECT 1 FROM index_protected_file_paths AS protected
                   WHERE protected.path = {FileStateTable}.path{FilePathCollation});
            """, cancellationToken);

        transaction.Commit();
        return true;
    }

    private static async Task<bool> HasFileSourceChangesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IndexSource source,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT
            EXISTS(
                SELECT 1
                FROM {FileSourceTable} AS current
                INNER JOIN {FileSourceScanTable} AS scan
                    ON scan.source = current.source
                   AND scan.generation = current.scan_generation
                WHERE current.source = $source
                  AND NOT EXISTS (
                      SELECT 1
                      FROM {FileSourceStagingTable} AS incoming
                      WHERE incoming.source = $source
                        AND incoming.path = current.path{FilePathCollation})
            )
            OR EXISTS(
                SELECT 1
                FROM {FileSourceStagingTable} AS incoming
                WHERE incoming.source = $source
                  AND NOT EXISTS (
                      SELECT 1
                      FROM {FileSourceTable} AS current
                      INNER JOIN {FileSourceScanTable} AS scan
                          ON scan.source = current.source
                         AND scan.generation = current.scan_generation
                      WHERE current.source = $source
                        AND current.path = incoming.path{FilePathCollation})
            );
            """;
        command.Parameters.AddWithValue("$source", (int)source);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) != 0;
    }

    private static async Task InsertProtectedKeysAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlySet<string> protectedKeys,
        CancellationToken cancellationToken)
    {
        if (protectedKeys.Count == 0)
        {
            return;
        }

        var batch = new List<string>(FileSourceBatchSize);
        foreach (var key in protectedKeys)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            batch.Add(key);
            if (batch.Count == FileSourceBatchSize)
            {
                await InsertProtectedKeyBatchAsync(connection, transaction, batch, cancellationToken);
                batch.Clear();
            }
        }

        if (batch.Count > 0)
        {
            await InsertProtectedKeyBatchAsync(connection, transaction, batch, cancellationToken);
        }
    }

    private static async Task InsertProtectedKeyBatchAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<string> keys,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        var values = new StringBuilder(keys.Count * 8);
        for (var index = 0; index < keys.Count; index++)
        {
            if (index > 0)
            {
                values.Append(',');
            }

            values.Append($"($key{index})");
            command.Parameters.AddWithValue($"$key{index}", keys[index]);
        }

        command.CommandText = $"INSERT OR IGNORE INTO index_protected_file_paths(path) VALUES {values};";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ExecuteInTransactionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<long> InsertVectorAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string vectorTable,
        string modelId,
        float[] vector,
        CancellationToken cancellationToken)
    {
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = $"INSERT INTO {vectorTable}(model_id, embedding) VALUES($modelId, $vector);";
            command.Parameters.AddWithValue("$modelId", modelId);
            command.Parameters.Add("$vector", SqliteType.Blob).Value = ToBlob(vector);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var rowIdCommand = connection.CreateCommand();
        rowIdCommand.Transaction = transaction;
        rowIdCommand.CommandText = "SELECT last_insert_rowid();";
        return Convert.ToInt64(await rowIdCommand.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task<long> CopyVectorAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string vectorTable,
        string modelId,
        long sourceRowId,
        CancellationToken cancellationToken)
    {
        byte[] embedding;
        await using (var source = connection.CreateCommand())
        {
            source.Transaction = transaction;
            source.CommandText = $"SELECT embedding FROM {vectorTable} WHERE rowid = $rowId;";
            source.Parameters.AddWithValue("$rowId", sourceRowId);
            embedding = (byte[])(await source.ExecuteScalarAsync(cancellationToken)
                                 ?? throw new InvalidOperationException("The source vector no longer exists."));
        }

        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = $"INSERT INTO {vectorTable}(model_id, embedding) VALUES($modelId, $embedding);";
            insert.Parameters.AddWithValue("$modelId", modelId);
            insert.Parameters.Add("$embedding", SqliteType.Blob).Value = embedding;
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var rowIdCommand = connection.CreateCommand();
        rowIdCommand.Transaction = transaction;
        rowIdCommand.CommandText = "SELECT last_insert_rowid();";
        return Convert.ToInt64(await rowIdCommand.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task UpsertMetadataAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string metadataTable,
        string keyColumn,
        string key,
        string? fingerprint,
        string modelId,
        int dimensions,
        long vectorRowId,
        TextContentKind? textContentKind,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        if (metadataTable == ImageMetadataTable)
        {
            command.CommandText = $"""
                INSERT INTO {metadataTable}(path, fingerprint, model_id, dimensions, vector_rowid, updated_at)
                VALUES($key, $fingerprint, $modelId, $dimensions, $vectorRowId, unixepoch())
                ON CONFLICT(path) DO UPDATE SET
                    fingerprint = excluded.fingerprint,
                    model_id = excluded.model_id,
                    dimensions = excluded.dimensions,
                    vector_rowid = excluded.vector_rowid,
                    updated_at = excluded.updated_at;
                """;
            command.Parameters.AddWithValue("$fingerprint", fingerprint ?? string.Empty);
        }
        else
        {
            command.CommandText = $"""
                INSERT INTO {metadataTable}(key, model_id, dimensions, vector_rowid, content_kind, updated_at)
                VALUES($key, $modelId, $dimensions, $vectorRowId, $contentKind, unixepoch())
                ON CONFLICT(key) DO UPDATE SET
                    model_id = excluded.model_id,
                    dimensions = excluded.dimensions,
                    vector_rowid = excluded.vector_rowid,
                    content_kind = excluded.content_kind,
                    updated_at = excluded.updated_at;
                """;
            command.Parameters.AddWithValue("$contentKind", (int)(textContentKind ?? TextContentKind.Entry));
        }

        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$modelId", modelId);
        command.Parameters.AddWithValue("$dimensions", dimensions);
        command.Parameters.AddWithValue("$vectorRowId", vectorRowId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task DeleteMappedVectorAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string vectorTable,
        string metadataTable,
        string keyColumn,
        string key,
        CancellationToken cancellationToken)
    {
        long? rowId;
        await using (var select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = $"SELECT vector_rowid FROM {metadataTable} WHERE {keyColumn} = $key;";
            select.Parameters.AddWithValue("$key", key);
            rowId = await select.ExecuteScalarAsync(cancellationToken) is { } value && value is not DBNull
                ? Convert.ToInt64(value)
                : null;
        }

        if (rowId is not null)
        {
            await using var vectorDelete = connection.CreateCommand();
            vectorDelete.Transaction = transaction;
            vectorDelete.CommandText = $"DELETE FROM {vectorTable} WHERE rowid = $rowId;";
            vectorDelete.Parameters.AddWithValue("$rowId", rowId.Value);
            await vectorDelete.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var metadataDelete = connection.CreateCommand();
        metadataDelete.Transaction = transaction;
        metadataDelete.CommandText = $"DELETE FROM {metadataTable} WHERE {keyColumn} = $key;";
        metadataDelete.Parameters.AddWithValue("$key", key);
        await metadataDelete.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task DeleteMappedTextVectorIfKindAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string key,
        TextContentKind kind,
        CancellationToken cancellationToken)
    {
        long? rowId;
        await using (var select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = $"SELECT vector_rowid FROM {TextMetadataTable} WHERE key = $key AND content_kind = $kind;";
            select.Parameters.AddWithValue("$key", key);
            select.Parameters.AddWithValue("$kind", (int)kind);
            rowId = await select.ExecuteScalarAsync(cancellationToken) is { } value && value is not DBNull
                ? Convert.ToInt64(value)
                : null;
        }

        if (rowId is null)
        {
            return;
        }

        await using (var vectorDelete = connection.CreateCommand())
        {
            vectorDelete.Transaction = transaction;
            vectorDelete.CommandText = $"DELETE FROM {TextVectorTable} WHERE rowid = $rowId;";
            vectorDelete.Parameters.AddWithValue("$rowId", rowId.Value);
            await vectorDelete.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var metadataDelete = connection.CreateCommand();
        metadataDelete.Transaction = transaction;
        metadataDelete.CommandText = $"DELETE FROM {TextMetadataTable} WHERE key = $key AND content_kind = $kind;";
        metadataDelete.Parameters.AddWithValue("$key", key);
        metadataDelete.Parameters.AddWithValue("$kind", (int)kind);
        await metadataDelete.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task EnsureTextContentKindColumnAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var exists = false;
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({TextMetadataTable});";
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (!string.Equals(reader.GetString(1), "content_kind", StringComparison.OrdinalIgnoreCase)) continue;
                exists = true;
                break;
            }
        }

        if (exists) return;
        await ExecuteAsync(connection,
            $"ALTER TABLE {TextMetadataTable} ADD COLUMN content_kind INTEGER NOT NULL DEFAULT 0;",
            cancellationToken);
    }

    private static async Task EnsureCaseInsensitiveTableAsync(
        SqliteConnection connection,
        string table,
        CancellationToken cancellationToken)
    {
        string? definition;
        await using (var definitionCommand = connection.CreateCommand())
        {
            definitionCommand.CommandText = """
                SELECT sql
                FROM sqlite_master
                WHERE type = 'table' AND name = $table;
                """;
            definitionCommand.Parameters.AddWithValue("$table", table);
            definition = await definitionCommand.ExecuteScalarAsync(cancellationToken) as string;
        }

        if (definition?.Contains("COLLATE NOCASE", StringComparison.OrdinalIgnoreCase) == true)
        {
            return;
        }

        var temporaryTable = $"{table}_case_migration";
        using var transaction = connection.BeginTransaction();
        await ExecuteInTransactionAsync(connection, transaction, $"DROP TABLE IF EXISTS {temporaryTable};", cancellationToken);

        var createTable = table switch
        {
            TextMetadataTable => $"""
                CREATE TABLE {temporaryTable} (
                    key TEXT NOT NULL PRIMARY KEY COLLATE NOCASE,
                    model_id TEXT NOT NULL,
                    dimensions INTEGER NOT NULL,
                    vector_rowid INTEGER NOT NULL,
                    content_kind INTEGER NOT NULL DEFAULT 0,
                    updated_at INTEGER NOT NULL
                );
                """,
            ImageMetadataTable => $"""
                CREATE TABLE {temporaryTable} (
                    path TEXT NOT NULL PRIMARY KEY COLLATE NOCASE,
                    fingerprint TEXT NOT NULL,
                    model_id TEXT NOT NULL,
                    dimensions INTEGER NOT NULL,
                    vector_rowid INTEGER NOT NULL,
                    updated_at INTEGER NOT NULL
                );
                """,
            FileStateTable => $"""
                CREATE TABLE {temporaryTable} (
                    path TEXT NOT NULL PRIMARY KEY COLLATE NOCASE,
                    file_kind INTEGER NOT NULL,
                    length INTEGER NOT NULL,
                    last_write_utc_ticks INTEGER NOT NULL,
                    content_hash TEXT NOT NULL,
                    ocr_completed INTEGER NOT NULL DEFAULT 0,
                    ocr_model_id TEXT NULL,
                    updated_at INTEGER NOT NULL
                );
                """,
            _ => throw new ArgumentException($"Unsupported case migration table '{table}'.", nameof(table))
        };
        await ExecuteInTransactionAsync(connection, transaction, createTable, cancellationToken);

        var columns = table switch
        {
            TextMetadataTable => "key, model_id, dimensions, vector_rowid, content_kind, updated_at",
            ImageMetadataTable => "path, fingerprint, model_id, dimensions, vector_rowid, updated_at",
            FileStateTable => "path, file_kind, length, last_write_utc_ticks, content_hash, ocr_completed, ocr_model_id, updated_at",
            _ => throw new ArgumentException($"Unsupported case migration table '{table}'.", nameof(table))
        };
        await ExecuteInTransactionAsync(
            connection,
            transaction,
            $"INSERT OR REPLACE INTO {temporaryTable}({columns}) SELECT {columns} FROM {table} ORDER BY updated_at, rowid;",
            cancellationToken);

        if (table == TextMetadataTable)
        {
            await ExecuteInTransactionAsync(connection, transaction,
                $"DELETE FROM {TextVectorTable} WHERE rowid NOT IN (SELECT vector_rowid FROM {temporaryTable});",
                cancellationToken);
        }
        else if (table == ImageMetadataTable)
        {
            await ExecuteInTransactionAsync(connection, transaction,
                $"DELETE FROM {ImageVectorTable} WHERE rowid NOT IN (SELECT vector_rowid FROM {temporaryTable});",
                cancellationToken);
        }

        await ExecuteInTransactionAsync(connection, transaction, $"DROP TABLE {table};", cancellationToken);
        await ExecuteInTransactionAsync(connection, transaction, $"ALTER TABLE {temporaryTable} RENAME TO {table};", cancellationToken);
        transaction.Commit();
    }

    private static async Task DeleteTextByKindAsync(
        SqliteConnection connection,
        TextContentKind contentKind,
        CancellationToken cancellationToken)
    {
        using var transaction = connection.BeginTransaction();
        await using (var vectorDelete = connection.CreateCommand())
        {
            vectorDelete.Transaction = transaction;
            vectorDelete.CommandText = $"""
                DELETE FROM {TextVectorTable}
                WHERE rowid IN (
                    SELECT vector_rowid FROM {TextMetadataTable} WHERE content_kind = $contentKind
                );
                """;
            vectorDelete.Parameters.AddWithValue("$contentKind", (int)contentKind);
            await vectorDelete.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var metadataDelete = connection.CreateCommand())
        {
            metadataDelete.Transaction = transaction;
            metadataDelete.CommandText = $"DELETE FROM {TextMetadataTable} WHERE content_kind = $contentKind;";
            metadataDelete.Parameters.AddWithValue("$contentKind", (int)contentKind);
            await metadataDelete.ExecuteNonQueryAsync(cancellationToken);
        }

        transaction.Commit();
    }

    private static async Task DeleteFileStatesAsync(
        SqliteConnection connection,
        IndexFileKind kind,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"DELETE FROM {FileStateTable} WHERE file_kind = $kind;";
        command.Parameters.AddWithValue("$kind", (int)kind);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<int> CountAsync(SqliteConnection connection, string table, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table};";
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static byte[] ToBlob(ReadOnlySpan<float> values) => MemoryMarshal.AsBytes(values).ToArray();
}

internal sealed record VectorMatch(string Key, double Score);

internal sealed record FileIndexState(
    string Path,
    IndexFileKind Kind,
    long Length,
    long LastWriteUtcTicks,
    string ContentHash,
    bool OcrCompleted,
    string? OcrModelId);

internal enum IndexFileKind
{
    Document = 1,
    Image = 2
}

internal enum TextContentKind
{
    Entry = 0,
    ImageOcr = 1,
    Document = 2
}
