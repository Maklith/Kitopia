using System.Numerics;
using System.Security.Cryptography;
using Kitopia.Desktop.Features.Services.Config;
using Kitopia.Desktop.Features.Services.Plugin;
using PluginCore.Onnx;
using Serilog;
using Tokenizers.HuggingFace.Tokenizer;

namespace Kitopia.Desktop.Features.Search.Semantic;

internal sealed class EmbeddingGemmaEmbeddingService : IDisposable
{
    public const int VectorDimensions = 768;
    public const int DocumentMaximumTokens = 8192;
    public const int IndexingMaximumTokens = 1024;
    public const int MetadataMaximumTokens = 1024;
    public const int QueryMaximumTokens = 512;
    public const string QueryInstruction = "task: search result | query: ";
    private const int BatchTokenBudget = 2048;
    private const long BatchAttentionBudget = 1024L * 1024;
    private const int MaximumBatchSize = 4;
    private readonly string _directoryPath;
    private readonly Tokenizer _tokenizer;
    private readonly SemaphoreSlim _inferenceGate = new(1, 1);
    private IInferenceSession? _textSession;
    private IInferenceSession? _visionSession;
    private string? _textDevice;
    private string? _visionDevice;

    internal EmbeddingGemmaEmbeddingService(string directoryPath)
    {
        _directoryPath = directoryPath;
        using var tokenizerFile = File.OpenRead(Path.Combine(directoryPath, "tokenizer.json"));
        ModelId = $"embeddinggemma-2-onnx-q4:{EmbeddingGemmaModelPackage.Revision}:768:ctx{DocumentMaximumTokens}:chunk{IndexingMaximumTokens}:metadata{MetadataMaximumTokens}:vision280:v2:{Convert.ToHexString(SHA256.HashData(tokenizerFile))}";
        _tokenizer = Tokenizer.FromFile(Path.Combine(directoryPath, "tokenizer.json"));
    }

    public string ModelId { get; }

