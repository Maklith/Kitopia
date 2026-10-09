using System.Text.Json;
using Intel.ML.OnnxRuntime.EP.OpenVINO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.ML.OnnxRuntime;
using PluginCore;

namespace OnnxRuntime.OpenVino;

public class OnnxRuntimeGpuWin : IPlugin, IDisposable
{
    private const string RegistrationName = "kitopia_openvino";
    private readonly Lazy<OrtEnv> _environment = new(() =>
    {
        var environment = OrtEnv.Instance();
        environment.RegisterExecutionProviderLibrary(RegistrationName, OpenVINOEp.GetLibraryPath());
        return environment;
    });
    private bool _disposed;

    static OnnxRuntimeGpuWin() => Shared.RuntimeAvailabilityProbe.ConfigureNativeLibraryResolution();

    internal void AppendExecutionProvider(SessionOptions options, OrtHardwareDeviceType deviceType, int intraOpNumThreads = 0)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var environment = _environment.Value;
        var device = environment.GetEpDevices().FirstOrDefault(device =>
            string.Equals(device.EpName, OpenVINOEp.GetEpName(), StringComparison.OrdinalIgnoreCase) &&
            device.HardwareDevice.Type == deviceType)
            ?? throw new InvalidOperationException($"OpenVINO did not report an available {deviceType} device.");
        var providerOptions = new Dictionary<string, string>();
        if (deviceType == OrtHardwareDeviceType.CPU)
        {
            var configuration = new Dictionary<string, string>
            {
                ["INFERENCE_PRECISION_HINT"] = "f32",
                ["EXECUTION_MODE_HINT"] = "ACCURACY"
            };
            if (intraOpNumThreads > 0)
                configuration["INFERENCE_NUM_THREADS"] = intraOpNumThreads.ToString(System.Globalization.CultureInfo.InvariantCulture);
            providerOptions["load_config"] = JsonSerializer.Serialize(new Dictionary<string, Dictionary<string, string>>
            {
                ["CPU"] = configuration
            });
        }
        options.AppendExecutionProvider(environment, [device], providerOptions);
    }

    public void OnEnabled(IServiceProvider serviceProvider, Dictionary<string, IServiceProvider> dependencyServiceProviders)
    {
    }

    public void OnDisabled()
    {
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // The container disposes transient sessions before releasing this singleton's EP library.
        if (_environment.IsValueCreated)
            _environment.Value.UnregisterExecutionProviderLibrary(RegistrationName);
    }

    public static IServiceProvider GetServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<OnnxRuntimeGpuWin>();
        services.AddTransient<NPUOVInferenceSession>();
        services.AddTransient<GPUOVInferenceSession>();
        services.AddTransient<CPUOVInferenceSession>();
        return services.BuildServiceProvider();
    }
}
