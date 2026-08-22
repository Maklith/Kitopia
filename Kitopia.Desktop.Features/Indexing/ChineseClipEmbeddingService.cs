using System.Buffers;
using Kitopia.Desktop.Features.Services.Config;
using Kitopia.Desktop.Features.Services.Plugin;
using Kitopia.Desktop.Features.Search.Semantic;
using Kitopia.Desktop.Features.Imaging;
using OpenCvSharp;
using OpenCvSharp.Dnn;
using PluginCore.Onnx;
using System.Runtime.InteropServices;

namespace Kitopia.Desktop.Features.Indexing;

internal sealed class ChineseClipEmbeddingService : IDisposable
{
    private const int ImageInputElementCount = 3 * 224 * 224;
    private const int ImageInferenceBatchSize = 8;
    private static readonly float[] Mean = [0.48145466f, 0.4578275f, 0.40821073f];
    private static readonly float[] StandardDeviation = [0.26862954f, 0.26130258f, 0.27577711f];
    private static readonly Image2BlobParams ImageBlobParameters = new(
        new Scalar(
            1d / (255d * StandardDeviation[0]),
            1d / (255d * StandardDeviation[1]),
            1d / (255d * StandardDeviation[2])),
        new Size(224, 224),
        new Scalar(255d * Mean[0], 255d * Mean[1], 255d * Mean[2]),
        true,
        MatType.CV_32F,
        DataLayout.NCHW,
        ImagePaddingMode.NULL,
        Scalar.All(0));
    private readonly ChineseClipTokenizer _tokenizer;
    private readonly SemaphoreSlim _initializationGate = new(1, 1);
    private readonly SemaphoreSlim _imageInferenceGate = new(1, 1);
    private readonly SemaphoreSlim _textInferenceGate = new(1, 1);
    private IInferenceSession? _imageSession;
    private IInferenceSession? _textSession;

    private ChineseClipEmbeddingService(ChineseClipTokenizer tokenizer)
    {
        _tokenizer = tokenizer;
        ModelId = $"chinese-clip-rn50-int8:1024:{new FileInfo(ChineseClipModelPackage.ImageModelPath).Length}:{new FileInfo(ChineseClipModelPackage.TextModelPath).Length}:{tokenizer.Fingerprint}";
    }

    public string ModelId { get; }

