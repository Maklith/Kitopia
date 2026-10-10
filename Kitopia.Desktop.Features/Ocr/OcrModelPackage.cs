using Kitopia.Desktop.Features.Utils;
using PluginCore.Onnx;

namespace Kitopia.Desktop.Features.Ocr;

internal static class OcrModelPackage
{
    public const string DetectorSignName = "paddleocr-v6-small-det";
    public const string RecognizerSignName = "paddleocr-v6-small-rec";

    public static readonly string DirectoryPath = Path.Combine(KitopiaPaths.AppRoot, "Ocr");
    public static readonly string DetectorPath = Path.Combine(DirectoryPath, "ppocrv6_small_det.onnx");
    public static readonly string RecognizerPath = Path.Combine(DirectoryPath, "ppocrv6_small_rec.onnx");
    public static readonly string DictionaryPath = Path.Combine(DirectoryPath, "ppocrv6_small_rec_dict.txt");

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
                Name = "lang.kitopia.models.ocr_detector",
                Description = "lang.kitopia.pp_ocrv6_tiny_detects_text_regions_in_local_images_and_screen_captures",
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
                Name = "lang.kitopia.models.ocr_recognizer",
                Description = "lang.kitopia.pp_ocrv6_tiny_recognizes_chinese_english_and_other_text_in_images_and_captures",
                SignName = RecognizerSignName,
                ModelPath = RecognizerPath,
                RequiredFiles = [DetectorPath, DictionaryPath],
                IsBundled = true
            }
        }
    ];
}
