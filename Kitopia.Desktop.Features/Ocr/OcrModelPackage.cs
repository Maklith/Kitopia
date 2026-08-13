using Kitopia.Desktop.Features.Utils;
using PluginCore.Onnx;

namespace Kitopia.Desktop.Features.Ocr;

internal static class OcrModelPackage
{
    public const string DetectorSignName = "paddleocr-det-v4";
    public const string RecognizerSignName = "paddleocr-rec-v4";

    public static readonly string DirectoryPath = Path.Combine(KitopiaPaths.AppRoot, "Ocr");
    public static readonly string DetectorPath = Path.Combine(DirectoryPath, "ocr_det.onnx");
    public static readonly string RecognizerPath = Path.Combine(DirectoryPath, "ocr_rec.onnx");
    public static readonly string DictionaryPath = Path.Combine(DirectoryPath, "rec_word_dict.txt");

    public static bool IsComplete() => File.Exists(DetectorPath)
                                       && File.Exists(RecognizerPath)
                                       && File.Exists(DictionaryPath);

    public static IReadOnlyList<OnnxModelInfoWrapper> CreateModelInfos() =>
    [
        new OnnxModelInfoWrapper
        {
            PluginStr = "Kitopia",
            Model = new OnnxModelInfo
            {
                Name = "PaddleOCR text detector",
                Description = "Detects text regions for local image and screen OCR.",
                SignName = DetectorSignName,
                ModelPath = DetectorPath,
                RequiredFiles = [RecognizerPath, DictionaryPath],
                IsBundled = true
            }
        },
        new OnnxModelInfoWrapper
        {
            PluginStr = "Kitopia",
            Model = new OnnxModelInfo
            {
                Name = "PaddleOCR text recognizer",
                Description = "Recognizes Chinese and Latin text for local image and screen OCR.",
                SignName = RecognizerSignName,
                ModelPath = RecognizerPath,
                RequiredFiles = [DetectorPath, DictionaryPath],
                IsBundled = true
            }
        }
    ];
}
