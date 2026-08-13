using Kitopia.Desktop.Features.Utils;
using PluginCore.Onnx;

namespace Kitopia.Desktop.Features.Indexing;

internal static class ChineseClipModelPackage
{
    public const string ImageModelSignName = "chinese-clip-rn50-image-int8";
    public const string TextModelSignName = "chinese-clip-rn50-text-int8";
    public const int ImageVectorDimensions = 1024;
    public const int TextContextLength = 52;

    public static readonly string DirectoryPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ChineseClip");
    public static readonly string ImageModelPath = Path.Combine(DirectoryPath, "chinese-clip-rn50.img.int8.onnx");
    public static readonly string TextModelPath = Path.Combine(DirectoryPath, "chinese-clip-rn50.txt.int8.onnx");
    public static readonly string VocabularyPath = Path.Combine(DirectoryPath, "vocab.txt");

    public static bool IsComplete() =>
        File.Exists(ImageModelPath) && File.Exists(TextModelPath) && File.Exists(VocabularyPath);

    public static IReadOnlyList<OnnxModelInfoWrapper> CreateModelInfos() =>
    [
        new OnnxModelInfoWrapper
        {
            PluginStr = "Kitopia",
            Model = new OnnxModelInfo
            {
                Name = "Chinese-CLIP RN50 图像编码器 INT8",
                Description = "用于图片向量索引；ONNX Runtime CPU，1024 维图像向量。",
                SignName = ImageModelSignName,
                ModelPath = ImageModelPath,
                RequiredFiles = [TextModelPath, VocabularyPath],
                IsBundled = true
            }
        },
        new OnnxModelInfoWrapper
        {
            PluginStr = "Kitopia",
            Model = new OnnxModelInfo
            {
                Name = "Chinese-CLIP RN50 文本编码器 INT8",
                Description = "用于文本检索图片；动态 INT64 token 输入，1024 维文本向量。",
                SignName = TextModelSignName,
                ModelPath = TextModelPath,
                RequiredFiles = [ImageModelPath, VocabularyPath],
                IsBundled = true
            }
        }
    ];
}
