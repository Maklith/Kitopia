using System.Text;
using System.Buffers;
using Kitopia.Desktop.Features.Imaging;
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
    private const int MaximumDetectedRegions = 1024;
    private const double DetectionPixelThreshold = 0.2d;
    private const double DetectionBoxThreshold = 0.4d;
    private const float DetectionUnclipRatio = 1.5f;
    private const float MaximumDetectionPaddingHeightRatio = 1f;
    // The recognition model accepts a dynamic width. Without a bound, a very wide
    // detection box creates an unbounded resize and output tensor in native memory.
    private const int MaximumRecognitionWidth = 3200;
    private const int RecognitionHeight = 48;
    private const int RecognitionWidthMultiple = 32;
    private readonly IInferenceSessionManager _sessions;
    private readonly SemaphoreSlim _inferenceGate = new(1, 1);
    private readonly string[] _alphabet;
    private IInferenceSession? _detector;
    private IInferenceSession? _recognizer;
    private string? _detectorInputName;
    private string? _recognizerInputName;
    private int _recognizerOutputDimensions;

    public PaddleOcrService(IInferenceSessionManager sessions)
    {
        _sessions = sessions;
        _alphabet = OcrModelPackage.IsComplete()
            ? File.ReadAllLines(OcrModelPackage.DictionaryPath)
                // The Paddle dictionary represents the ASCII space as an empty
                // line. Keep that token instead of dropping it during decoding.
                .Select(character => character.Length == 0 ? " " : character)
                .ToArray()
            : [];
    }

    public bool IsAvailable => OcrModelPackage.IsComplete();

    public async Task<IReadOnlyList<OcrTextRegion>> RecognizeFileAsync(
        string imagePath,
        CancellationToken cancellationToken = default)
    {
        if (!IsAvailable) return [];
        using var image = ImageInputLoader.LoadBgr(imagePath, ImageInputLoader.MaximumOcrPixels);
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
            var detectorInputName = _detectorInputName ?? throw new InvalidOperationException("The OCR detector input metadata is unavailable.");
            var recognizerInputName = _recognizerInputName ?? throw new InvalidOperationException("The OCR recognizer input metadata is unavailable.");
            using var resized = ImageInputLoader.ResizeToMaximumPixels(image, ImageInputLoader.MaximumOcrPixels);
            var input = resized ?? image;
            var scaleX = image.Cols / (double)input.Cols;
            var scaleY = image.Rows / (double)input.Rows;
            return await Task.Run(() => RecognizeCore(input, detector, recognizer,
                detectorInputName, recognizerInputName, _recognizerOutputDimensions,
                scaleX, scaleY, image.Cols, image.Rows, cancellationToken), cancellationToken);
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
            _detectorInputName = null;
            _recognizerInputName = null;
            _recognizerOutputDimensions = 0;
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
        _detector ??= _sessions.GetSession(OcrModelPackage.DetectorSignName, useCpuMemoryArena: false)
                      ?? throw new InvalidOperationException("The OCR detector runtime is unavailable.");
        _recognizer ??= _sessions.GetSession(OcrModelPackage.RecognizerSignName, useCpuMemoryArena: false)
                        ?? throw new InvalidOperationException("The OCR recognizer runtime is unavailable.");
        _detectorInputName ??= _detector.InputNames.FirstOrDefault()
                              ?? throw new InvalidOperationException("The OCR detector input metadata is unavailable.");
        _recognizerInputName ??= _recognizer.InputNames.FirstOrDefault()
                                ?? throw new InvalidOperationException("The OCR recognizer input metadata is unavailable.");
        if (_recognizerOutputDimensions == 0)
        {
            _recognizerOutputDimensions = _recognizer.OutputShape.FirstOrDefault()?.ElementAtOrDefault(2) ?? 0;
            if (_recognizerOutputDimensions == 0)
            {
                throw new InvalidOperationException("The OCR recognizer output metadata is unavailable.");
            }
        }
    }

    private IReadOnlyList<OcrTextRegion> RecognizeCore(
        Mat image,
        IInferenceSession detector,
        IInferenceSession recognizer,
        string detectorInputName,
        string recognizerInputName,
        int recognizerOutputDimensions,
        double scaleX,
        double scaleY,
        int outputWidth,
        int outputHeight,
        CancellationToken cancellationToken)
    {
        var detectorImage = PrepareDetectionImage(image);
        try
        {
            var regions = Detect(detector, detectorImage, detectorInputName, cancellationToken);
            var results = new List<OcrTextRegion>(regions.Count);

            foreach (var region in regions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var crop = new Mat(detectorImage, region.Bounds);
                var text = Recognize(recognizer, recognizerInputName, recognizerOutputDimensions, crop);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    var bounds = ScaleBounds(region.Bounds, image.Cols, image.Rows,
                        scaleX, scaleY, outputWidth, outputHeight);
                    results.Add(new OcrTextRegion(text, bounds.Left, bounds.Top,
                        bounds.Width, bounds.Height));
                }
            }

            // FindContours does not guarantee reading order (OpenCV commonly returns
            // contours from the bottom of the image upwards). Return OCR regions in
            // the order users read them so indexing and the result window are stable.
            return results
                .OrderBy(region => region.Top)
                .ThenBy(region => region.Left)
                .ToArray();
        }
        finally
        {
            if (!ReferenceEquals(detectorImage, image))
            {
                detectorImage.Dispose();
            }
        }
    }

    private static List<DetectedRegion> Detect(
        IInferenceSession session,
        Mat input,
        string inputName,
        CancellationToken cancellationToken)
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
                (inputName, new Memory<int>([1, 3, input.Rows, input.Cols]),
                    tensor.AsMemory(0, elementCount))
            ]);

            unsafe
            {
                using var outputHandle = output.Pin();
                using var binary = Mat.FromPixelData(input.Rows, input.Cols, MatType.CV_32FC1,
                    (IntPtr)outputHandle.Pointer);
                using var threshold = new Mat();
                binary.ConvertTo(threshold, MatType.CV_8UC1, 255d);
                Cv2.Threshold(threshold, threshold, DetectionPixelThreshold * 255d, 255,
                    ThresholdTypes.Binary);
                Cv2.FindContours(threshold, out Point[][] contours, out _, RetrievalModes.External,
                    ContourApproximationModes.ApproxTC89L1);

                var regions = new List<DetectedRegion>(contours.Length);
                foreach (var contour in contours)
                {
                    if (regions.Count == MaximumDetectedRegions) break;
                    cancellationToken.ThrowIfCancellationRequested();
                    var contourBounds = Clamp(Cv2.BoundingRect(contour), input.Cols, input.Rows);
                    // Mat.Zeros returns a lazy MatExpr; materialize a writable mask for FillPoly.
                    using var contourMask = new Mat(contourBounds.Height, contourBounds.Width, MatType.CV_8UC1,
                        Scalar.Black);
                    Cv2.FillPoly(
                        contourMask,
                        [contour],
                        Scalar.White,
                        offset: new Point(-contourBounds.X, -contourBounds.Y));
                    using var contourScores = new Mat(binary, contourBounds);
                    if (Cv2.Mean(contourScores, contourMask).Val0 < DetectionBoxThreshold) continue;
                    var box = Cv2.MinAreaRect(contour);
                    if (Math.Min(box.Size.Width, box.Size.Height) < 3f) continue;
                    // DB's unclip operation expands a polygon by area * (ratio - 1) / perimeter.
                    // The height-derived cap keeps long code lines from expanding in
                    // proportion to their full width.
                    var padding = Math.Min(box.Size.Height * MaximumDetectionPaddingHeightRatio,
                        box.Size.Width * box.Size.Height * (DetectionUnclipRatio - 1f) /
                        (2f * (box.Size.Width + box.Size.Height)));
                    var expandedBox = new RotatedRect(box.Center,
                        new Size2f(box.Size.Width + 2f * padding,
                            box.Size.Height + 2f * padding), box.Angle);
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

    private string Recognize(IInferenceSession session, string inputName, int dimensions, Mat source)
    {
        using var input = PrepareRecognitionImage(source);
        using var normalized = new Mat();
        input.ConvertTo(normalized, MatType.CV_32FC3, 1d / 255d);
        Cv2.Subtract(normalized, new Scalar(0.5, 0.5, 0.5), normalized);
        Cv2.Divide(normalized, new Scalar(0.5, 0.5, 0.5), normalized);
        var elementCount = 3 * input.Rows * input.Cols;
        var tensor = ArrayPool<float>.Shared.Rent(elementCount);
        try
        {
            OnnxInputDataTool.InputTensor(normalized, tensor.AsMemory(0, elementCount));
            var output = session.Infer(
            [
                (inputName, new Memory<int>([1, 3, input.Rows, input.Cols]),
                    tensor.AsMemory(0, elementCount))
            ]);
            if (dimensions <= 0 || output.Length % dimensions != 0
                || output.Length > MaximumRecognitionWidth * dimensions)
            {
                throw new InvalidDataException(
                    $"OCR recognizer returned an invalid output length ({output.Length}) for width {input.Cols}.");
            }

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
        if (source.Channels() == 3)
        {
            return source.Rows % 32 == 0 && source.Cols % 32 == 0
                ? source
                : PadToMultiple(source, 32, 32);
        }

        var bgr = ConvertToBgr(source);
        if (bgr.Rows % 32 == 0 && bgr.Cols % 32 == 0)
        {
            return bgr;
        }

        try
        {
            return PadToMultiple(bgr, 32, 32);
        }
        finally
        {
            bgr.Dispose();
        }
    }

    internal static Mat PrepareRecognitionImage(Mat source)
    {
        if (source.Channels() == 3) return ResizeRecognitionImage(source);

        using var bgr = ConvertToBgr(source);
        return ResizeRecognitionImage(bgr);
    }

    private static Mat ResizeRecognitionImage(Mat source)
    {
        if (source.Rows <= 0 || source.Cols <= 0)
        {
            throw new InvalidDataException("OCR recognition input is empty.");
        }

        // Resize directly instead of padding to the source width first. A one-pixel-high
        // wide box would otherwise allocate a multi-million-column Mat before resizing.
        var proportionalWidth = Math.Max(
            RecognitionWidthMultiple,
            (int)Math.Ceiling(source.Cols * (double)RecognitionHeight / source.Rows));
        var resizedWidth = Math.Min(
            MaximumRecognitionWidth,
            RoundUp(proportionalWidth, RecognitionWidthMultiple));
        var result = new Mat();
        Cv2.Resize(source, result, new Size(resizedWidth, RecognitionHeight));
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

    internal static Rect ScaleBounds(
        Rect bounds,
        int inputWidth,
        int inputHeight,
        double scaleX,
        double scaleY,
        int outputWidth,
        int outputHeight)
    {
        var leftInput = Math.Clamp(bounds.Left, 0, inputWidth - 1);
        var topInput = Math.Clamp(bounds.Top, 0, inputHeight - 1);
        var left = Math.Clamp((int)Math.Floor(leftInput * scaleX), 0, outputWidth - 1);
        var top = Math.Clamp((int)Math.Floor(topInput * scaleY), 0, outputHeight - 1);
        var rightInput = Math.Clamp(bounds.Right, 0, inputWidth);
        var bottomInput = Math.Clamp(bounds.Bottom, 0, inputHeight);
        var right = Math.Clamp((int)Math.Ceiling(rightInput * scaleX), left + 1, outputWidth);
        var bottom = Math.Clamp((int)Math.Ceiling(bottomInput * scaleY), top + 1, outputHeight);
        return new Rect(left, top, right - left, bottom - top);
    }

    private static int RoundUp(int value, int multiple) => ((value + multiple - 1) / multiple) * multiple;

    private sealed record DetectedRegion(Rect Bounds);
}
