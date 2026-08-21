using BenchmarkDotNet.Attributes;
using System.Buffers;
using OpenCvSharp;
using PluginCore.Onnx;

namespace KitopiaBenchmark;

[Config(typeof(InProcessConfig))]
[MemoryDiagnoser]
public unsafe class OcrPreprocessingBenchmark
{
    private const int RecognitionWidth = 320;
    private const int RecognitionTimesteps = 40;
    private const int RecognitionLabels = 6906;
    private Mat _source = null!;
    private Mat _detectorInput = null!;
    private Mat _recognizerInput = null!;
    private Memory<float> _detectorOutput;
    private Memory<float> _recognizerOutput;
    private int _detectorElementCount;
    private int _recognizerElementCount;

    [Params(640, 1920)]
    public int DetectorWidth { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var sourceHeight = (int)Math.Round(DetectorWidth * 9d / 16d);
        _source = new Mat(sourceHeight, DetectorWidth, MatType.CV_8UC3);
        Cv2.Randu(_source, Scalar.All(0), Scalar.All(256));

        var detectorHeight = RoundUp(sourceHeight, 32);
        _detectorInput = new Mat(detectorHeight, DetectorWidth, MatType.CV_32FC3);
        Cv2.Randu(_detectorInput, Scalar.All(-2), Scalar.All(2));
        _detectorElementCount = 3 * detectorHeight * DetectorWidth;

        _recognizerInput = new Mat(48, RecognitionWidth, MatType.CV_32FC3);
        Cv2.Randu(_recognizerInput, Scalar.All(0), Scalar.All(1));
        _recognizerElementCount = 3 * _recognizerInput.Rows * _recognizerInput.Cols;

        _detectorOutput = CreateOutput(detectorHeight * DetectorWidth);
        _recognizerOutput = CreateOutput(RecognitionTimesteps * RecognitionLabels);

        VerifyPreparation();
        VerifyTensorLayout(_detectorInput, _detectorElementCount);
        VerifyTensorLayout(_recognizerInput, _recognizerElementCount);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _source.Dispose();
        _detectorInput.Dispose();
        _recognizerInput.Dispose();
    }

    [Benchmark(Description = "Before: detector image padding")]
    public int LegacyDetectorImagePadding()
    {
        using var result = LegacyPrepareDetectionImage(_source);
        return result.Rows * result.Cols;
    }

    [Benchmark(Description = "After: detector image padding")]
    public int CurrentDetectorImagePadding()
    {
        using var result = CurrentPrepareDetectionImage(_source);
        return result.Rows * result.Cols;
    }

    [Benchmark(Description = "Before: detector NCHW tensor")]
    public Memory<float> LegacyDetectorTensor() => LegacyInputTensor(_detectorInput, _detectorElementCount);

    [Benchmark(Description = "After: detector NCHW tensor")]
    public float CurrentDetectorTensor() => CreatePooledTensor(_detectorInput, _detectorElementCount);

    [Benchmark(Description = "Before: recognizer NCHW tensor")]
    public Memory<float> LegacyRecognizerTensor() => LegacyInputTensor(_recognizerInput, _recognizerElementCount);

    [Benchmark(Description = "After: recognizer NCHW tensor")]
    public float CurrentRecognizerTensor() => CreatePooledTensor(_recognizerInput, _recognizerElementCount);

    [Benchmark(Description = "Before: detector output copy")]
    public float LegacyDetectorOutputCopy()
    {
        var output = _detectorOutput.ToArray();
        return output[^1];
    }

    [Benchmark(Description = "After: detector output pin")]
    public float CurrentDetectorOutputPin()
    {
        using var handle = _detectorOutput.Pin();
        return ((float*)handle.Pointer)[_detectorOutput.Length - 1];
    }

    [Benchmark(Description = "Before: recognizer output copy")]
    public float LegacyRecognizerOutputCopy()
    {
        var output = _recognizerOutput.ToArray();
        return output[^1];
    }

    [Benchmark(Description = "After: recognizer output span")]
    public float CurrentRecognizerOutputSpan() => _recognizerOutput.Span[^1];

    private void VerifyPreparation()
    {
        using var legacy = LegacyPrepareDetectionImage(_source);
        using var current = CurrentPrepareDetectionImage(_source);
        if (legacy.Size() != current.Size() || Cv2.Norm(legacy, current, NormTypes.L1) != 0d)
        {
            throw new InvalidOperationException("The detector padding implementations produced different images.");
        }
    }

    private static void VerifyTensorLayout(Mat image, int elementCount)
    {
        var legacy = LegacyInputTensor(image, elementCount);
        var current = new float[elementCount];
        OnnxInputDataTool.InputTensor(image, current);
        if (!legacy.Span.SequenceEqual(current))
        {
            throw new InvalidOperationException("OpenCV channel conversion changed the OCR tensor layout.");
        }
    }

    private static Mat LegacyPrepareDetectionImage(Mat source)
    {
        using var bgr = new Mat();
        source.CopyTo(bgr);
        var result = new Mat(RoundUp(bgr.Rows, 32), RoundUp(bgr.Cols, 32), MatType.CV_8UC3,
            new Scalar(255, 255, 255));
        using var content = new Mat(result, new Rect(0, 0, bgr.Cols, bgr.Rows));
        bgr.CopyTo(content);
        return result;
    }

    private static Mat CurrentPrepareDetectionImage(Mat source)
    {
        var result = new Mat();
        Cv2.CopyMakeBorder(source, result, 0, RoundUp(source.Rows, 32) - source.Rows,
            0, RoundUp(source.Cols, 32) - source.Cols, BorderTypes.Constant, new Scalar(255, 255, 255));
        return result;
    }

    private static unsafe Memory<float> LegacyInputTensor(Mat image, int size)
    {
        var channels = image.Split();
        try
        {
            var tensor = new float[size];
            using var handle = tensor.AsMemory().Pin();
            var bytesPerChannel = channels[0].Total() * sizeof(float);
            Buffer.MemoryCopy(channels[0].DataPointer, handle.Pointer, tensor.Length * sizeof(float), bytesPerChannel);
            Buffer.MemoryCopy(channels[1].DataPointer, (byte*)handle.Pointer + bytesPerChannel,
                tensor.Length * sizeof(float) - bytesPerChannel, bytesPerChannel);
            Buffer.MemoryCopy(channels[2].DataPointer, (byte*)handle.Pointer + 2 * bytesPerChannel,
                tensor.Length * sizeof(float) - 2 * bytesPerChannel, bytesPerChannel);
            return tensor;
        }
        finally
        {
            foreach (var channel in channels)
            {
                channel.Dispose();
            }
        }
    }

    private static float CreatePooledTensor(Mat image, int elementCount)
    {
        var tensor = ArrayPool<float>.Shared.Rent(elementCount);
        try
        {
            OnnxInputDataTool.InputTensor(image, tensor.AsMemory(0, elementCount));
            return tensor[elementCount - 1];
        }
        finally
        {
            ArrayPool<float>.Shared.Return(tensor);
        }
    }

    private static Memory<float> CreateOutput(int length)
    {
        var output = new float[length];
        for (var index = 0; index < output.Length; index++)
        {
            output[index] = index / (float)output.Length;
        }

        return output;
    }

    private static int RoundUp(int value, int multiple) => ((value + multiple - 1) / multiple) * multiple;
}
