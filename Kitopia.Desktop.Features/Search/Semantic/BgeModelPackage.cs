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
            Name = "中文语义搜索模型（BGE）",
            Description = "用于理解搜索词与内容的语义关联",
            SignName = ModelSignName,
            ModelPath = ModelPath,
            RequiredFiles = [ModelDataPath, TokenizerPath],
            IsBundled = true
        };
    }
}
