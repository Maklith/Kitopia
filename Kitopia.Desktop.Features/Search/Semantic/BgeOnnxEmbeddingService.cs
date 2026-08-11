using Kitopia.Desktop.Features.Services.Config;
using Kitopia.Desktop.Features.Services.Plugin;
using PluginCore.Onnx;

namespace Kitopia.Desktop.Features.Search.Semantic;

internal sealed class BgeOnnxEmbeddingService : IDisposable
{
    private const int EmbeddingDimensions = 512;
    private const int ModelMaximumTokens = 512;
    private const string SentenceEmbeddingOutputName = "sentence_embedding";
    private static readonly string ManagedModelDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kitopia", "BGE_Model");
    private static readonly string LegacyModelDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "rag", "bge-small-zh-v1.5-onnx");

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
        _modelId = $"bge-small-zh-v1.5-onnx-int8:512:{new FileInfo(modelPath).Length}:{tokenizer.GetFingerprint()}";
    }

    public const int VectorDimensions = EmbeddingDimensions;

    public const int MetadataMaximumTokens = 128;

    public const int DocumentMaximumTokens = ModelMaximumTokens;

    public const string QueryInstruction = "为这个句子生成表示以用于检索相关文章：";

    public string ModelId => _modelId;

    public static bool TryCreate(out BgeOnnxEmbeddingService? service)
    {
        service = null;
        try
        {
            if (!ConfigManger.Config.enableSemanticSearch)
            {
                return false;
            }

            foreach (var modelDirectory in GetCandidateModelDirectories())
            {
                var modelPath = Path.Combine(modelDirectory, "quantized", "model_quantized.onnx");
                var modelDataPath = modelPath + "_data";
                var tokenizerPath = Path.Combine(modelDirectory, "tokenizer.json");
                if (!File.Exists(modelPath) || !File.Exists(modelDataPath) || !File.Exists(tokenizerPath))
                {
                    continue;
                }

                service = new BgeOnnxEmbeddingService(modelPath, BertWordPieceTokenizer.Load(tokenizerPath));
                return true;
            }

            return false;
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

        await EnsureSessionAsync(cancellationToken);
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
            var output = await Task.Run(() => _session!.InferInt64(
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

    public void Dispose()
    {
        _session?.Dispose();
        _initializationGate.Dispose();
        _inferenceGate.Dispose();
    }

    private static IEnumerable<string> GetCandidateModelDirectories()
    {
        var configuredDirectory = ConfigManger.Config.semanticSearchModelDirectory;
        if (IsSameDirectory(configuredDirectory, LegacyModelDirectory))
        {
            ConfigManger.Config.semanticSearchModelDirectory = ManagedModelDirectory;
            ConfigManger.Save("KitopiaConfig");
            configuredDirectory = ManagedModelDirectory;
        }

        yield return configuredDirectory;
        yield return Path.Combine(configuredDirectory, "bge-small-zh-v1.5-onnx");
    }

    private static bool IsSameDirectory(string left, string right)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
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

            var runtime = PluginOverall.GetOnnxRuntime("CPU")
                          ?? throw new InvalidOperationException("The CPU ONNX Runtime plugin is not available.");
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
}
