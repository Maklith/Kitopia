using System.Text;
using Kitopia.Desktop.Features.Services.Onnx;
using OpenCvSharp;
using PluginCore;
using PluginCore.Onnx;
using Rect = OpenCvSharp.Rect;

namespace Kitopia.Desktop.Features.Ocr;

/// <summary>
/// Process-wide local PaddleOCR runner. It creates disposable ONNX sessions per request so idle OCR
/// does not retain large native arenas.
/// </summary>
public sealed class PaddleOcrService : IOcrService
{
    private readonly IInferenceSessionManager _sessions;
    private readonly SemaphoreSlim _inferenceGate = new(1, 1);
    private readonly string[] _alphabet;

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
            return await Task.Run(() => RecognizeCore(image, cancellationToken), cancellationToken);
        }
        finally
        {
            _inferenceGate.Release();
        }
    }

    private IReadOnlyList<OcrTextRegion> RecognizeCore(Mat image, CancellationToken cancellationToken)
    {
        using var detector = _sessions.GetSession(OcrModelPackage.DetectorSignName);
        using var recognizer = _sessions.GetSession(OcrModelPackage.RecognizerSignName);
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
        using var normalized = input.Clone();
        Cv2.Normalize(normalized, normalized, 0, 1, NormTypes.MinMax, MatType.CV_32F);
        normalized.Add(new Scalar(-0.485, -0.456, -0.406));
        normalized.Mul(new Scalar(1d / 0.229, 1d / 0.224, 1d / 0.225));
        var output = session.Infer(
        [
            (session.InputNames.First(), new Memory<int>([1, 3, input.Rows, input.Cols]),
                OnnxInputDataTool.InputTensor(normalized, 3 * input.Rows * input.Cols))
        ]);

        var values = output.ToArray();
        unsafe
        {
            fixed (float* pointer = values)
            {
                using var binary = Mat.FromPixelData(input.Rows, input.Cols, MatType.CV_32FC1, (IntPtr)pointer);
                using var threshold = new Mat();
                binary.ConvertTo(threshold, MatType.CV_8UC1, 255d);
                Cv2.Threshold(threshold, threshold, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);
                Cv2.FindContours(threshold, out Point[][] contours, out _, RetrievalModes.List,
                    ContourApproximationModes.ApproxTC89L1);

                var regions = new List<DetectedRegion>();
                foreach (var contour in contours)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var box = Cv2.MinAreaRect(contour);
                    if (Math.Min(box.Size.Width, box.Size.Height) < 3f) continue;
                    var expanded = ExpandBox(box.Points(), 1.6f);
                    var expandedBox = Cv2.MinAreaRect(expanded);
                    if (Math.Min(expandedBox.Size.Width, expandedBox.Size.Height) < 5f) continue;
                    var bounds = Cv2.BoundingRect(expandedBox.Points());
                    bounds = Clamp(bounds, input.Cols, input.Rows);
                    if (bounds.Width > 1 && bounds.Height > 1) regions.Add(new DetectedRegion(bounds));
                }

                return regions;
            }
        }
    }

    private string Recognize(IInferenceSession session, Mat source)
    {
        using var input = PrepareRecognitionImage(source);
        Cv2.Normalize(input, input, 0, 1, NormTypes.MinMax, MatType.CV_32F);
        var output = session.Infer(
        [
            (session.InputNames.First(), new Memory<int>([1, 3, input.Rows, input.Cols]),
                OnnxInputDataTool.InputTensor(input, 3 * input.Rows * input.Cols))
        ]).ToArray();
        var dimensions = session.OutputShape[0][2];
        var characterCount = output.Length / dimensions;
        var builder = new StringBuilder();
        var previous = 0;
        for (var character = 0; character < characterCount; character++)
        {
            var label = 0;
            var confidence = float.NegativeInfinity;
            for (var dimension = 0; dimension < dimensions; dimension++)
            {
                var value = output[character * dimensions + dimension];
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

    private static Mat PrepareDetectionImage(Mat source)
    {
        using var bgr = EnsureBgr(source);
        var height = RoundUp(bgr.Rows, 32);
        var width = RoundUp(bgr.Cols, 32);
        var result = new Mat(height, width, MatType.CV_8UC3, new Scalar(255, 255, 255));
        bgr.CopyTo(new Mat(result, new Rect(0, 0, bgr.Cols, bgr.Rows)));
        return result;
    }

    private static Mat PrepareRecognitionImage(Mat source)
    {
        using var bgr = EnsureBgr(source);
        var width = RoundUp(bgr.Cols, 32);
        var height = RoundUp(bgr.Rows, 48);
        using var padded = new Mat(height, width, MatType.CV_8UC3, new Scalar(255, 255, 255));
        bgr.CopyTo(new Mat(padded, new Rect(0, 0, bgr.Cols, bgr.Rows)));
        var resizedWidth = Math.Max(1, (int)Math.Round(width * 48d / height));
        var result = new Mat();
        Cv2.Resize(padded, result, new Size(resizedWidth, 48));
        return result;
    }

    private static Mat EnsureBgr(Mat source)
    {
        var result = new Mat();
        switch (source.Channels())
        {
            case 4:
                Cv2.CvtColor(source, result, ColorConversionCodes.BGRA2BGR);
                break;
            case 1:
                Cv2.CvtColor(source, result, ColorConversionCodes.GRAY2BGR);
                break;
            default:
                source.CopyTo(result);
                break;
        }
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

    private static Point2f[] ExpandBox(IReadOnlyList<Point2f> points, float ratio)
    {
        var center = new Point2f(points.Average(point => point.X), points.Average(point => point.Y));
        return points.Select(point => center + (point - center) * ratio).ToArray();
    }

    private sealed record DetectedRegion(Rect Bounds);
}
