using Kitopia.Desktop.Features.Utils;
using Kitopia.Desktop.Features.Services.Plugin;

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
    public static string? DirectoryPath => PluginOverall.OnnxModelInfos.Values.SelectMany(models => models)
        .FirstOrDefault(model => model.Model.SignName == TextModelSignName) is { } model
        ? Path.GetDirectoryName(Path.GetDirectoryName(model.Model.ModelPath)!)!
        : null;

    public static bool IsComplete(string? directoryPath = null) =>
        (directoryPath ?? DirectoryPath) is { } directory &&
        RequiredFiles.All(file => File.Exists(Path.Combine(directory, file)));

}
