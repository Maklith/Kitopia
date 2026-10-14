using System.Numerics;
using System.Diagnostics;
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
    private const int MaximumImageTokens = EmbeddingGemmaImageProcessor.MaximumPatches / 9;
    private static readonly float[] EmptyNpuFeatures = new float[512];
    private static readonly float[] EmptyNpuImageFeatures = new float[MaximumImageTokens * 512];
    private readonly string _directoryPath;
    private readonly Tokenizer _tokenizer;
    private readonly SemaphoreSlim _inferenceGate = new(1, 1);
    private IInferenceSession? _textSession;
    private IInferenceSession? _visionSession;
    private string? _textDevice;
    private string? _visionDevice;
    private (int BatchSize, int SequenceLength, int ImageTokens)? _textShape;

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
        var isNpu = ConfigManger.Config.OnnxTargetDevices.GetValueOrDefault(
            EmbeddingGemmaModelPackage.TextModelSignName, "CPU") == "NPU(OpenVino)";
        foreach (var text in texts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var tokens = EncodeInput(text, maximumTokens);
            // Reuse a small set of shapes for normal indexing; masks exclude padding.
            var tokenLength = tokens.Length <= IndexingMaximumTokens
                ? (int)BitOperations.RoundUpToPowerOf2((uint)tokens.Length)
                : tokens.Length;
            var paddedLength = Math.Max(sequenceLength, tokenLength);
            // NPU requests retain batch=1; queue up to four independently bounded requests.
            if (batch.Count > 0 && (batch.Count == MaximumBatchSize
                                   || ((!isNpu || paddedLength > IndexingMaximumTokens)
                                       && (paddedLength * (batch.Count + 1) > BatchTokenBudget
                                           || (long)paddedLength * paddedLength * (batch.Count + 1) > BatchAttentionBudget))))
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
            var (pixels, positions, tokenCount) = await Task.Run(
                () => EmbeddingGemmaImageProcessor.Process(path, cancellationToken), cancellationToken);
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
                    var isNpu = target == "NPU(OpenVino)";
                    if (_visionSession is null)
                    {
                        try
                        {
                            var visionDimensions = new Dictionary<string, long>
                            {
                                ["s11"] = 1,
                                ["s35"] = EmbeddingGemmaImageProcessor.MaximumPatches,
                                ["u0"] = MaximumImageTokens
                            };
                            _visionSession = CreateSession(target,
                                "vision_encoder_q4.onnx",
                                target == "NPU(OpenVino)" ? visionDimensions : null);
                        }
                        catch (Exception exception) when (isNpu && PluginOverall.GetOnnxRuntime("CPU") is not null)
                        {
                            // A failed NPU compile must not be retried for every image in the same batch.
                            Log.Warning(exception, "NPU vision session initialization failed; using one CPU fallback session for this service instance.");
                            _visionSession = CreateSession("CPU", "vision_encoder_q4.onnx");
                        }
                    }
                    _visionDevice = target;
                    var output = _visionSession.Infer(
                        [("pixel_position_ids", new Memory<int>([1, EmbeddingGemmaImageProcessor.MaximumPatches, 2]), positions)],
                        [("pixel_values", new Memory<int>([1, EmbeddingGemmaImageProcessor.MaximumPatches, 768]), pixels)],
                        "image_features", cancellationToken);
                    if (isNpu)
                    {
                        if (output.Length != tokenCount * 512 && output.Length != MaximumImageTokens * 512)
                            throw new InvalidDataException($"Unexpected bounded vision embedding length {output.Length}.");
                        // CPU text sessions need only the valid prefix. NPU text pads a shorter
                        // result to the fixed 280-row input below.
                        if (ConfigManger.Config.OnnxTargetDevices.GetValueOrDefault(
                                EmbeddingGemmaModelPackage.TextModelSignName, "CPU") != "NPU(OpenVino)")
                            output = output[..(tokenCount * 512)];
                    }
                    return (output, tokenCount);
                }, cancellationToken);
                (features, softTokens) = result;
            }
            finally
            {
                _inferenceGate.Release();
            }
            if (features.Length != softTokens * 512 && features.Length != MaximumImageTokens * 512)
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
        var target = ConfigManger.Config.OnnxTargetDevices.GetValueOrDefault(EmbeddingGemmaModelPackage.TextModelSignName, "CPU");
        var isNpu = target == "NPU(OpenVino)";
        if (isNpu)
        {
            // Fixed buckets avoid recompiling the same weights for each text or image length.
            sequenceLength = Math.Max(IndexingMaximumTokens,
                (int)BitOperations.RoundUpToPowerOf2((uint)sequenceLength));
            if (imageFeatures.IsEmpty)
                imageFeatures = EmptyNpuImageFeatures;
            else if (imageFeatures.Length < EmptyNpuImageFeatures.Length)
            {
                var paddedFeatures = new float[EmptyNpuImageFeatures.Length];
                imageFeatures.CopyTo(paddedFeatures);
                imageFeatures = paddedFeatures;
            }
        }
        var ids = new long[batch.Count * sequenceLength];
        var mask = new long[ids.Length];
        for (var index = 0; index < batch.Count; index++)
        {
            batch[index].CopyTo(ids, index * sequenceLength);
            Array.Fill(mask, 1L, index * sequenceLength, batch[index].Length);
        }
        Memory<float> output;
        Memory<float>[]? individualOutputs = null;
        await _inferenceGate.WaitAsync(cancellationToken);
        try
        {
            output = await Task.Run(() =>
            {
                // Gemma's NPU batch>1 results are incorrect; concurrent runs each keep batch=1.
                var runtimeBatchSize = isNpu ? 1 : batch.Count;
                var features = imageFeatures;
                var unusedFeatures = isNpu ? EmptyNpuFeatures.AsMemory() : Memory<float>.Empty;
                (int, int, int)? shape = isNpu ? (runtimeBatchSize, sequenceLength, features.Length / 512) : null;
                if (_textDevice != target)
                {
                    _textSession?.Dispose();
                    _textSession = null;
                    _textShape = null;
                }
                else if (_textShape != shape)
                {
                    // One resident NPU graph prevents shape changes from doubling accelerator memory.
                    _textSession?.Dispose();
                    _textSession = null;
                }
                // Track the requested device so CPU recovery lasts until pause/reload or a device change.
                _textDevice = target;
                _textShape = shape;
                var runtimeTarget = target;
                for (var attempt = 0; ; attempt++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        var session = _textSession;
                        if (session is null)
                        {
                            try
                            {
                                session = CreateSession(runtimeTarget, "model_q4.onnx",
                                    isNpu ? new Dictionary<string, long>
                                    {
                                        ["batch_size"] = runtimeBatchSize,
                                        ["sequence_length"] = sequenceLength,
                                        ["num_image_tokens"] = features.Length / 512,
                                        ["num_video_tokens"] = 1,
                                        ["num_audio_tokens"] = 1
                                    } : null);
                                _textSession = session;
                            }
                            catch (Exception exception) when (runtimeTarget == "NPU(OpenVino)"
                                                               && PluginOverall.GetOnnxRuntime("CPU") is not null)
                            {
                                // Keep a compiler failure from causing one retry per pending item.
                                Log.Warning(exception, "NPU text session initialization failed; using one CPU fallback session for this service instance.");
                                runtimeTarget = "CPU";
                                session = CreateSession(runtimeTarget, "model_q4.onnx");
                                _textSession = session;
                            }
                        }
                        Memory<float> InferItem(int index) => session.Infer(
                            [
                                ("input_ids", new Memory<int>([runtimeBatchSize, sequenceLength]),
                                    ids.AsMemory(index * sequenceLength, runtimeBatchSize * sequenceLength)),
                                ("attention_mask", new Memory<int>([runtimeBatchSize, sequenceLength]),
                                    mask.AsMemory(index * sequenceLength, runtimeBatchSize * sequenceLength))
                            ],
                            [
                                // No placeholder token selects these zero rows; NPU rejects empty tensors.
                                ("image_features", new Memory<int>([features.Length / 512, 512]), features),
                                ("video_features", new Memory<int>([unusedFeatures.Length / 512, 512]), unusedFeatures),
                                ("audio_features", new Memory<int>([unusedFeatures.Length / 512, 512]), unusedFeatures)
                            ], "sentence_embedding", cancellationToken);
                        if (!isNpu || batch.Count == 1) return InferItem(0);
                        var batchOutputs = new Memory<float>[batch.Count];
                        Parallel.For(0, batch.Count, new ParallelOptions
                        {
                            MaxDegreeOfParallelism = MaximumBatchSize,
                            CancellationToken = cancellationToken
                        }, index => batchOutputs[index] = InferItem(index));
                        individualOutputs = batchOutputs;
                        return Memory<float>.Empty;
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
        if (individualOutputs is null && output.Length != batch.Count * VectorDimensions)
            throw new InvalidDataException($"Unexpected sentence embedding length {output.Length}.");
        var vectors = new float[batch.Count][];
        for (var index = 0; index < batch.Count; index++)
        {
            var row = individualOutputs is null
                ? output.Slice(index * VectorDimensions, VectorDimensions)
                : individualOutputs[index];
            if (row.Length != VectorDimensions)
                throw new InvalidDataException($"Unexpected sentence embedding length {row.Length}.");
            var vector = row.ToArray();
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

    private IInferenceSession CreateSession(string target, string fileName,
        IReadOnlyDictionary<string, long>? freeDimensionOverrides = null)
    {
        var runtime = PluginOverall.GetOnnxRuntime(target)
                      ?? throw new InvalidOperationException($"The {target} ONNX Runtime plugin is not available.");
        var session = runtime();
        try
        {
            var threads = Math.Max(1, Environment.ProcessorCount *
                Math.Clamp(ConfigManger.Config.indexingMaximumCpuUsagePercent, 1, 100) / 100);
            var gpuMemoryLimit = (long)Math.Clamp(ConfigManger.Config.inferenceMaximumGpuMemoryMiB, 128, 32768) * 1024 * 1024;
            var started = Stopwatch.GetTimestamp();
            if (target == "NPU(OpenVino)")
                Log.Information("Initializing {Model} for NPU with dimensions {@Dimensions}; compiled graphs are cached on disk.",
                    fileName, freeDimensionOverrides);
            session.InitSession(Path.Combine(_directoryPath, "onnx", fileName), useCpuMemoryArena: true, threads,
                gpuMemoryLimit, freeDimensionOverrides);
            if (target == "NPU(OpenVino)")
                Log.Information("NPU session initialization for {Model} completed in {ElapsedSeconds:F1} seconds.",
                    fileName, Stopwatch.GetElapsedTime(started).TotalSeconds);
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
            _textShape = null;
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