    public static bool TryCreate(out EmbeddingGemmaEmbeddingService? service)
    {
        service = null;
        try
        {
            if (!EmbeddingGemmaModelPackage.IsComplete()) return false;
            service = new EmbeddingGemmaEmbeddingService(EmbeddingGemmaModelPackage.DirectoryPath);
            return true;
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "Failed to load the EmbeddingGemma 2 Q4 processor.");
            return false;
        }
    }

    public static string FormatDocument(string text, string? title = null) =>
        $"title: {(string.IsNullOrWhiteSpace(title) ? "none" : title)} | text: {text}";

    public int CountDocumentTokens(ReadOnlySpan<char> text, string? title) =>
        _tokenizer.Encode(FormatDocument(text.ToString(), title), true).First().Ids.Count;

    internal long[] EncodeInput(string text, int maximumTokens)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumTokens, 2);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumTokens, DocumentMaximumTokens);
        var ids = _tokenizer.Encode(text, true).First().Ids;
        var tokens = new long[Math.Min(ids.Count, maximumTokens)];
        for (var index = 0; index < tokens.Length; index++) tokens[index] = ids[index];
        // Right truncation reserves the tokenizer's EOS, as in Hugging Face post-processing.
        if (ids.Count > maximumTokens) tokens[^1] = 1;
        return tokens;
    }

    public async Task<IReadOnlyList<float[]>> EmbedAsync(
        IReadOnlyList<string> texts, int maximumTokens, CancellationToken cancellationToken)
    {
        if (texts.Count == 0) return [];
        var vectors = new List<float[]>(texts.Count);
        var batch = new List<long[]>(MaximumBatchSize);
        var sequenceLength = 0;
        foreach (var text in texts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var tokens = EncodeInput(text, maximumTokens);
            // Reuse a small set of shapes for normal indexing; masks exclude padding.
            var tokenLength = tokens.Length <= IndexingMaximumTokens
                ? (int)BitOperations.RoundUpToPowerOf2((uint)tokens.Length)
                : tokens.Length;
            var paddedLength = Math.Max(sequenceLength, tokenLength);
            // Attention allocations grow with sequence length squared, not just token count.
            if (batch.Count > 0 && (batch.Count == MaximumBatchSize
                                   || paddedLength * (batch.Count + 1) > BatchTokenBudget
                                   || (long)paddedLength * paddedLength * (batch.Count + 1) > BatchAttentionBudget))
            {
                vectors.AddRange(await InferTextBatchAsync(batch, sequenceLength, Memory<float>.Empty, cancellationToken));
                batch.Clear();
                sequenceLength = 0;
            }
            batch.Add(tokens);
            sequenceLength = Math.Max(sequenceLength, tokenLength);
        }
        if (batch.Count > 0)
            vectors.AddRange(await InferTextBatchAsync(batch, sequenceLength, Memory<float>.Empty, cancellationToken));
        return vectors;
    }

    public async Task<IReadOnlyList<float[]>> EmbedImagesAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken)
    {
        if (paths.Count == 0) return [];
        var vectors = new List<float[]>(paths.Count);
        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Memory<float> features;
            int softTokens;
            await _inferenceGate.WaitAsync(cancellationToken);
            try
            {
                var result = await Task.Run(() =>
                {
                    var target = ConfigManger.Config.OnnxTargetDevices.GetValueOrDefault(EmbeddingGemmaModelPackage.VisionModelSignName, "CPU");
                    if (_visionDevice != target)
                    {
                        _visionSession?.Dispose();
                        _visionSession = null;
                    }
                    _visionSession ??= CreateSession(target, "vision_encoder_q4.onnx");
                    _visionDevice = target;
                    var (pixels, positions, tokenCount) = EmbeddingGemmaImageProcessor.Process(path);
                    var output = _visionSession.Infer(
                        [("pixel_position_ids", new Memory<int>([1, EmbeddingGemmaImageProcessor.MaximumPatches, 2]), positions)],
                        [("pixel_values", new Memory<int>([1, EmbeddingGemmaImageProcessor.MaximumPatches, 768]), pixels)],
                        "image_features", cancellationToken);
                    return (output, tokenCount);
                }, cancellationToken);
                (features, softTokens) = result;
            }
            finally
            {
                _inferenceGate.Release();
            }
            if (features.Length != softTokens * 512)
                throw new InvalidDataException($"The vision encoder returned {features.Length} values for {softTokens} tokens.");
            var ids = new long[softTokens + 4];
            ids[0] = 2;
            ids[1] = 255999;
            Array.Fill(ids, 258880L, 2, softTokens);
            ids[^2] = 258882;
            ids[^1] = 1;
            vectors.AddRange(await InferTextBatchAsync([ids], ids.Length, features, cancellationToken));
        }
        return vectors;
    }

    private async Task<float[][]> InferTextBatchAsync(
        IReadOnlyList<long[]> batch, int sequenceLength, Memory<float> imageFeatures, CancellationToken cancellationToken)
    {
        var ids = new long[batch.Count * sequenceLength];
        var mask = new long[ids.Length];
        for (var index = 0; index < batch.Count; index++)
        {
            batch[index].CopyTo(ids, index * sequenceLength);
            Array.Fill(mask, 1L, index * sequenceLength, batch[index].Length);
        }
        Memory<float> output;
        await _inferenceGate.WaitAsync(cancellationToken);
        try
        {
            output = await Task.Run(() =>
            {
                var target = ConfigManger.Config.OnnxTargetDevices.GetValueOrDefault(EmbeddingGemmaModelPackage.TextModelSignName, "CPU");
                if (_textDevice != target)
                {
                    _textSession?.Dispose();
                    _textSession = null;
                }
                // Track the requested device so CPU recovery lasts until pause/reload or a device change.
                _textDevice = target;
                var runtimeTarget = target;
                for (var attempt = 0; ; attempt++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        _textSession ??= CreateSession(runtimeTarget, "model_q4.onnx");
                        return _textSession.Infer(
                            [
                                ("input_ids", new Memory<int>([batch.Count, sequenceLength]), ids),
                                ("attention_mask", new Memory<int>([batch.Count, sequenceLength]), mask)
                            ],
                            [
                                ("image_features", new Memory<int>([imageFeatures.Length / 512, 512]), imageFeatures),
                                ("video_features", new Memory<int>([0, 512]), Memory<float>.Empty),
                                ("audio_features", new Memory<int>([0, 512]), Memory<float>.Empty)
                            ], "sentence_embedding", cancellationToken);
                    }
                    catch (OutOfMemoryException exception) when (runtimeTarget == "GPU(CUDA)"
                                                               && (_textSession is null || _textSession.Device == "GPU(CUDA)"))
                    {
                        _textSession?.Dispose();
                        _textSession = null;
                        if (_visionSession?.Device == "GPU(CUDA)")
                        {
                            _visionSession.Dispose();
                            _visionSession = null;
                        }
                        cancellationToken.ThrowIfCancellationRequested();
                        if (attempt == 0)
                        {
                            Log.Warning(exception, "CUDA allocation failed for embedding batch {Count} x {Length}; retrying with a fresh session.",
                                batch.Count, sequenceLength);
                        }
                        else if (attempt == 1 && PluginOverall.GetOnnxRuntime("CPU") is not null)
                        {
                            Log.Warning(exception, "CUDA allocation failed after reload for embedding batch {Count} x {Length}; using CPU until sessions are released.",
                                batch.Count, sequenceLength);
                            runtimeTarget = "CPU";
                        }
                        else
                        {
                            throw;
                        }
                    }
                }
            }, cancellationToken);
        }
        finally
        {
            _inferenceGate.Release();
        }
        if (output.Length != batch.Count * VectorDimensions)
            throw new InvalidDataException($"Unexpected sentence embedding length {output.Length}.");
        var vectors = new float[batch.Count][];
        for (var index = 0; index < batch.Count; index++)
        {
            var vector = output.Span.Slice(index * VectorDimensions, VectorDimensions).ToArray();
            var squaredLength = 0d;
            foreach (var value in vector)
            {
                if (!float.IsFinite(value)) throw new InvalidDataException("The embedding contains a non-finite value.");
                squaredLength += (double)value * value;
            }
            if (squaredLength < 1e-16) throw new InvalidDataException("The model returned a zero embedding.");
            var length = Math.Sqrt(squaredLength);
            for (var component = 0; component < vector.Length; component++) vector[component] = (float)(vector[component] / length);
            vectors[index] = vector;
        }
        return vectors;
    }

    private IInferenceSession CreateSession(string target, string fileName)
    {
        var runtime = PluginOverall.GetOnnxRuntime(target)
                      ?? throw new InvalidOperationException($"The {target} ONNX Runtime plugin is not available.");
        var session = runtime();
        try
        {
            var threads = Math.Max(1, Environment.ProcessorCount *
                Math.Clamp(ConfigManger.Config.indexingMaximumCpuUsagePercent, 1, 100) / 100);
            var gpuMemoryLimit = (long)Math.Clamp(ConfigManger.Config.inferenceMaximumGpuMemoryMiB, 128, 32768) * 1024 * 1024;
            session.InitSession(Path.Combine(_directoryPath, "onnx", fileName), useCpuMemoryArena: true, threads, gpuMemoryLimit);
            return session;
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    public async Task ReleaseSessionsAsync()
    {
        await _inferenceGate.WaitAsync();
        try
        {
            _visionSession?.Dispose();
            _textSession?.Dispose();
            _visionSession = null;
            _textSession = null;
            _visionDevice = null;
            _textDevice = null;
        }
        finally
        {
            _inferenceGate.Release();
        }
    }

    public void Dispose()
    {
        _visionSession?.Dispose();
        _textSession?.Dispose();
        _tokenizer.Dispose();
        _inferenceGate.Dispose();
    }
}
