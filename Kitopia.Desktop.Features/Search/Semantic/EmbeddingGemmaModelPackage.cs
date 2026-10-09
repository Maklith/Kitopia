using Kitopia.Desktop.Features.Utils;
using PluginCore.Onnx;

namespace Kitopia.Desktop.Features.Search.Semantic;

internal static class EmbeddingGemmaModelPackage
{
    public const string Revision = "daa72c51243991dfcaf9f9137d2c573d8f7790c0";
    public const string TextModelSignName = "embeddinggemma-2-text-onnx-q4";
    public const string VisionModelSignName = "embeddinggemma-2-vision-onnx-q4";
    public const string DisplayName = "EmbeddingGemma 2 Q4";
    public static readonly string[] TextModelFiles =
    [
        "onnx/model_q4.onnx", "onnx/model_q4.onnx_data.part001", "onnx/model_q4.onnx_data.part002",
        "onnx/model_q4.onnx_data.part003", "onnx/model_q4.onnx_data.part004", "onnx/model_q4.onnx_data.part005"
    ];
    public static readonly string[] VisionModelFiles =
    [
        "onnx/vision_encoder_q4.onnx", "onnx/vision_encoder_q4.onnx_data.part001",
        "onnx/vision_encoder_q4.onnx_data.part002", "onnx/vision_encoder_q4.onnx_data.part003",
        "onnx/vision_encoder_q4.onnx_data.part004"
    ];
    public static readonly string[] RequiredFiles =
    [
        ..TextModelFiles,
        ..VisionModelFiles,
        "tokenizer.json", "tokenizer_config.json", "processor_config.json", "config.json"
    ];
    public static readonly string DirectoryPath = ResolveDirectory(
        Path.Combine(KitopiaPaths.AppRoot, "EmbeddingGemma2"),
        Path.Combine(AppContext.BaseDirectory, "EmbeddingGemma2"));
    public static readonly string TextModelPath = Path.Combine(DirectoryPath, "onnx", "model_q4.onnx");
    public static readonly string VisionModelPath = Path.Combine(DirectoryPath, "onnx", "vision_encoder_q4.onnx");

    internal static string ResolveDirectory(string installedDirectory, string bundledDirectory) =>
        IsComplete(installedDirectory) || !IsComplete(bundledDirectory) ? installedDirectory : bundledDirectory;

    public static bool IsComplete(string? directoryPath = null) =>
        RequiredFiles.All(file => File.Exists(Path.Combine(directoryPath ?? DirectoryPath, file)));

    public static IReadOnlyList<OnnxModelInfoWrapper> CreateModelInfos() =>
    [
        new()
        {
            PluginStr = "Kitopia",
            Model = new OnnxModelInfo
            {
                Name = "lang.kitopia.models.embeddinggemma_text",
                Description = "lang.kitopia.embeddinggemma_text_description",
                SignName = TextModelSignName,
                ModelPath = TextModelPath,
                RequiredFiles = TextModelFiles.Select(file => Path.Combine(DirectoryPath, file)).ToArray(),
                IsBundled = true
            }
        },
        new()
        {
            PluginStr = "Kitopia",
            Model = new OnnxModelInfo
            {
                Name = "lang.kitopia.models.embeddinggemma_vision",
                Description = "lang.kitopia.embeddinggemma_vision_description",
                SignName = VisionModelSignName,
                ModelPath = VisionModelPath,
                RequiredFiles = VisionModelFiles.Select(file => Path.Combine(DirectoryPath, file)).ToArray(),
                IsBundled = true
            }
        }
    ];
}
