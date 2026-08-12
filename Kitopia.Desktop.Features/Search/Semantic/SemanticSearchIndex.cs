using System.Security.Cryptography;
using System.Text;
using System.Diagnostics.CodeAnalysis;
using Kitopia.Desktop.Features.Services;
using Kitopia.Desktop.Features.Services.Config;
using Serilog;

namespace Kitopia.Desktop.Features.Search.Semantic;

internal sealed class SemanticSearchIndex
{
    private const int MetadataIndexingBatchSize = 128;
    private const int ContentIndexingBatchSize = 32;
    private const int MetadataLookupBatchSize = 500;
    private const int ContentSearchCandidateMultiplier = 4;
    private static readonly ILogger Logger = LogManager.Logger.ForContext<SemanticSearchIndex>();
    private readonly object _lock = new();
    private readonly SqliteSemanticVectorStore _store = new();
    private readonly Dictionary<string, SemanticDocument> _documents = new(StringComparer.Ordinal);
    private BgeOnnxEmbeddingService? _embeddingService;
    private long _documentsVersion;
    private int _synchronizationScheduled;

    public void Upsert(SearchEntry entry)
    {
        var document = SemanticDocument.Create(entry);
        var changed = false;
        lock (_lock)
        {
            if (_documents.TryGetValue(entry.OnlyKey, out var existing)
                && existing.ContentHash == document.ContentHash)
            {
                return;
            }

            _documents[entry.OnlyKey] = document;
            _documentsVersion++;
            changed = true;
        }

        if (changed)
        {
            ScheduleSynchronization();
        }
    }

