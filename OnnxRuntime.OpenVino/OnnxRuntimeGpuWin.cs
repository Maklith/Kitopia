using System.Text.Json;
using System.Runtime.InteropServices;
using Intel.ML.OnnxRuntime.EP.OpenVINO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.ML.OnnxRuntime;
using PluginCore;

namespace OnnxRuntime.OpenVino;

public class OnnxRuntimeGpuWin : IPlugin, IDisposable
{
    private const string RegistrationName = "kitopia_openvino";
    private readonly Lazy<OrtEnv> _environment;
    private IntPtr _levelZeroLibrary;
    private bool _disposed;

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int ZeInit(uint flags);

    public OnnxRuntimeGpuWin()
    {
        _environment = new Lazy<OrtEnv>(() =>
        {
            // Level Zero teardown invalidates driver state between EP enumeration and session creation.
            if (OperatingSystem.IsWindows() &&
                NativeLibrary.TryLoad(Path.Combine(Environment.SystemDirectory, "ze_loader.dll"), out _levelZeroLibrary))
            {
                // Initialize all driver types before ORT's hardware discovery initializes Level Zero.
                Marshal.GetDelegateForFunctionPointer<ZeInit>(NativeLibrary.GetExport(_levelZeroLibrary, "zeInit"))(0);
            }
            var environment = OrtEnv.Instance();
            environment.RegisterExecutionProviderLibrary(RegistrationName, OpenVINOEp.GetLibraryPath());
            return environment;
        });
    }

    static OnnxRuntimeGpuWin() => Shared.RuntimeAvailabilityProbe.ConfigureNativeLibraryResolution();

    internal void AppendExecutionProvider(SessionOptions options, OrtHardwareDeviceType deviceType, int intraOpNumThreads = 0,
        bool allowCpuFallback = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var environment = _environment.Value;
        var device = environment.GetEpDevices().FirstOrDefault(device =>
            string.Equals(device.EpName, OpenVINOEp.GetEpName(), StringComparison.OrdinalIgnoreCase) &&
            device.HardwareDevice.Type == deviceType)
            ?? throw new InvalidOperationException($"OpenVINO did not report an available {deviceType} device.");
        var providerOptions = new Dictionary<string, string>();
        var configuration = new Dictionary<string, string>();
        if (deviceType == OrtHardwareDeviceType.CPU)
        {
            configuration["INFERENCE_PRECISION_HINT"] = "f32";
            configuration["EXECUTION_MODE_HINT"] = "ACCURACY";
            if (intraOpNumThreads > 0)
                configuration["INFERENCE_NUM_THREADS"] = intraOpNumThreads.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        else if (deviceType == OrtHardwareDeviceType.NPU)
        {
            // Keep compilation aligned with the OpenVINO version bundled with this provider.
            configuration["NPU_COMPILER_TYPE"] = "PLUGIN";
            configuration["CACHE_DIR"] = Path.Combine(Path.GetTempPath(), "Kitopia", "openvino-npu-cache");
            // ORT profiling otherwise changes this compilation property and invalidates the cache.
            configuration["PERF_COUNT"] = "YES";
            // This also disables OpenVINO EP's internal CPU retry on compilation failure.
            if (!allowCpuFallback)
                options.AddSessionConfigEntry("session.disable_cpu_ep_fallback", "1");
        }
        if (configuration.Count > 0)
        {
            providerOptions["load_config"] = JsonSerializer.Serialize(new Dictionary<string, Dictionary<string, string>>
            {
                [deviceType.ToString()] = configuration
            });
        }
        // Plugin registration creates the device's shared allocator in OrtEnv.
        options.AddSessionConfigEntry("session.use_env_allocators", "1");
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
        try
        {
            // The container disposes transient sessions before releasing this singleton's EP library.
            if (_environment.IsValueCreated)
                _environment.Value.UnregisterExecutionProviderLibrary(RegistrationName);
        }
        finally
        {
            if (_levelZeroLibrary != IntPtr.Zero)
            {
                NativeLibrary.Free(_levelZeroLibrary);
                _levelZeroLibrary = IntPtr.Zero;
            }
        }
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
