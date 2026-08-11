using System.Buffers;
using System.Runtime.InteropServices;
using Kitopia.Desktop.Features.Utils;
using Microsoft.Data.Sqlite;

namespace Kitopia.Desktop.Features.Search.Semantic;

internal sealed class SqliteSemanticVectorStore
{
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
                var parameterNames = documentIdBatch
                    .Select((_, index) => $"$documentId{index}")
                    .ToArray();
                command.CommandText = $"""
                    SELECT document_id, content_hash, dimensions
                    FROM semantic_embeddings
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

    public async Task UpsertAsync(
        string documentId,
        string contentHash,
        string modelId,
        float[] vector,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO semantic_embeddings(document_id, content_hash, model_id, dimensions, vector, updated_at)
                VALUES($documentId, $contentHash, $modelId, $dimensions, $vector, unixepoch())
                ON CONFLICT(document_id, model_id) DO UPDATE SET
                    content_hash = excluded.content_hash,
                    dimensions = excluded.dimensions,
                    vector = excluded.vector,
                    updated_at = excluded.updated_at;
                """;
            command.Parameters.AddWithValue("$documentId", documentId);
            command.Parameters.AddWithValue("$contentHash", contentHash);
            command.Parameters.AddWithValue("$modelId", modelId);
            command.Parameters.AddWithValue("$dimensions", vector.Length);
            command.Parameters.Add("$vector", SqliteType.Blob).Value = MemoryMarshal.AsBytes(vector.AsSpan()).ToArray();
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
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
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO semantic_embeddings(document_id, content_hash, model_id, dimensions, vector, updated_at)
                VALUES($documentId, $contentHash, $modelId, $dimensions, $vector, unixepoch())
                ON CONFLICT(document_id, model_id) DO UPDATE SET
                    content_hash = excluded.content_hash,
                    dimensions = excluded.dimensions,
                    vector = excluded.vector,
                    updated_at = excluded.updated_at;
                """;

            foreach (var embedding in embeddings)
            {
                command.Parameters.Clear();
                command.Parameters.AddWithValue("$documentId", embedding.DocumentId);
                command.Parameters.AddWithValue("$contentHash", embedding.ContentHash);
                command.Parameters.AddWithValue("$modelId", embedding.ModelId);
                command.Parameters.AddWithValue("$dimensions", embedding.Vector.Length);
                command.Parameters.Add("$vector", SqliteType.Blob).Value =
                    MemoryMarshal.AsBytes(embedding.Vector.AsSpan()).ToArray();
                await command.ExecuteNonQueryAsync(cancellationToken);
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
        return await SearchEmbeddingsAsync(
            """
            SELECT document_id, dimensions, vector
            FROM semantic_embeddings
            WHERE model_id = $modelId;
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
                    FROM semantic_content_embeddings AS embedding
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
            await using var command = connection.CreateCommand();
            command.CommandText = """
                DELETE FROM semantic_content_embeddings
                WHERE entry_key = $entryKey
                  AND source_fingerprint = $contentHash;
                """;
            command.Parameters.AddWithValue("$entryKey", entryKey);
            command.Parameters.AddWithValue("$contentHash", contentHash);
            await command.ExecuteNonQueryAsync(cancellationToken);
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
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO semantic_content_embeddings(
                    entry_key, source_fingerprint, chunk_index, model_id, dimensions, vector, updated_at)
                VALUES($entryKey, $sourceFingerprint, $chunkIndex, $modelId, $dimensions, $vector, unixepoch())
                ON CONFLICT(entry_key, source_fingerprint, model_id, chunk_index) DO UPDATE SET
                    dimensions = excluded.dimensions,
                    vector = excluded.vector,
                    updated_at = excluded.updated_at;
                """;

            foreach (var embedding in embeddings)
            {
                command.Parameters.Clear();
                command.Parameters.AddWithValue("$entryKey", embedding.EntryKey);
                command.Parameters.AddWithValue("$sourceFingerprint", embedding.ContentHash);
                command.Parameters.AddWithValue("$chunkIndex", embedding.ChunkIndex);
                command.Parameters.AddWithValue("$modelId", embedding.ModelId);
                command.Parameters.AddWithValue("$dimensions", embedding.Vector.Length);
                command.Parameters.Add("$vector", SqliteType.Blob).Value =
                    MemoryMarshal.AsBytes(embedding.Vector.AsSpan()).ToArray();
                await command.ExecuteNonQueryAsync(cancellationToken);
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
            await using var sourceCommand = connection.CreateCommand();
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

            await using var cleanupCommand = connection.CreateCommand();
            cleanupCommand.Transaction = transaction;
            cleanupCommand.CommandText = """
                DELETE FROM semantic_content_embeddings
                WHERE entry_key = $entryKey
                  AND (source_fingerprint != $contentHash OR model_id != $modelId);
                """;
            cleanupCommand.Parameters.AddWithValue("$entryKey", entryKey);
            cleanupCommand.Parameters.AddWithValue("$contentHash", contentHash);
            cleanupCommand.Parameters.AddWithValue("$modelId", modelId);
            await cleanupCommand.ExecuteNonQueryAsync(cancellationToken);

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
        return await SearchEmbeddingsAsync(
            """
            SELECT embedding.entry_key, embedding.dimensions, embedding.vector
            FROM semantic_content_embeddings AS embedding
            INNER JOIN semantic_content_sources AS source
                ON source.entry_key = embedding.entry_key
                AND source.content_hash = embedding.source_fingerprint
                AND source.model_id = embedding.model_id
                AND source.dimensions = embedding.dimensions
            WHERE embedding.model_id = $modelId;
            """,
            modelId,
            queryVector,
            maximumResults,
            cancellationToken);
    }

    private async Task<IReadOnlyList<SemanticSearchMatch>> SearchEmbeddingsAsync(
        string query,
        string modelId,
        ReadOnlyMemory<float> queryVector,
        int maximumResults,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = query;
            command.Parameters.AddWithValue("$modelId", modelId);

            var bestMatches = new PriorityQueue<SemanticSearchMatch, double>();
            var expectedVectorBytes = checked(queryVector.Length * sizeof(float));
            var vectorBuffer = ArrayPool<byte>.Shared.Rent(expectedVectorBytes);
            try
            {
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (reader.GetInt32(1) != queryVector.Length
                        || reader.GetBytes(2, 0, null, 0, 0) != expectedVectorBytes)
                    {
                        continue;
                    }

                    var bytesRead = reader.GetBytes(2, 0, vectorBuffer, 0, expectedVectorBytes);
                    if (bytesRead != expectedVectorBytes)
                    {
                        continue;
                    }

                    var vector = MemoryMarshal.Cast<byte, float>(vectorBuffer.AsSpan(0, expectedVectorBytes));
                    var score = DotProduct(queryVector.Span, vector);
                    var match = new SemanticSearchMatch(reader.GetString(0), score);
                    if (bestMatches.Count < maximumResults)
                    {
                        bestMatches.Enqueue(match, score);
                    }
                    else if (bestMatches.TryPeek(out _, out var lowestScore) && score > lowestScore)
                    {
                        bestMatches.Dequeue();
                        bestMatches.Enqueue(match, score);
                    }
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(vectorBuffer);
            }

            return bestMatches.UnorderedItems
                .Select(item => item.Element)
                .OrderByDescending(match => match.Score)
                .ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteAsync(string documentId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                DELETE FROM semantic_embeddings WHERE document_id = $documentId;
                DELETE FROM semantic_content_embeddings WHERE entry_key = $documentId;
                DELETE FROM semantic_content_sources WHERE entry_key = $documentId;
                """;
            command.Parameters.AddWithValue("$documentId", documentId);
            await command.ExecuteNonQueryAsync(cancellationToken);
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
            foreach (var documentIdBatch in documentIds.Chunk(500))
            {
                await using var command = connection.CreateCommand();
                var parameterNames = documentIdBatch.Select((_, index) => $"$documentId{index}").ToArray();
                command.CommandText = $"""
                    DELETE FROM semantic_embeddings
                    WHERE document_id IN ({string.Join(", ", parameterNames)});
                    DELETE FROM semantic_content_embeddings
                    WHERE entry_key IN ({string.Join(", ", parameterNames)});
                    DELETE FROM semantic_content_sources
                    WHERE entry_key IN ({string.Join(", ", parameterNames)});
                    """;
                for (var index = 0; index < documentIdBatch.Length; index++)
                {
                    command.Parameters.AddWithValue(parameterNames[index], documentIdBatch[index]);
                }

                await command.ExecuteNonQueryAsync(cancellationToken);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        if (_initialized)
        {
            return connection;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS semantic_embeddings (
                document_id TEXT NOT NULL,
                content_hash TEXT NOT NULL,
                model_id TEXT NOT NULL,
                dimensions INTEGER NOT NULL,
                vector BLOB NOT NULL,
                updated_at INTEGER NOT NULL,
                PRIMARY KEY(document_id, model_id)
            );
            CREATE TABLE IF NOT EXISTS semantic_content_embeddings (
                entry_key TEXT NOT NULL,
                source_fingerprint TEXT NOT NULL,
                chunk_index INTEGER NOT NULL,
                model_id TEXT NOT NULL,
                dimensions INTEGER NOT NULL,
                vector BLOB NOT NULL,
                updated_at INTEGER NOT NULL,
                PRIMARY KEY(entry_key, source_fingerprint, model_id, chunk_index)
            );
            CREATE TABLE IF NOT EXISTS semantic_content_sources (
                entry_key TEXT NOT NULL PRIMARY KEY,
                source_fingerprint TEXT NOT NULL,
                content_hash TEXT NOT NULL,
                chunk_count INTEGER NOT NULL,
                model_id TEXT NOT NULL,
                dimensions INTEGER NOT NULL,
                updated_at INTEGER NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_semantic_content_embeddings_model
                ON semantic_content_embeddings(model_id, entry_key, source_fingerprint);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
        await EnsureContentSourceColumnsAsync(connection, cancellationToken);
        _initialized = true;
        return connection;
    }

    private static double DotProduct(ReadOnlySpan<float> left, ReadOnlySpan<float> right)
    {
        var score = 0d;
        for (var index = 0; index < left.Length; index++)
        {
            score += left[index] * right[index];
        }

        return score;
    }

    private static async Task EnsureContentSourceColumnsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var columnCommand = connection.CreateCommand())
        {
            columnCommand.CommandText = "PRAGMA table_info(semantic_content_sources);";
            await using var reader = await columnCommand.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                columns.Add(reader.GetString(1));
            }
        }

        if (!columns.Contains("content_hash"))
        {
            await using var alterCommand = connection.CreateCommand();
            alterCommand.CommandText = "ALTER TABLE semantic_content_sources ADD COLUMN content_hash TEXT NOT NULL DEFAULT '';";
            await alterCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        if (!columns.Contains("chunk_count"))
        {
            await using var alterCommand = connection.CreateCommand();
            alterCommand.CommandText = "ALTER TABLE semantic_content_sources ADD COLUMN chunk_count INTEGER NOT NULL DEFAULT -1;";
            await alterCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var indexCommand = connection.CreateCommand();
        indexCommand.CommandText = """
            CREATE INDEX IF NOT EXISTS ix_semantic_content_embeddings_model_hash
                ON semantic_content_embeddings(model_id, source_fingerprint);
            """;
        await indexCommand.ExecuteNonQueryAsync(cancellationToken);
    }
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