    public void Synchronize(IEnumerable<SearchEntry> entries)
    {
        var documents = entries
            .Select(SemanticDocument.Create)
            .GroupBy(document => document.Entry.OnlyKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
        var changed = false;
        string[] removedDocumentIds = [];
        lock (_lock)
        {
            if (_documents.Count == documents.Count
                && documents.All(pair => _documents.TryGetValue(pair.Key, out var existing)
                                         && existing.ContentHash == pair.Value.ContentHash))
            {
                // File contents are intentionally not part of the lightweight entry hash.
                // A rebuild is the point at which their file fingerprints are checked.
                ScheduleSynchronization();
                return;
            }

            removedDocumentIds = _documents.Keys
                .Where(key => !documents.ContainsKey(key))
                .ToArray();
            _documents.Clear();
            foreach (var (key, document) in documents)
            {
                _documents[key] = document;
            }

            _documentsVersion++;
            changed = true;
        }

        if (changed)
        {
            if (removedDocumentIds.Length > 0)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _store.DeleteBatchAsync(removedDocumentIds, CancellationToken.None);
                    }
                    catch (Exception exception)
                    {
                        Logger.Warning(exception, "Failed to remove {DocumentCount} stale semantic documents.", removedDocumentIds.Length);
                    }
                });
            }

            ScheduleSynchronization();
        }
    }

    public void Remove(string onlyKey)
    {
        var removed = false;
        lock (_lock)
        {
            removed = _documents.Remove(onlyKey);
            if (removed)
            {
                _documentsVersion++;
            }
        }

        if (!removed)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await _store.DeleteAsync(onlyKey, CancellationToken.None);
            }
            catch (Exception exception)
            {
                Logger.Warning(exception, "Failed to remove semantic embedding for {OnlyKey}", onlyKey);
            }
        });
    }

    public async Task<IReadOnlyList<SemanticSearchMatch>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        if (!TryGetEmbeddingService(out var embeddingService))
        {
            return [];
        }

        var queryVector = (await embeddingService.EmbedAsync(
            [BgeOnnxEmbeddingService.QueryInstruction + query],
            BgeOnnxEmbeddingService.MetadataMaximumTokens,
            cancellationToken))[0];
        var maximumResults = Math.Max(1, ConfigManger.Config.semanticSearchMaxResults);
        var baseMatches = await _store.SearchAsync(
            embeddingService.ModelId,
            queryVector,
            maximumResults,
            cancellationToken);
        var contentMatches = await _store.SearchContentAsync(
            embeddingService.ModelId,
            queryVector,
            checked(maximumResults * ContentSearchCandidateMultiplier),
            cancellationToken);

        // A content document can have several matching chunks. Keep the strongest chunk
        // for each entry before fusing it with the pinyin result.
        return baseMatches.Concat(contentMatches)
            .GroupBy(match => match.OnlyKey, StringComparer.Ordinal)
            .Select(group => group.MaxBy(match => match.Score)!)
            .OrderByDescending(match => match.Score)
            .Take(maximumResults)
            .ToList();
    }

    private void ScheduleSynchronization()
    {
        if (!TryGetEmbeddingService(out _)
            || Interlocked.CompareExchange(ref _synchronizationScheduled, 1, 0) != 0)
        {
            return;
        }

        _ = Task.Run(SynchronizationAsync);
    }

    private async Task SynchronizationAsync()
    {
        var documentVersion = 0L;
        var completed = false;
        BgeOnnxEmbeddingService? embeddingService = null;
        try
        {
            if (!TryGetEmbeddingService(out embeddingService))
            {
                return;
            }

            List<SemanticDocument> documents;
            lock (_lock)
            {
                documents = _documents.Values.ToList();
                documentVersion = _documentsVersion;
            }

            foreach (var documentBatch in documents.Chunk(MetadataLookupBatchSize))
            {
                var documentsInBatch = documentBatch.ToList();
                var storedEmbeddings = await _store.LoadMetadataAsync(
                    embeddingService.ModelId,
                    documentsInBatch.Select(document => document.Entry.OnlyKey).ToArray(),
                    CancellationToken.None);
                var missingDocuments = documentsInBatch
                    .Where(document => !storedEmbeddings.TryGetValue(document.Entry.OnlyKey, out var stored)
                                       || stored.ContentHash != document.ContentHash
                                       || stored.Dimensions != BgeOnnxEmbeddingService.VectorDimensions);

                foreach (var missingDocumentBatch in missingDocuments.Chunk(MetadataIndexingBatchSize))
                {
                    var documentsToEmbed = missingDocumentBatch.ToList();
                    var vectors = await embeddingService.EmbedAsync(
                        documentsToEmbed.Select(document => document.CreateContent()).ToList(),
                        BgeOnnxEmbeddingService.MetadataMaximumTokens,
                        CancellationToken.None);

                    var writes = new List<EmbeddingWrite>(documentsToEmbed.Count);

                    for (var index = 0; index < documentsToEmbed.Count; index++)
                    {
                        var document = documentsToEmbed[index];
                        var vector = vectors[index];
                        lock (_lock)
                        {
                            if (_documents.TryGetValue(document.Entry.OnlyKey, out var current)
                                && current.ContentHash == document.ContentHash)
                            {
                                writes.Add(new EmbeddingWrite(
                                    document.Entry.OnlyKey,
                                    document.ContentHash,
                                    embeddingService.ModelId,
                                    vector));
                            }
                        }
                    }

                    await _store.UpsertBatchAsync(writes, CancellationToken.None);
                }
            }

            await SynchronizeContentAsync(documents, embeddingService);

            completed = true;
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Semantic search indexing is unavailable; pinyin search remains active.");
        }
        finally
        {
            // Indexing can create a large native memory arena inside ONNX Runtime. Vectors have
            // already been persisted to SQLite, so release it here and recreate it on demand.
            if (embeddingService is not null)
            {
                try
                {
                    await embeddingService.ReleaseSessionAsync();
                }
                catch (Exception exception)
                {
                    Logger.Warning(exception, "Failed to release the semantic search inference session.");
                }
            }

            Interlocked.Exchange(ref _synchronizationScheduled, 0);

            if (completed)
            {
                var changedWhileIndexing = false;
                lock (_lock)
                {
                    changedWhileIndexing = _documentsVersion != documentVersion;
                }

                if (changedWhileIndexing)
                {
                    ScheduleSynchronization();
                }
            }
        }
    }

    private bool TryGetEmbeddingService([NotNullWhen(true)] out BgeOnnxEmbeddingService? embeddingService)
    {
        embeddingService = _embeddingService;
        if (embeddingService is not null)
        {
            return true;
        }

        lock (_lock)
        {
            if (_embeddingService is null && BgeOnnxEmbeddingService.TryCreate(out var created))
            {
                _embeddingService = created;
            }

            embeddingService = _embeddingService;
            return embeddingService is not null;
        }
    }

    private async Task<int> SynchronizeContentAsync(
        IReadOnlyList<SemanticDocument> documents,
        BgeOnnxEmbeddingService embeddingService)
    {
        var indexedDocumentCount = 0;
        var contentDocuments = new List<SemanticContentDocument>(MetadataLookupBatchSize);
        foreach (var document in documents)
        {
            if (DocumentTextExtractor.TryCreateSource(document.Entry.OnlyKey, out var source))
            {
                contentDocuments.Add(new SemanticContentDocument(document.Entry, source));
            }

            if (contentDocuments.Count == MetadataLookupBatchSize)
            {
                indexedDocumentCount += await SynchronizeContentBatchAsync(contentDocuments, embeddingService);
                contentDocuments.Clear();
            }
        }

        if (contentDocuments.Count > 0)
        {
            indexedDocumentCount += await SynchronizeContentBatchAsync(contentDocuments, embeddingService);
        }

        return indexedDocumentCount;
    }

    private async Task<int> SynchronizeContentBatchAsync(
        IReadOnlyList<SemanticContentDocument> documents,
        BgeOnnxEmbeddingService embeddingService)
    {
        var storedMetadata = await _store.LoadContentMetadataAsync(
            documents.Select(document => document.Entry.OnlyKey).ToArray(),
            CancellationToken.None);
        var documentsToCheck = new List<(SemanticContentDocument Document, bool NeedsMetadataUpdate)>();
        foreach (var document in documents)
        {
            if (storedMetadata.TryGetValue(document.Entry.OnlyKey, out var stored)
                && stored.SourceFingerprint == document.Source.SourceFingerprint)
            {
                if (stored.ChunkCount == 0)
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(stored.ContentHash))
                {
                    documentsToCheck.Add((
                        document with { Source = document.Source with { ContentHash = stored.ContentHash } },
                        stored.ModelId != embeddingService.ModelId
                        || stored.Dimensions != BgeOnnxEmbeddingService.VectorDimensions));
                    continue;
                }
            }

            var hashedSource = await DocumentTextExtractor.TryComputeContentHashAsync(
                document.Source,
                CancellationToken.None);
            if (hashedSource is not null)
            {
                documentsToCheck.Add((document with { Source = hashedSource }, true));
            }
        }

        var indexedContentHashes = await _store.LoadIndexedContentHashesAsync(
            documentsToCheck.Select(item => item.Document.Source.ContentHash!).ToArray(),
            embeddingService.ModelId,
            BgeOnnxEmbeddingService.VectorDimensions,
            CancellationToken.None);
        var indexedDocumentCount = 0;
        foreach (var (document, needsMetadataUpdate) in documentsToCheck)
        {
            try
            {
                var contentHash = document.Source.ContentHash!;
                var contentAlreadyIndexed = indexedContentHashes.Contains(contentHash);
                if (contentAlreadyIndexed)
                {
                    if (needsMetadataUpdate && await CompleteDuplicateContentAsync(document, embeddingService))
                    {
                        continue;
                    }

                    continue;
                }

                Logger.Debug("Indexing semantic content for {DocumentPath}.", document.Source.Path);
                if (await IndexContentAsync(document, embeddingService))
                {
                    indexedDocumentCount++;
                    indexedContentHashes.Add(contentHash);
                }
            }
            catch (Exception)
            {
            }
        }

        return indexedDocumentCount;
    }

    private async Task<bool> CompleteDuplicateContentAsync(
        SemanticContentDocument document,
        BgeOnnxEmbeddingService embeddingService)
    {
        if (!IsCurrentContentSource(document))
        {
            return false;
        }

        await _store.CompleteContentIndexAsync(
            document.Entry.OnlyKey,
            document.Source.SourceFingerprint,
            document.Source.ContentHash!,
            -1,
            embeddingService.ModelId,
            BgeOnnxEmbeddingService.VectorDimensions,
            CancellationToken.None);
        return true;
    }

    private async Task<bool> IndexContentAsync(
        SemanticContentDocument document,
        BgeOnnxEmbeddingService embeddingService)
    {
        await _store.DeleteContentVersionAsync(
            document.Entry.OnlyKey,
            document.Source.ContentHash!,
            CancellationToken.None);

        var chunks = new List<string>(ContentIndexingBatchSize);
        var chunkIndex = 0;
        await foreach (var chunk in DocumentTextExtractor.ExtractChunksAsync(document.Source, CancellationToken.None))
        {
            chunks.Add(chunk);
            if (chunks.Count == ContentIndexingBatchSize)
            {
                await PersistContentBatchAsync(document, chunks, chunkIndex, embeddingService);
                chunkIndex += chunks.Count;
                chunks.Clear();
            }
        }

        if (chunks.Count > 0)
        {
            await PersistContentBatchAsync(document, chunks, chunkIndex, embeddingService);
            chunkIndex += chunks.Count;
        }

        if (!IsCurrentContentSource(document))
        {
            return false;
        }

        await _store.CompleteContentIndexAsync(
            document.Entry.OnlyKey,
            document.Source.SourceFingerprint,
            document.Source.ContentHash!,
            chunkIndex,
            embeddingService.ModelId,
            BgeOnnxEmbeddingService.VectorDimensions,
            CancellationToken.None);
        Logger.Debug(
            "Completed semantic content indexing for {DocumentPath}: {TotalChunkCount} chunks.",
            document.Source.Path,
            chunkIndex);
        return true;
    }

    private async Task PersistContentBatchAsync(
        SemanticContentDocument document,
        IReadOnlyList<string> chunks,
        int startingChunkIndex,
        BgeOnnxEmbeddingService embeddingService)
    {
        Logger.Debug(
            "Embedding {ChunkCount} content chunks for {DocumentPath} (chunks {FirstChunkIndex}-{LastChunkIndex}; {TotalChunkCount} chunks accumulated).",
            chunks.Count,
            document.Source.Path,
            startingChunkIndex + 1,
            startingChunkIndex + chunks.Count,
            startingChunkIndex + chunks.Count);
        var vectors = await embeddingService.EmbedAsync(
            chunks,
            BgeOnnxEmbeddingService.DocumentMaximumTokens,
            CancellationToken.None);
        var writes = new List<ContentEmbeddingWrite>(chunks.Count);
        for (var index = 0; index < chunks.Count; index++)
        {
            writes.Add(new ContentEmbeddingWrite(
                document.Entry.OnlyKey,
                document.Source.ContentHash!,
                startingChunkIndex + index,
                embeddingService.ModelId,
                vectors[index]));
        }

        await _store.UpsertContentBatchAsync(writes, CancellationToken.None);
        Logger.Debug(
            "Wrote {ChunkCount} content vectors for {DocumentPath} (chunks {FirstChunkIndex}-{LastChunkIndex}; {TotalChunkCount} chunks accumulated).",
            writes.Count,
            document.Source.Path,
            startingChunkIndex + 1,
            startingChunkIndex + writes.Count,
            startingChunkIndex + writes.Count);
    }

    private bool IsCurrentContentSource(SemanticContentDocument document)
    {
        if (!DocumentTextExtractor.TryCreateSource(document.Entry.OnlyKey, out var currentSource)
            || currentSource.SourceFingerprint != document.Source.SourceFingerprint)
        {
            return false;
        }

        lock (_lock)
        {
            return _documents.ContainsKey(document.Entry.OnlyKey);
        }
    }

}

internal sealed record SemanticSearchMatch(string OnlyKey, double Score);

internal sealed record SemanticDocument(SearchEntry Entry, string ContentHash)
{
    public static SemanticDocument Create(SearchEntry entry)
    {
        var content = CreateContent(entry);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
        return new SemanticDocument(entry, hash);
    }

    public string CreateContent()
    {
        return CreateContent(Entry);
    }

    private static string CreateContent(SearchEntry entry)
    {
        return string.Join('\n',
            entry.DisplayName,
            entry.FileType.ToString(),
            entry.OnlyKey);
    }
}

internal sealed record SemanticContentDocument(SearchEntry Entry, DocumentContentSource Source);
