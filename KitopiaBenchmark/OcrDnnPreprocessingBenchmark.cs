using System.Buffers;
using BenchmarkDotNet.Attributes;
using Kitopia.Desktop.Features.Ocr;
using OpenCvSharp;
using PluginCore.Onnx;

namespace KitopiaBenchmark;

/// <summary>
/// Compares the current OCR normalization/NCHW path with OpenCV DNN blob creation.
/// Image padding and recognition resizing are prepared once because this benchmark
/// measures tensor preprocessing, not OCR geometry.
/// </summary>
[Config(typeof(InProcessConfig))]
[MemoryDiagnoser]
public class OcrDnnPreprocessingBenchmark
{
    private const double DetectorMean0 = 0.485d;
    private const double DetectorMean1 = 0.456d;
    private const double DetectorMean2 = 0.406d;
    private const double DetectorStd0 = 0.229d;
    private const double DetectorStd1 = 0.224d;
    private const double DetectorStd2 = 0.225d;
    private const double RecognizerMean = 0.5d;
    private const double RecognizerStd = 0.5d;
    private const int RecognitionHeight = 48;
    private const int RecognitionWidth = 320;

    private Mat _detectorImage = null!;
    private Mat _recognizerImage = null!;
    private readonly Scalar _detectorMean = new(DetectorMean0, DetectorMean1, DetectorMean2);
    private readonly Scalar _detectorStandardDeviation = new(DetectorStd0, DetectorStd1, DetectorStd2);
    private readonly Scalar _recognizerMean = Scalar.All(RecognizerMean);
    private readonly Scalar _recognizerStandardDeviation = Scalar.All(RecognizerStd);
    private int _detectorElementCount;
    private int _recognizerElementCount;

    [Params(640, 1920)]
    public int DetectorWidth { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var sourceHeight = (int)Math.Round(DetectorWidth * 9d / 16d);
        var source = new Mat(sourceHeight, DetectorWidth, MatType.CV_8UC3);
        try
        {
            Cv2.Randu(source, Scalar.All(0), Scalar.All(256));
            _detectorImage = new Mat();
            Cv2.CopyMakeBorder(
                source,
                _detectorImage,
                0,
                RoundUp(source.Rows, 32) - source.Rows,
                0,
                RoundUp(source.Cols, 32) - source.Cols,
                BorderTypes.Constant,
                Scalar.All(255));
        }
        finally
        {
            source.Dispose();
        }

        _recognizerImage = new Mat(RecognitionHeight, RecognitionWidth, MatType.CV_8UC3);
        Cv2.Randu(_recognizerImage, Scalar.All(0), Scalar.All(256));
        _detectorElementCount = checked(3 * _detectorImage.Rows * _detectorImage.Cols);
        _recognizerElementCount = 3 * _recognizerImage.Rows * _recognizerImage.Cols;
        VerifyEquivalent(_detectorImage, _detectorElementCount, _detectorMean, _detectorStandardDeviation);
        VerifyEquivalent(_recognizerImage, _recognizerElementCount, _recognizerMean, _recognizerStandardDeviation);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _detectorImage.Dispose();
        _recognizerImage.Dispose();
    }

    [Benchmark(Baseline = true, Description = "Current: detector normalize + NCHW")]
    public float CurrentDetectorTensor() =>
        CurrentTensorLast(_detectorImage, _detectorElementCount,
            _detectorMean, _detectorStandardDeviation);

    [Benchmark(Description = "DNN: detector blob + NCHW copy")]
    public float DnnDetectorBlob() =>
        DnnTensorLast(_detectorImage, _detectorElementCount, _detectorMean, _detectorStandardDeviation);

    [Benchmark(Description = "Current: recognizer normalize + NCHW")]
    public float CurrentRecognizerTensor() =>
        CurrentTensorLast(_recognizerImage, _recognizerElementCount,
            _recognizerMean, _recognizerStandardDeviation);

    [Benchmark(Description = "DNN: recognizer blob + NCHW copy")]
    public float DnnRecognizerBlob() =>
        DnnTensorLast(_recognizerImage, _recognizerElementCount, _recognizerMean, _recognizerStandardDeviation);

    private static float[] CurrentTensor(
        Mat image,
        int elementCount,
        Scalar mean,
        Scalar standardDeviation)
    {
        using var normalized = new Mat();
        image.ConvertTo(normalized, MatType.CV_32FC3, 1d / 255d);
        Cv2.Subtract(normalized, mean, normalized);
        Cv2.Divide(normalized, standardDeviation, normalized);
        var tensor = new float[elementCount];
        OnnxInputDataTool.InputTensor(normalized, tensor);
        return tensor;
    }

    private static float CurrentTensorLast(
        Mat image,
        int elementCount,
        Scalar mean,
        Scalar standardDeviation)
    {
        var tensor = ArrayPool<float>.Shared.Rent(elementCount);
        try
        {
            using var normalized = new Mat();
            image.ConvertTo(normalized, MatType.CV_32FC3, 1d / 255d);
            Cv2.Subtract(normalized, mean, normalized);
            Cv2.Divide(normalized, standardDeviation, normalized);
            OnnxInputDataTool.InputTensor(normalized, tensor.AsMemory(0, elementCount));
            return tensor[elementCount - 1];
        }
        finally
        {
            ArrayPool<float>.Shared.Return(tensor);
        }
    }

    private static float DnnTensorLast(
        Mat image,
        int elementCount,
        Scalar mean,
        Scalar standardDeviation)
    {
        var tensor = ArrayPool<float>.Shared.Rent(elementCount);
        try
        {
            PaddleOcrService.PreprocessToNchw(image, tensor, mean, standardDeviation);
            return tensor[elementCount - 1];
        }
        finally
        {
            ArrayPool<float>.Shared.Return(tensor);
        }
    }

    private static void VerifyEquivalent(
        Mat image,
        int elementCount,
        Scalar mean,
        Scalar standardDeviation)
    {
        var current = CurrentTensor(image, elementCount, mean, standardDeviation);
        var dnn = new float[elementCount];
        PaddleOcrService.PreprocessToNchw(image, dnn, mean, standardDeviation);

        var maximumDifference = 0f;
        for (var index = 0; index < elementCount; index++)
        {
            maximumDifference = Math.Max(maximumDifference, Math.Abs(current[index] - dnn[index]));
        }

        if (maximumDifference > 1e-5f)
        {
            throw new InvalidOperationException(
                $"DNN OCR preprocessing differs from the current path by {maximumDifference}.");
        }
    }

    private static int RoundUp(int value, int multiple) =>
        ((value + multiple - 1) / multiple) * multiple;
}
