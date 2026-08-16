using System.Text;
using System.Buffers;
using Kitopia.Desktop.Features.Services.Onnx;
using OpenCvSharp;
using PluginCore;
using PluginCore.Onnx;
using Rect = OpenCvSharp.Rect;

namespace Kitopia.Desktop.Features.Ocr;

/// <summary>
/// Process-wide local PaddleOCR runner. It reuses model sessions for an indexing pass and releases
/// their native arenas when indexing completes.
/// </summary>
public sealed class PaddleOcrService : IOcrService, IDisposable
{
    private readonly IInferenceSessionManager _sessions;
    private readonly SemaphoreSlim _inferenceGate = new(1, 1);
    private readonly string[] _alphabet;
    private IInferenceSession? _detector;
    private IInferenceSession? _recognizer;

    public PaddleOcrService(IInferenceSessionManager sessions)
    {
        _sessions = sessions;
        _alphabet = OcrModelPackage.IsComplete()
            ? File.ReadAllLines(OcrModelPackage.DictionaryPath).Append(" ").ToArray()
            : [];
    }

    public bool IsAvailable => OcrModelPackage.IsComplete();

    public async Task<IReadOnlyList<OcrTextRegion>> RecognizeFileAsync(
        string imagePath,
        CancellationToken cancellationToken = default)
    {
        if (!IsAvailable) return [];
        using var image = Cv2.ImRead(imagePath, ImreadModes.Unchanged);
        if (image.Empty()) throw new InvalidDataException($"Unable to decode image '{imagePath}'.");
        return await RecognizeAsync(image, cancellationToken);
    }

    public async Task<IReadOnlyList<OcrTextRegion>> RecognizeAsync(
        Mat image,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (!IsAvailable) return [];

        await _inferenceGate.WaitAsync(cancellationToken);
        try
        {
            EnsureSessions();
            var detector = _detector ?? throw new InvalidOperationException("The OCR detector session has not been initialized.");
            var recognizer = _recognizer ?? throw new InvalidOperationException("The OCR recognizer session has not been initialized.");
            return await Task.Run(() => RecognizeCore(image, detector, recognizer, cancellationToken), cancellationToken);
        }
        finally
        {
            _inferenceGate.Release();
        }
    }

    public async Task ReleaseSessionsAsync()
    {
        await _inferenceGate.WaitAsync();
        try
        {
            var detector = _detector;
            var recognizer = _recognizer;
            _detector = null;
            _recognizer = null;
            detector?.Dispose();
            recognizer?.Dispose();
        }
        finally
        {
            _inferenceGate.Release();
        }
    }

    public void Dispose()
    {
        _detector?.Dispose();
        _recognizer?.Dispose();
        _inferenceGate.Dispose();
    }

    private void EnsureSessions()
    {
        _detector ??= _sessions.GetSession(OcrModelPackage.DetectorSignName)
                      ?? throw new InvalidOperationException("The OCR detector runtime is unavailable.");
        _recognizer ??= _sessions.GetSession(OcrModelPackage.RecognizerSignName)
                        ?? throw new InvalidOperationException("The OCR recognizer runtime is unavailable.");
    }

    private IReadOnlyList<OcrTextRegion> RecognizeCore(Mat image, IInferenceSession detector,
        IInferenceSession recognizer, CancellationToken cancellationToken)
    {
        using var detectorImage = PrepareDetectionImage(image);
        var regions = Detect(detector, detectorImage, cancellationToken);
        var results = new List<OcrTextRegion>(regions.Count);

        foreach (var region in regions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var crop = new Mat(detectorImage, region.Bounds);
            var text = Recognize(recognizer, crop);
            if (!string.IsNullOrWhiteSpace(text))
            {
                results.Add(new OcrTextRegion(text, region.Bounds.Left, region.Bounds.Top,
                    region.Bounds.Width, region.Bounds.Height));
            }
        }

        return results;
    }

