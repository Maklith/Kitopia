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
        if (!File.Exists(onnxModelInfoWrapper.Model.ModelPath))
            throw new Exception(Lang.Format("lang.kitopia.messages.model_value_is_missing_download_it_first", onnxModelInfoWrapper.Model.Name));
        var onnxRuntime = runtime.Invoke();
        try
        {
            var threads = Math.Max(1, Environment.ProcessorCount *
                Math.Clamp(ConfigManger.Config.indexingMaximumCpuUsagePercent, 1, 100) / 100);
            var gpuMemoryLimit = (long)Math.Clamp(ConfigManger.Config.inferenceMaximumGpuMemoryMiB, 128, 32768) * 1024 * 1024;
            onnxRuntime.InitSession(onnxModelInfoWrapper.Model.ModelPath, useCpuMemoryArena, threads, gpuMemoryLimit);
            return onnxRuntime;
        }
        catch
        {
            onnxRuntime.Dispose();
            throw;
        }
    }
}
