using Core.SDKs.Services.Config;
using Core.SDKs.Services.Plugin;
using PluginCore.Onnx;

namespace Core.SDKs.Services.Onnx;

public class InferenceSessionManager: IInferenceSessionManager
{
    public IInferenceSession GetSession(string modelSignName)
    {
        var onnxModelInfoWrapper = PluginOverall.OnnxModelInfos.SelectMany(e=>e.Value).FirstOrDefault(e=>e.Model.SignName==modelSignName);
        if (onnxModelInfoWrapper is null)
            return null;
        var target= ConfigManger.Config.OnnxTargetDevices.ContainsKey(onnxModelInfoWrapper.Model.SignName)
            ? ConfigManger.Config.OnnxTargetDevices[onnxModelInfoWrapper.Model.SignName]
            : "CPU";
        if (!PluginOverall.OnnxRuntimes.ContainsKey(target))
        {
            throw new Exception($"目标推理环境'{target}'不存在");
        }
        var onnxRuntime = PluginOverall.OnnxRuntimes[target].Invoke();
        if (!File.Exists(onnxModelInfoWrapper.Model.ModelPath))
        {
            throw new Exception($"模型'{onnxModelInfoWrapper.Model.Name}'不存在,请先下载");
        }
        onnxRuntime.InitSession(onnxModelInfoWrapper.Model.ModelPath);
        return onnxRuntime;
    }
}