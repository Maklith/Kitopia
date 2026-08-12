using System.Runtime.InteropServices;
using Kitopia.Desktop.Features.Utils;
using Microsoft.Data.Sqlite;

namespace Kitopia.Desktop.Features.Search.Semantic;

internal sealed class SqliteSemanticVectorStore
{
    private const int VectorDimensions = 512;
    private const string EmbeddingMetadataTable = "semantic_embeddings";
    private const string EmbeddingVectorTable = "semantic_embedding_vectors";
    private const string ContentEmbeddingMetadataTable = "semantic_content_embeddings";
    private const string ContentEmbeddingVectorTable = "semantic_content_vectors";
    private readonly string _connectionString;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _initialized;

    internal SqliteSemanticVectorStore(string? databasePath = null)
    {
        databasePath ??= Path.Combine(KitopiaPaths.AppRoot, "search-rag.db");
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
    }

    public async Task<Dictionary<string, StoredEmbeddingMetadata>> LoadMetadataAsync(
        string modelId,
        IReadOnlyCollection<string> documentIds,
        CancellationToken cancellationToken)
    {
        if (documentIds.Count == 0)
        {
            return new Dictionary<string, StoredEmbeddingMetadata>(StringComparer.Ordinal);
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(cancellationToken);
            var result = new Dictionary<string, StoredEmbeddingMetadata>(documentIds.Count, StringComparer.Ordinal);
            foreach (var documentIdBatch in documentIds.Chunk(500))
            {
                await using var command = connection.CreateCommand();
                var parameterNames = documentIdBatch.Select((_, index) => $"$documentId{index}").ToArray();
                command.CommandText = $"""
                    SELECT document_id, content_hash, dimensions
                    FROM {EmbeddingMetadataTable}
                    WHERE model_id = $modelId
                      AND document_id IN ({string.Join(", ", parameterNames)});
                    """;
                command.Parameters.AddWithValue("$modelId", modelId);
                for (var index = 0; index < documentIdBatch.Length; index++)
                {
                    command.Parameters.AddWithValue(parameterNames[index], documentIdBatch[index]);
                }

                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    result[reader.GetString(0)] = new StoredEmbeddingMetadata(
                        reader.GetString(1),
                        reader.GetInt32(2));
                }
            }

            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task UpsertAsync(
        string documentId,
        string contentHash,
        string modelId,
        float[] vector,
        CancellationToken cancellationToken)
    {
        return UpsertBatchAsync(
            [new EmbeddingWrite(documentId, contentHash, modelId, vector)],
            cancellationToken);
    }

    public async Task UpsertBatchAsync(
        IReadOnlyList<EmbeddingWrite> embeddings,
        CancellationToken cancellationToken)
    {
        if (embeddings.Count == 0)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(cancellationToken);
            using var transaction = connection.BeginTransaction();
            foreach (var embedding in embeddings)
            {
                EnsureVectorDimensions(embedding.Vector);
                await ReplaceEmbeddingAsync(connection, transaction, embedding, cancellationToken);
            }

            transaction.Commit();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<SemanticSearchMatch>> SearchAsync(
        string modelId,
        ReadOnlyMemory<float> queryVector,
        int maximumResults,
        CancellationToken cancellationToken)
    {
        return await SearchVectorsAsync(
            $"""
            SELECT metadata.document_id, vectors.distance
            FROM {EmbeddingVectorTable} AS vectors
            INNER JOIN {EmbeddingMetadataTable} AS metadata
                ON metadata.vector_rowid = vectors.rowid
            WHERE vectors.embedding MATCH $queryVector
              AND k = $maximumResults
              AND vectors.model_id = $modelId
              AND metadata.dimensions = $dimensions
            ORDER BY vectors.distance;
            """,
            modelId,
            queryVector,
            maximumResults,
            cancellationToken);
    }

    public async Task<Dictionary<string, StoredContentMetadata>> LoadContentMetadataAsync(
        IReadOnlyCollection<string> entryKeys,
        CancellationToken cancellationToken)
    {
        if (entryKeys.Count == 0)
        {
            return new Dictionary<string, StoredContentMetadata>(StringComparer.Ordinal);
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(cancellationToken);
            var result = new Dictionary<string, StoredContentMetadata>(entryKeys.Count, StringComparer.Ordinal);
            foreach (var entryKeyBatch in entryKeys.Chunk(500))
            {
                await using var command = connection.CreateCommand();
                var parameterNames = entryKeyBatch.Select((_, index) => $"$entryKey{index}").ToArray();
                command.CommandText = $"""
                    SELECT entry_key, source_fingerprint, content_hash, chunk_count, model_id, dimensions
                    FROM semantic_content_sources
                    WHERE entry_key IN ({string.Join(", ", parameterNames)});
                    """;
                for (var index = 0; index < entryKeyBatch.Length; index++)
                {
                    command.Parameters.AddWithValue(parameterNames[index], entryKeyBatch[index]);
                }

                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    result[reader.GetString(0)] = new StoredContentMetadata(
                        reader.GetString(1),
                        reader.GetString(2),
                        reader.GetInt32(3),
                        reader.GetString(4),
                        reader.GetInt32(5));
                }
            }

            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<HashSet<string>> LoadIndexedContentHashesAsync(
        IReadOnlyCollection<string> contentHashes,
        string modelId,
        int dimensions,
        CancellationToken cancellationToken)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        if (contentHashes.Count == 0)
        {
            return result;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(cancellationToken);
            foreach (var hashBatch in contentHashes.Distinct(StringComparer.Ordinal).Chunk(500))
            {
                await using var command = connection.CreateCommand();
                var parameterNames = hashBatch.Select((_, index) => $"$contentHash{index}").ToArray();
                command.CommandText = $"""
                    SELECT DISTINCT embedding.source_fingerprint
                    FROM {ContentEmbeddingMetadataTable} AS embedding
                    INNER JOIN semantic_content_sources AS source
                        ON source.entry_key = embedding.entry_key
                        AND source.content_hash = embedding.source_fingerprint
                        AND source.model_id = embedding.model_id
                        AND source.dimensions = embedding.dimensions
                    WHERE embedding.model_id = $modelId
                      AND embedding.dimensions = $dimensions
                      AND embedding.source_fingerprint IN ({string.Join(", ", parameterNames)});
                    """;
                command.Parameters.AddWithValue("$modelId", modelId);
                command.Parameters.AddWithValue("$dimensions", dimensions);
                for (var index = 0; index < hashBatch.Length; index++)
                {
                    command.Parameters.AddWithValue(parameterNames[index], hashBatch[index]);
                }

                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    result.Add(reader.GetString(0));
                }
            }

            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteContentVersionAsync(
        string entryKey,
        string contentHash,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(cancellationToken);
            using var transaction = connection.BeginTransaction();
            await DeleteMappedVectorsAsync(
                connection,
                transaction,
                ContentEmbeddingMetadataTable,
                ContentEmbeddingVectorTable,
                "entry_key = $entryKey AND source_fingerprint = $contentHash",
                [new SqlParameterValue("$entryKey", entryKey), new SqlParameterValue("$contentHash", contentHash)],
                cancellationToken);
            transaction.Commit();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpsertContentBatchAsync(
        IReadOnlyList<ContentEmbeddingWrite> embeddings,
        CancellationToken cancellationToken)
    {
        if (embeddings.Count == 0)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(cancellationToken);
            using var transaction = connection.BeginTransaction();
            foreach (var embedding in embeddings)
            {
                EnsureVectorDimensions(embedding.Vector);
                await ReplaceContentEmbeddingAsync(connection, transaction, embedding, cancellationToken);
            }

            transaction.Commit();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task CompleteContentIndexAsync(
        string entryKey,
        string sourceFingerprint,
        string contentHash,
        int chunkCount,
        string modelId,
        int dimensions,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(cancellationToken);
            using var transaction = connection.BeginTransaction();
            await using (var sourceCommand = connection.CreateCommand())
            {
                sourceCommand.Transaction = transaction;
                sourceCommand.CommandText = """
                    INSERT INTO semantic_content_sources(
                        entry_key, source_fingerprint, content_hash, chunk_count, model_id, dimensions, updated_at)
                    VALUES($entryKey, $sourceFingerprint, $contentHash, $chunkCount, $modelId, $dimensions, unixepoch())
                    ON CONFLICT(entry_key) DO UPDATE SET
                        source_fingerprint = excluded.source_fingerprint,
                        content_hash = excluded.content_hash,
                        chunk_count = excluded.chunk_count,
                        model_id = excluded.model_id,
                        dimensions = excluded.dimensions,
                        updated_at = excluded.updated_at;
                    """;
                sourceCommand.Parameters.AddWithValue("$entryKey", entryKey);
                sourceCommand.Parameters.AddWithValue("$sourceFingerprint", sourceFingerprint);
                sourceCommand.Parameters.AddWithValue("$contentHash", contentHash);
                sourceCommand.Parameters.AddWithValue("$chunkCount", chunkCount);
                sourceCommand.Parameters.AddWithValue("$modelId", modelId);
                sourceCommand.Parameters.AddWithValue("$dimensions", dimensions);
                await sourceCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            await DeleteMappedVectorsAsync(
                connection,
                transaction,
                ContentEmbeddingMetadataTable,
                ContentEmbeddingVectorTable,
                "entry_key = $entryKey AND (source_fingerprint != $contentHash OR model_id != $modelId)",
                [
                    new SqlParameterValue("$entryKey", entryKey),
                    new SqlParameterValue("$contentHash", contentHash),
                    new SqlParameterValue("$modelId", modelId)
                ],
                cancellationToken);
            transaction.Commit();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<SemanticSearchMatch>> SearchContentAsync(
        string modelId,
        ReadOnlyMemory<float> queryVector,
        int maximumResults,
        CancellationToken cancellationToken)
    {
        return await SearchVectorsAsync(
            $"""
            SELECT embedding.entry_key, vectors.distance
            FROM {ContentEmbeddingVectorTable} AS vectors
            INNER JOIN {ContentEmbeddingMetadataTable} AS embedding
                ON embedding.vector_rowid = vectors.rowid
            INNER JOIN semantic_content_sources AS source
                ON source.entry_key = embedding.entry_key
                AND source.content_hash = embedding.source_fingerprint
                AND source.model_id = embedding.model_id
                AND source.dimensions = embedding.dimensions
            WHERE vectors.embedding MATCH $queryVector
              AND k = $maximumResults
              AND vectors.model_id = $modelId
              AND embedding.dimensions = $dimensions
            ORDER BY vectors.distance;
            """,
            modelId,
            queryVector,
            maximumResults,
            cancellationToken);
    }

    public async Task DeleteAsync(string documentId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(cancellationToken);
            using var transaction = connection.BeginTransaction();
            await DeleteDocumentVectorsAsync(connection, transaction, documentId, cancellationToken);
            transaction.Commit();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteBatchAsync(IReadOnlyCollection<string> documentIds, CancellationToken cancellationToken)
    {
        if (documentIds.Count == 0)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(cancellationToken);
            using var transaction = connection.BeginTransaction();
            foreach (var documentId in documentIds.Distinct(StringComparer.Ordinal))
            {
                await DeleteDocumentVectorsAsync(connection, transaction, documentId, cancellationToken);
            }

            transaction.Commit();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IReadOnlyList<SemanticSearchMatch>> SearchVectorsAsync(
        string query,
        string modelId,
        ReadOnlyMemory<float> queryVector,
        int maximumResults,
        CancellationToken cancellationToken)
    {
        EnsureVectorDimensions(queryVector.Span);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = query;
            command.Parameters.Add("$queryVector", SqliteType.Blob).Value = ToVectorBlob(queryVector.Span);
            command.Parameters.AddWithValue("$maximumResults", Math.Max(1, maximumResults));
            command.Parameters.AddWithValue("$modelId", modelId);
            command.Parameters.AddWithValue("$dimensions", queryVector.Length);

            var matches = new List<SemanticSearchMatch>(maximumResults);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                // sqlite-vec returns cosine distance. Existing rank fusion consumes cosine similarity.
                matches.Add(new SemanticSearchMatch(reader.GetString(0), 1d - reader.GetDouble(1)));
            }

            return matches;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static async Task ReplaceEmbeddingAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        EmbeddingWrite embedding,
        CancellationToken cancellationToken)
    {
        var existingRowId = await GetVectorRowIdAsync(
            connection,
            transaction,
            EmbeddingMetadataTable,
            "document_id = $documentId",
            [new SqlParameterValue("$documentId", embedding.DocumentId)],
            cancellationToken);
        if (existingRowId is not null)
        {
            await DeleteVectorAsync(connection, transaction, EmbeddingVectorTable, existingRowId.Value, cancellationToken);
        }

        var vectorRowId = await InsertVectorAsync(
            connection,
            transaction,
            EmbeddingVectorTable,
            embedding.ModelId,
            embedding.Vector,
            cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            INSERT INTO {EmbeddingMetadataTable}(document_id, content_hash, model_id, dimensions, vector_rowid, updated_at)
            VALUES($documentId, $contentHash, $modelId, $dimensions, $vectorRowId, unixepoch())
            ON CONFLICT(document_id) DO UPDATE SET
                content_hash = excluded.content_hash,
                model_id = excluded.model_id,
                dimensions = excluded.dimensions,
                vector_rowid = excluded.vector_rowid,
                updated_at = excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$documentId", embedding.DocumentId);
        command.Parameters.AddWithValue("$contentHash", embedding.ContentHash);
        command.Parameters.AddWithValue("$modelId", embedding.ModelId);
        command.Parameters.AddWithValue("$dimensions", embedding.Vector.Length);
        command.Parameters.AddWithValue("$vectorRowId", vectorRowId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ReplaceContentEmbeddingAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ContentEmbeddingWrite embedding,
        CancellationToken cancellationToken)
    {
        var keyParameters = new[]
        {
            new SqlParameterValue("$entryKey", embedding.EntryKey),
            new SqlParameterValue("$sourceFingerprint", embedding.ContentHash),
            new SqlParameterValue("$modelId", embedding.ModelId),
            new SqlParameterValue("$chunkIndex", embedding.ChunkIndex)
        };
        var existingRowId = await GetVectorRowIdAsync(
            connection,
            transaction,
            ContentEmbeddingMetadataTable,
            "entry_key = $entryKey AND source_fingerprint = $sourceFingerprint AND model_id = $modelId AND chunk_index = $chunkIndex",
            keyParameters,
            cancellationToken);
        if (existingRowId is not null)
        {
            await DeleteVectorAsync(connection, transaction, ContentEmbeddingVectorTable, existingRowId.Value, cancellationToken);
        }

        var vectorRowId = await InsertVectorAsync(
            connection,
            transaction,
            ContentEmbeddingVectorTable,
            embedding.ModelId,
            embedding.Vector,
            cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            INSERT INTO {ContentEmbeddingMetadataTable}(
                entry_key, source_fingerprint, chunk_index, model_id, dimensions, vector_rowid, updated_at)
            VALUES($entryKey, $sourceFingerprint, $chunkIndex, $modelId, $dimensions, $vectorRowId, unixepoch())
            ON CONFLICT(entry_key, source_fingerprint, model_id, chunk_index) DO UPDATE SET
                dimensions = excluded.dimensions,
                vector_rowid = excluded.vector_rowid,
                updated_at = excluded.updated_at;
            """;
        AddParameters(command, keyParameters);
        command.Parameters.AddWithValue("$dimensions", embedding.Vector.Length);
        command.Parameters.AddWithValue("$vectorRowId", vectorRowId);
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
            command.Parameters.Add("$vector", SqliteType.Blob).Value = ToVectorBlob(vector);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var rowIdCommand = connection.CreateCommand();
        rowIdCommand.Transaction = transaction;
        rowIdCommand.CommandText = "SELECT last_insert_rowid();";
        return Convert.ToInt64(await rowIdCommand.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task<long?> GetVectorRowIdAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string metadataTable,
        string predicate,
        IReadOnlyList<SqlParameterValue> parameters,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT vector_rowid FROM {metadataTable} WHERE {predicate};";
        AddParameters(command, parameters);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null || result is DBNull ? null : Convert.ToInt64(result);
    }

    private static async Task DeleteDocumentVectorsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string documentId,
        CancellationToken cancellationToken)
    {
        var parameters = new[] { new SqlParameterValue("$documentId", documentId) };
        await DeleteMappedVectorsAsync(
            connection,
            transaction,
            EmbeddingMetadataTable,
            EmbeddingVectorTable,
            "document_id = $documentId",
            parameters,
            cancellationToken);
        await DeleteMappedVectorsAsync(
            connection,
            transaction,
            ContentEmbeddingMetadataTable,
            ContentEmbeddingVectorTable,
            "entry_key = $documentId",
            parameters,
            cancellationToken);
        await using var sourceCommand = connection.CreateCommand();
        sourceCommand.Transaction = transaction;
        sourceCommand.CommandText = "DELETE FROM semantic_content_sources WHERE entry_key = $documentId;";
        AddParameters(sourceCommand, parameters);
        await sourceCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task DeleteMappedVectorsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string metadataTable,
        string vectorTable,
        string predicate,
        IReadOnlyList<SqlParameterValue> parameters,
        CancellationToken cancellationToken)
    {
        var vectorRowIds = new List<long>();
        await using (var selectCommand = connection.CreateCommand())
        {
            selectCommand.Transaction = transaction;
            selectCommand.CommandText = $"SELECT vector_rowid FROM {metadataTable} WHERE {predicate};";
            AddParameters(selectCommand, parameters);
            await using var reader = await selectCommand.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                vectorRowIds.Add(reader.GetInt64(0));
            }
        }

        foreach (var vectorRowId in vectorRowIds)
        {
            await DeleteVectorAsync(connection, transaction, vectorTable, vectorRowId, cancellationToken);
        }

        await using var deleteCommand = connection.CreateCommand();
        deleteCommand.Transaction = transaction;
        deleteCommand.CommandText = $"DELETE FROM {metadataTable} WHERE {predicate};";
        AddParameters(deleteCommand, parameters);
        await deleteCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task DeleteVectorAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string vectorTable,
        long vectorRowId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"DELETE FROM {vectorTable} WHERE rowid = $vectorRowId;";
        command.Parameters.AddWithValue("$vectorRowId", vectorRowId);
        await command.ExecuteNonQueryAsync(cancellationToken);
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
                await InitializeAsync(connection, cancellationToken);
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

    private static async Task InitializeAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await ExecuteNonQueryAsync(connection, "PRAGMA journal_mode = WAL;", cancellationToken);
        await CreateContentSourcesTableAsync(connection, cancellationToken);
        await CreateVectorTablesAsync(connection, cancellationToken);
        await CreateEmbeddingMetadataTableAsync(connection, cancellationToken);
        await CreateContentEmbeddingMetadataTableAsync(connection, cancellationToken);
        await ExecuteNonQueryAsync(
            connection,
            $"CREATE INDEX IF NOT EXISTS ix_semantic_embeddings_model ON {EmbeddingMetadataTable}(model_id);",
            cancellationToken);
        await ExecuteNonQueryAsync(
            connection,
            $"CREATE INDEX IF NOT EXISTS ix_semantic_content_embeddings_model ON {ContentEmbeddingMetadataTable}(model_id, entry_key, source_fingerprint);",
            cancellationToken);
        await ExecuteNonQueryAsync(
            connection,
            $"CREATE INDEX IF NOT EXISTS ix_semantic_content_embeddings_model_hash ON {ContentEmbeddingMetadataTable}(model_id, source_fingerprint);",
            cancellationToken);
    }

    private static async Task CreateVectorTablesAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await ExecuteNonQueryAsync(
            connection,
            $"CREATE VIRTUAL TABLE IF NOT EXISTS {EmbeddingVectorTable} USING vec0(embedding float[{VectorDimensions}] distance_metric=cosine, model_id TEXT PARTITION KEY);",
            cancellationToken);
        await ExecuteNonQueryAsync(
            connection,
            $"CREATE VIRTUAL TABLE IF NOT EXISTS {ContentEmbeddingVectorTable} USING vec0(embedding float[{VectorDimensions}] distance_metric=cosine, model_id TEXT PARTITION KEY);",
            cancellationToken);
    }

    private static async Task CreateEmbeddingMetadataTableAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await ExecuteNonQueryAsync(
            connection,
            $"""
            CREATE TABLE IF NOT EXISTS {EmbeddingMetadataTable} (
                document_id TEXT NOT NULL PRIMARY KEY,
                content_hash TEXT NOT NULL,
                model_id TEXT NOT NULL,
                dimensions INTEGER NOT NULL,
                vector_rowid INTEGER NOT NULL,
                updated_at INTEGER NOT NULL
            );
            """,
            cancellationToken);
    }

    private static async Task CreateContentEmbeddingMetadataTableAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await ExecuteNonQueryAsync(
            connection,
            $"""
            CREATE TABLE IF NOT EXISTS {ContentEmbeddingMetadataTable} (
                entry_key TEXT NOT NULL,
                source_fingerprint TEXT NOT NULL,
                chunk_index INTEGER NOT NULL,
                model_id TEXT NOT NULL,
                dimensions INTEGER NOT NULL,
                vector_rowid INTEGER NOT NULL,
                updated_at INTEGER NOT NULL,
                PRIMARY KEY(entry_key, source_fingerprint, model_id, chunk_index)
            );
            """,
            cancellationToken);
    }

    private static async Task CreateContentSourcesTableAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await ExecuteNonQueryAsync(
            connection,
            """
            CREATE TABLE IF NOT EXISTS semantic_content_sources (
                entry_key TEXT NOT NULL PRIMARY KEY,
                source_fingerprint TEXT NOT NULL,
                content_hash TEXT NOT NULL,
                chunk_count INTEGER NOT NULL,
                model_id TEXT NOT NULL,
                dimensions INTEGER NOT NULL,
                updated_at INTEGER NOT NULL
            );
            """,
            cancellationToken);
    }

    private static async Task ExecuteNonQueryAsync(
        SqliteConnection connection,
        string commandText,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddParameters(SqliteCommand command, IReadOnlyList<SqlParameterValue> parameters)
    {
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        }
    }

    private static byte[] ToVectorBlob(ReadOnlySpan<float> vector)
    {
        return MemoryMarshal.AsBytes(vector).ToArray();
    }

    private static void EnsureVectorDimensions(ReadOnlySpan<float> vector)
    {
        if (vector.Length != VectorDimensions)
        {
            throw new ArgumentException($"sqlite-vec expects {VectorDimensions}-dimension embeddings.", nameof(vector));
        }
    }

    private readonly record struct SqlParameterValue(string Name, object Value);
}

internal sealed record StoredEmbeddingMetadata(string ContentHash, int Dimensions);

internal sealed record EmbeddingWrite(string DocumentId, string ContentHash, string ModelId, float[] Vector);

internal sealed record StoredContentMetadata(
    string SourceFingerprint,
    string ContentHash,
    int ChunkCount,
    string ModelId,
    int Dimensions);

internal sealed record ContentEmbeddingWrite(
    string EntryKey,
    string ContentHash,
    int ChunkIndex,
    string ModelId,
    float[] Vector);
