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
                Name = "lang.kitopia.models.chinese_clip_image",
                Description = "lang.kitopia.image_indexing_with_onnx_runtime_cpu_and_1024_dimensional_image_embeddings",
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
                Name = "lang.kitopia.models.chinese_clip_text",
                Description = "lang.kitopia.text_to_image_search_with_dynamic_int64_tokens_and_1024_dimensional_text_embeddings",
                SignName = TextModelSignName,
                ModelPath = TextModelPath,
                RequiredFiles = [ImageModelPath, VocabularyPath],
                IsBundled = true
            }
        }
    ];
}
