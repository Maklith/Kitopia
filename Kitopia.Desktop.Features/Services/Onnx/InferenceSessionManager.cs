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
        if (runtime is null) throw new Exception($"目标推理环境'{target}'不存在");
        var onnxRuntime = runtime.Invoke();
        if (!File.Exists(onnxModelInfoWrapper.Model.ModelPath))
            throw new Exception($"模型'{onnxModelInfoWrapper.Model.Name}'不存在,请先下载");
        onnxRuntime.InitSession(onnxModelInfoWrapper.Model.ModelPath, useCpuMemoryArena);
        return onnxRuntime;
    }
}
