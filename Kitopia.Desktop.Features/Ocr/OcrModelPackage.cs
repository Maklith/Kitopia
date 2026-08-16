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
                Name = "PaddleOCR 文字检测模型",
                Description = "用于检测本地图片和屏幕截图中的文字区域。",
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
                Name = "PaddleOCR 文字识别模型",
                Description = "用于识别本地图片和屏幕截图中的中文、英文等文字。",
                SignName = RecognizerSignName,
                ModelPath = RecognizerPath,
                RequiredFiles = [DetectorPath, DictionaryPath],
                IsBundled = true
            }
        }
    ];
}
