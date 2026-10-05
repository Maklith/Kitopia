using Kitopia.Feature.Localization;
using Kitopia.Desktop.Features.Services.Config;
using Kitopia.Desktop.Features.Services.Plugin;
using PluginCore.Onnx;

namespace Kitopia.Desktop.Features.Services.Onnx;

public class InferenceSessionManager : IInferenceSessionManager
{
    public IInferenceSession GetSession(string modelSignName) => GetSession(modelSignName, useCpuMemoryArena: false);

    public IInferenceSession GetSession(string modelSignName, bool useCpuMemoryArena)
    {
        var onnxModelInfoWrapper = PluginOverall.OnnxModelInfos.SelectMany(e => e.Value)
            .FirstOrDefault(e => e.Model.SignName == modelSignName);
        if (onnxModelInfoWrapper is null)
            return null;
        var target = ConfigManger.Config.OnnxTargetDevices.ContainsKey(onnxModelInfoWrapper.Model.SignName)
            ? ConfigManger.Config.OnnxTargetDevices[onnxModelInfoWrapper.Model.SignName]
            : "CPU";
        var runtime = PluginOverall.GetOnnxRuntime(target);
        if (runtime is null) throw new Exception(Lang.Format("lang.kitopia.messages.inference_environment_value_is_unavailable", target));
        var onnxRuntime = runtime.Invoke();
        if (!File.Exists(onnxModelInfoWrapper.Model.ModelPath))
            throw new Exception(Lang.Format("lang.kitopia.messages.model_value_is_missing_download_it_first", onnxModelInfoWrapper.Model.Name));
        onnxRuntime.InitSession(onnxModelInfoWrapper.Model.ModelPath, useCpuMemoryArena);
        return onnxRuntime;
    }
}