    private static List<DetectedRegion> Detect(IInferenceSession session, Mat input, CancellationToken cancellationToken)
    {
        using var normalized = new Mat();
        input.ConvertTo(normalized, MatType.CV_32FC3, 1d / 255d);
        Cv2.Subtract(normalized, new Scalar(0.485, 0.456, 0.406), normalized);
        Cv2.Divide(normalized, new Scalar(0.229, 0.224, 0.225), normalized);
        var elementCount = 3 * input.Rows * input.Cols;
        var tensor = ArrayPool<float>.Shared.Rent(elementCount);
        try
        {
            OnnxInputDataTool.InputTensor(normalized, tensor.AsMemory(0, elementCount));
            var output = session.Infer(
            [
                (session.InputNames.First(), new Memory<int>([1, 3, input.Rows, input.Cols]),
                    tensor.AsMemory(0, elementCount))
            ]);

            unsafe
            {
                using var outputHandle = output.Pin();
                using var binary = Mat.FromPixelData(input.Rows, input.Cols, MatType.CV_32FC1,
                    (IntPtr)outputHandle.Pointer);
                using var threshold = new Mat();
                binary.ConvertTo(threshold, MatType.CV_8UC1, 255d);
                Cv2.Threshold(threshold, threshold, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);
                Cv2.FindContours(threshold, out Point[][] contours, out _, RetrievalModes.List,
                    ContourApproximationModes.ApproxTC89L1);

                var regions = new List<DetectedRegion>(contours.Length);
                foreach (var contour in contours)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var box = Cv2.MinAreaRect(contour);
                    if (Math.Min(box.Size.Width, box.Size.Height) < 3f) continue;
                    var expandedBox = new RotatedRect(box.Center,
                        new Size2f(box.Size.Width * 1.6f, box.Size.Height * 1.6f), box.Angle);
                    if (Math.Min(expandedBox.Size.Width, expandedBox.Size.Height) < 5f) continue;
                    var bounds = Clamp(Cv2.BoundingRect(expandedBox.Points()), input.Cols, input.Rows);
                    if (bounds.Width > 1 && bounds.Height > 1) regions.Add(new DetectedRegion(bounds));
                }

                return regions;
            }
        }
        finally
        {
            ArrayPool<float>.Shared.Return(tensor);
        }
    }

    private string Recognize(IInferenceSession session, Mat source)
    {
        using var input = PrepareRecognitionImage(source);
        using var normalized = new Mat();
        input.ConvertTo(normalized, MatType.CV_32FC3, 1d / 255d);
        var elementCount = 3 * input.Rows * input.Cols;
        var tensor = ArrayPool<float>.Shared.Rent(elementCount);
        try
        {
            OnnxInputDataTool.InputTensor(normalized, tensor.AsMemory(0, elementCount));
            var output = session.Infer(
            [
                (session.InputNames.First(), new Memory<int>([1, 3, input.Rows, input.Cols]),
                    tensor.AsMemory(0, elementCount))
            ]);
            var dimensions = session.OutputShape[0][2];
            var characterCount = output.Length / dimensions;
            var values = output.Span;
            var builder = new StringBuilder(characterCount);
            var previous = 0;
            for (var character = 0; character < characterCount; character++)
            {
                var label = 0;
                var confidence = float.NegativeInfinity;
                var offset = character * dimensions;
                for (var dimension = 0; dimension < dimensions; dimension++)
                {
                    var value = values[offset + dimension];
                    if (value > confidence)
                    {
                        confidence = value;
                        label = dimension;
                    }
                }

                if (label != 0 && label != previous && confidence >= 0.4f && label - 1 < _alphabet.Length)
                {
                    builder.Append(_alphabet[label - 1]);
                }
                previous = label;
            }

            return builder.ToString();
        }
        finally
        {
            ArrayPool<float>.Shared.Return(tensor);
        }
    }

    private static Mat PrepareDetectionImage(Mat source)
    {
        if (source.Channels() == 3) return PadToMultiple(source, 32, 32);

        using var bgr = ConvertToBgr(source);
        return PadToMultiple(bgr, 32, 32);
    }

    private static Mat PrepareRecognitionImage(Mat source)
    {
        if (source.Channels() == 3) return ResizeRecognitionImage(source);

        using var bgr = ConvertToBgr(source);
        return ResizeRecognitionImage(bgr);
    }

    private static Mat ResizeRecognitionImage(Mat source)
    {
        using var padded = PadToMultiple(source, 48, 32);
        var resizedWidth = Math.Max(1, (int)Math.Round(padded.Cols * 48d / padded.Rows));
        var result = new Mat();
        Cv2.Resize(padded, result, new Size(resizedWidth, 48));
        return result;
    }

    private static Mat PadToMultiple(Mat source, int heightMultiple, int widthMultiple)
    {
        var result = new Mat();
        Cv2.CopyMakeBorder(source, result, 0, RoundUp(source.Rows, heightMultiple) - source.Rows,
            0, RoundUp(source.Cols, widthMultiple) - source.Cols, BorderTypes.Constant,
            new Scalar(255, 255, 255));
        return result;
    }

    private static Mat ConvertToBgr(Mat source)
    {
        var conversion = source.Channels() switch
        {
            4 => ColorConversionCodes.BGRA2BGR,
            1 => ColorConversionCodes.GRAY2BGR,
            _ => throw new ArgumentException("OCR images must have one, three, or four channels.", nameof(source))
        };
        var result = new Mat();
        Cv2.CvtColor(source, result, conversion);
        return result;
    }

    private static Rect Clamp(Rect bounds, int maximumWidth, int maximumHeight)
    {
        var left = Math.Clamp(bounds.Left, 0, maximumWidth - 1);
        var top = Math.Clamp(bounds.Top, 0, maximumHeight - 1);
        var right = Math.Clamp(bounds.Right, left + 1, maximumWidth);
        var bottom = Math.Clamp(bounds.Bottom, top + 1, maximumHeight);
        return new Rect(left, top, right - left, bottom - top);
    }

    private static int RoundUp(int value, int multiple) => ((value + multiple - 1) / multiple) * multiple;

    private sealed record DetectedRegion(Rect Bounds);
}
