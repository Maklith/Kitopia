using Kitopia.Desktop.Features.Utils;
using PluginCore.Onnx;

namespace Kitopia.Desktop.Features.Search.Semantic;

internal static class BgeModelPackage
{
    public const string ModelSignName = "bge-small-zh-v1.5-onnx-int8";

    public static readonly string DirectoryPath = Path.Combine(KitopiaPaths.AppRoot, "BGE_Model");
    public static readonly string ModelPath = Path.Combine(DirectoryPath, "quantized", "model_quantized.onnx");
    public static readonly string ModelDataPath = ModelPath + "_data";
    public static readonly string TokenizerPath = Path.Combine(DirectoryPath, "tokenizer.json");

    internal static bool IsComplete()
    {
        return IsComplete(DirectoryPath);
    }

    internal static bool IsComplete(string directoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);

        var modelPath = Path.Combine(directoryPath, "quantized", "model_quantized.onnx");
        return File.Exists(modelPath)
               && File.Exists(modelPath + "_data")
               && File.Exists(Path.Combine(directoryPath, "tokenizer.json"));
    }

    internal static OnnxModelInfo CreateModelInfo()
    {
        return new OnnxModelInfo
        {
            Name = "lang.kitopia.models.bge_semantic_search",
            Description = "lang.kitopia.understands_semantic_relationships_between_search_terms_and_content",
            SignName = ModelSignName,
            ModelPath = ModelPath,
            RequiredFiles = [ModelDataPath, TokenizerPath],
            IsBundled = true
        };
    }
}