    public static bool TryCreate(out ChineseClipEmbeddingService? service)
    {
        service = null;
        try
        {
            if (!ChineseClipModelPackage.IsComplete()) return false;
            service = new ChineseClipEmbeddingService(ChineseClipTokenizer.Load(ChineseClipModelPackage.VocabularyPath));
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<float[]> EmbedImageAsync(string path, CancellationToken cancellationToken)
    {
        return (await EmbedImagesAsync([path], cancellationToken))[0];
    }

    public async Task<IReadOnlyList<float[]>> EmbedImagesAsync(
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(paths.Count, 1);
        await _imageInferenceGate.WaitAsync(cancellationToken);
        try
        {
            await EnsureImageSessionAsync(cancellationToken);
            var embeddings = new List<float[]>(paths.Count);
            foreach (var batch in paths.Chunk(ImageInferenceBatchSize))
            {
                var batchEmbeddings = await EmbedImageBatchAsync(batch, cancellationToken);
                embeddings.AddRange(batchEmbeddings);
            }

            return embeddings;
        }
        finally
        {
            _imageInferenceGate.Release();
        }
    }

    private async Task<IReadOnlyList<float[]>> EmbedImageBatchAsync(
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken)
    {
        var inputLength = checked(paths.Count * ImageInputElementCount);
        var input = ArrayPool<float>.Shared.Rent(inputLength);
        var images = new List<Mat>(paths.Count);
        try
        {
            foreach (var path in paths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                images.Add(ImageInputLoader.LoadBgr(path, ImageInputLoader.MaximumEmbeddingPixels));
            }

            using var blob = Cv2.Dnn.BlobFromImagesWithParams(images, ImageBlobParameters);
            if (blob.Type() != MatType.CV_32FC1 || blob.Total() != inputLength)
            {
                throw new InvalidDataException(
                    $"Chinese-CLIP preprocessing produced a {blob.Type()} tensor with {blob.Total()} values; expected {inputLength} CV_32FC1 values.");
            }

            Marshal.Copy(blob.Data, input, 0, inputLength);

            var output = await Task.Run(() => _imageSession!.Infer(
                [("image", new Memory<int>([paths.Count, 3, 224, 224]), input.AsMemory(0, inputLength))]), cancellationToken);
            const int dimensions = 1024;
            if (output.Length != paths.Count * dimensions)
            {
                throw new InvalidDataException($"The image model returned {output.Length} values for {paths.Count} images.");
            }

            var embeddings = new float[paths.Count][];
            for (var index = 0; index < paths.Count; index++)
            {
                embeddings[index] = Normalize(output.Span.Slice(index * dimensions, dimensions));
            }

            return embeddings;
        }
        finally
        {
            foreach (var image in images)
            {
                image.Dispose();
            }

            ArrayPool<float>.Shared.Return(input);
        }
    }

    public async Task<float[]> EmbedTextAsync(string text, CancellationToken cancellationToken)
    {
        await _textInferenceGate.WaitAsync(cancellationToken);
        try
        {
            await EnsureTextSessionAsync(cancellationToken);
            var tokens = _tokenizer.Encode(text, ChineseClipModelPackage.TextContextLength);
            var output = await Task.Run(() => _textSession!.InferInt64(
                [("text", new Memory<int>([1, tokens.Length]), new Memory<long>(tokens))]), cancellationToken);
            return Normalize(output.Span);
        }
        finally
        {
            _textInferenceGate.Release();
        }
    }

    public static string CreateImageFingerprint(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException("Image file was not found.", path);
        return $"{info.Length}:{info.LastWriteTimeUtc.Ticks}";
    }

    public async Task ReleaseSessionsAsync()
    {
        await ReleaseImageSessionAsync();

        await _textInferenceGate.WaitAsync();
        try
        {
            var session = _textSession;
            _textSession = null;
            session?.Dispose();
        }
        finally
        {
            _textInferenceGate.Release();
        }
    }

    /// <summary>
    /// Releases only the image encoder. Image indexing can then run OCR/BGE without keeping
    /// the considerably larger image model session alive.
    /// </summary>
    public async Task ReleaseImageSessionAsync()
    {
        await _imageInferenceGate.WaitAsync();
        try
        {
            var session = _imageSession;
            _imageSession = null;
            session?.Dispose();
        }
        finally
        {
            _imageInferenceGate.Release();
        }
    }

    public void Dispose()
    {
        _imageSession?.Dispose();
        _textSession?.Dispose();
        _initializationGate.Dispose();
        _imageInferenceGate.Dispose();
        _textInferenceGate.Dispose();
    }

    private async Task EnsureImageSessionAsync(CancellationToken cancellationToken)
    {
        if (_imageSession is not null) return;
        await _initializationGate.WaitAsync(cancellationToken);
        try
        {
            if (_imageSession is null)
            {
                _imageSession = CreateSession(ChineseClipModelPackage.ImageModelSignName, ChineseClipModelPackage.ImageModelPath);
            }
        }
        finally
        {
            _initializationGate.Release();
        }
    }

    private async Task EnsureTextSessionAsync(CancellationToken cancellationToken)
    {
        if (_textSession is not null) return;
        await _initializationGate.WaitAsync(cancellationToken);
        try
        {
            if (_textSession is null)
            {
                _textSession = CreateSession(ChineseClipModelPackage.TextModelSignName, ChineseClipModelPackage.TextModelPath);
            }
        }
        finally
        {
            _initializationGate.Release();
        }
    }

    private static IInferenceSession CreateSession(string signName, string path)
    {
        var target = ConfigManger.Config.OnnxTargetDevices.TryGetValue(signName, out var configuredTarget)
            ? configuredTarget
            : "CPU";
        var runtime = PluginOverall.GetOnnxRuntime(target)
                      ?? throw new InvalidOperationException($"The {target} ONNX Runtime plugin is not available.");
        var session = runtime();
        // Both CLIP inputs have a dynamic batch/sequence dimension. ORT's CPU arena
        // otherwise retains the largest indexing batch until the session is disposed.
        session.InitSession(path, useCpuMemoryArena: false);
        return session;
    }

    private static float[] Normalize(ReadOnlySpan<float> values)
    {
        var result = values.ToArray();
        unsafe
        {
            fixed (float* vectorData = result)
            using (var vector = Mat.FromPixelData(1, result.Length, MatType.CV_32FC1, (IntPtr)vectorData))
            {
                Cv2.Normalize(vector, vector, 1d, 0d, NormTypes.L2);
            }
        }

        return result;
    }
}

internal sealed class ChineseClipTokenizer
{
    private readonly BertWordPieceTokenizer _inner;

    private ChineseClipTokenizer(BertWordPieceTokenizer inner)
    {
        _inner = inner;
        Fingerprint = inner.GetFingerprint();
    }

    public string Fingerprint { get; }

    public static ChineseClipTokenizer Load(string vocabularyPath) => new(BertWordPieceTokenizer.LoadVocabulary(vocabularyPath));

    public long[] Encode(string text, int contextLength)
    {
        var tokens = _inner.Encode(text, contextLength);
        if (tokens.Length == contextLength) return tokens;
        var padded = new long[contextLength];
        tokens.CopyTo(padded, 0);
        return padded;
    }
}
