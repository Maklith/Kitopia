using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using PluginCore.CustomScenario.Attribute;
using PluginCore.Onnx;

namespace KitopiaSemanticSearch;

public sealed class KitopiaSemanticSearch : IPlugin
{
    [OnnxModelInfo]
    public OnnxModelInfo TextModel { get; } = new()
    {
        Name = "lang.kitopia.models.embeddinggemma_text",
        Description = "lang.kitopia.embeddinggemma_text_description",
        SignName = "embeddinggemma-2-text-onnx-q4",
        ModelPath = "EmbeddingGemma2/onnx/model_q4.onnx",
        RequiredFiles =
        [
            "EmbeddingGemma2/onnx/model_q4.onnx_data.part001", "EmbeddingGemma2/onnx/model_q4.onnx_data.part002",
            "EmbeddingGemma2/onnx/model_q4.onnx_data.part003", "EmbeddingGemma2/onnx/model_q4.onnx_data.part004",
            "EmbeddingGemma2/onnx/model_q4.onnx_data.part005"
        ],
        IsBundled = true
    };

    [OnnxModelInfo]
    public OnnxModelInfo VisionModel { get; } = new()
    {
        Name = "lang.kitopia.models.embeddinggemma_vision",
        Description = "lang.kitopia.embeddinggemma_vision_description",
        SignName = "embeddinggemma-2-vision-onnx-q4",
        ModelPath = "EmbeddingGemma2/onnx/vision_encoder_q4.onnx",
        RequiredFiles =
        [
            "EmbeddingGemma2/onnx/vision_encoder_q4.onnx_data.part001", "EmbeddingGemma2/onnx/vision_encoder_q4.onnx_data.part002",
            "EmbeddingGemma2/onnx/vision_encoder_q4.onnx_data.part003", "EmbeddingGemma2/onnx/vision_encoder_q4.onnx_data.part004"
        ],
        IsBundled = true
    };

    public void OnEnabled(IServiceProvider serviceProvider, Dictionary<string, IServiceProvider> dependencyServiceProviders) { }

    public void OnDisabled() { }

    public static IServiceProvider GetServiceProvider() =>
        new ServiceCollection().AddSingleton<KitopiaSemanticSearch>().BuildServiceProvider();
}
