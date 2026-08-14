using Kitopia.Desktop.Features.Services.Config;
using Kitopia.Desktop.Features.Services.Plugin;
using PluginCore.Onnx;

namespace Kitopia.Desktop.Features.Search.Semantic;

internal sealed class BgeOnnxEmbeddingService : IDisposable
{
    private const int EmbeddingDimensions = 512;
    private const int ModelMaximumTokens = 512;
    private const string SentenceEmbeddingOutputName = "sentence_embedding";
    private static readonly Lazy<BertWordPieceTokenizer?> PreviewTokenizer = new(LoadPreviewTokenizer);
    private readonly string _modelPath;
    private readonly string _modelId;
    private readonly BertWordPieceTokenizer _tokenizer;
    private readonly SemaphoreSlim _initializationGate = new(1, 1);
    private readonly SemaphoreSlim _inferenceGate = new(1, 1);
    private IInferenceSession? _session;

    private BgeOnnxEmbeddingService(string modelPath, BertWordPieceTokenizer tokenizer)
    {
        _modelPath = modelPath;
        _tokenizer = tokenizer;
        _modelId = $"bge-small-zh-v1.5-onnx-int8:512:content-256-overlap-48:{new FileInfo(modelPath).Length}:{tokenizer.GetFingerprint()}";
    }

    public const int VectorDimensions = EmbeddingDimensions;

    public const int MetadataMaximumTokens = 128;

    public const int DocumentMaximumTokens = 256;

    public const string QueryInstruction = "为这个句子生成表示以用于检索相关文章：";

    public string ModelId => _modelId;

    public int CountTokens(ReadOnlySpan<char> text) => _tokenizer.CountTokens(text);

    internal static int CountDocumentTokens(ReadOnlySpan<char> text)
    {
        return PreviewTokenizer.Value?.CountTokens(text) ?? text.Length;
    }

    public static bool TryCreate(out BgeOnnxEmbeddingService? service)
    {
        service = null;
        try
        {
            if (!ConfigManger.Config.enableSemanticSearch)
            {
                return false;
            }

            if (!BgeModelPackage.IsComplete())
            {
                return false;
            }

            service = new BgeOnnxEmbeddingService(
                BgeModelPackage.ModelPath,
                BertWordPieceTokenizer.Load(BgeModelPackage.TokenizerPath));
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<float[]>> EmbedAsync(
        IReadOnlyList<string> texts,
        int maximumTokens,
        CancellationToken cancellationToken)
    {
        if (texts.Count == 0)
        {
            return [];
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(maximumTokens, 2);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumTokens, ModelMaximumTokens);

        var tokenized = texts.Select(text => _tokenizer.Encode(text, maximumTokens)).ToArray();
        var sequenceLength = tokenized.Max(tokens => tokens.Length);
        var inputIds = new long[texts.Count * sequenceLength];
        var attentionMask = new long[inputIds.Length];
        var tokenTypeIds = new long[inputIds.Length];

        Array.Fill(inputIds, _tokenizer.PaddingTokenId);
        for (var batchIndex = 0; batchIndex < tokenized.Length; batchIndex++)
        {
            var tokens = tokenized[batchIndex];
            tokens.CopyTo(inputIds, batchIndex * sequenceLength);
            Array.Fill(attentionMask, 1L, batchIndex * sequenceLength, tokens.Length);
        }

        await _inferenceGate.WaitAsync(cancellationToken);
        try
        {
            // Keep session initialization, inference, and unloading mutually exclusive.
            // An ONNX session owns a sizeable native allocator arena which must be disposed
            // after an indexing pass instead of staying resident for the process lifetime.
            await EnsureSessionAsync(cancellationToken);
            var session = _session
                          ?? throw new InvalidOperationException("The inference session has not been initialized.");
            var output = await Task.Run(() => session.InferInt64(
                [
                    ("input_ids", new Memory<int>([texts.Count, sequenceLength]), new Memory<long>(inputIds)),
                    ("attention_mask", new Memory<int>([texts.Count, sequenceLength]), new Memory<long>(attentionMask)),
                    ("token_type_ids", new Memory<int>([texts.Count, sequenceLength]), new Memory<long>(tokenTypeIds))
                ],
                SentenceEmbeddingOutputName).ToArray(), cancellationToken);

            if (output.Length != texts.Count * EmbeddingDimensions)
            {
                throw new InvalidDataException(
                    $"Unexpected embedding output length {output.Length}; expected {texts.Count * EmbeddingDimensions}.");
            }

            var vectors = new float[texts.Count][];
            for (var index = 0; index < texts.Count; index++)
            {
                vectors[index] = Normalize(output.AsSpan(index * EmbeddingDimensions, EmbeddingDimensions));
            }

            return vectors;
        }
        finally
        {
            _inferenceGate.Release();
        }
    }

    /// <summary>
    /// Releases the native ONNX session after a background indexing pass. The tokenizer remains
    /// available, and the session is initialized again on the next embedding request.
    /// </summary>
    public async Task ReleaseSessionAsync()
    {
        await _inferenceGate.WaitAsync();
        try
        {
            var session = _session;
            _session = null;
            session?.Dispose();
        }
        finally
        {
            _inferenceGate.Release();
        }
    }

    public void Dispose()
    {
        _session?.Dispose();
        _initializationGate.Dispose();
        _inferenceGate.Dispose();
    }

    private async Task EnsureSessionAsync(CancellationToken cancellationToken)
    {
        if (_session is not null)
        {
            return;
        }

        await _initializationGate.WaitAsync(cancellationToken);
        try
        {
            if (_session is not null)
            {
                return;
            }

            var target = ConfigManger.Config.OnnxTargetDevices.TryGetValue(
                BgeModelPackage.ModelSignName, out var configuredTarget)
                ? configuredTarget
                : "CPU";
            var runtime = PluginOverall.GetOnnxRuntime(target)
                          ?? throw new InvalidOperationException($"The {target} ONNX Runtime plugin is not available.");
            _session = runtime();
            _session.InitSession(_modelPath);
        }
        finally
        {
            _initializationGate.Release();
        }
    }

    private static float[] Normalize(ReadOnlySpan<float> embedding)
    {
        var vector = embedding.ToArray();
        var squaredLength = 0d;
        foreach (var value in vector)
        {
            squaredLength += value * value;
        }

        var length = Math.Sqrt(squaredLength);
        if (length <= 0)
        {
            return vector;
        }

        for (var index = 0; index < vector.Length; index++)
        {
            vector[index] = (float)(vector[index] / length);
        }

        return vector;
    }

    private static BertWordPieceTokenizer? LoadPreviewTokenizer()
    {
        try
        {
            return File.Exists(BgeModelPackage.TokenizerPath)
                ? BertWordPieceTokenizer.Load(BgeModelPackage.TokenizerPath)
                : null;
        }
        catch
        {
            return null;
        }
    }
}
