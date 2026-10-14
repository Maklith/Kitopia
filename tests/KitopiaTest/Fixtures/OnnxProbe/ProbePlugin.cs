using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using PluginCore.Onnx;

namespace OnnxProbeFixture;

public sealed class ProbePlugin : IPlugin
{
    public static IServiceProvider GetServiceProvider() => new ServiceCollection()
        .AddSingleton<ProbePlugin>().AddTransient<LegacySession>().AddTransient<ProbeSession>().BuildServiceProvider();
    public void OnEnabled(IServiceProvider provider, Dictionary<string, IServiceProvider> dependencies)
    {
        if (Environment.GetCommandLineArgs().Contains("--onnx-runtime-probe"))
            throw new InvalidOperationException("Environment probes must not enable unrelated plugin features.");
    }
    public void OnDisabled() { }
}

public class LegacySession : IInferenceSession
{
    public virtual string Device => "legacy";
    public IReadOnlyList<string> InputNames => [];
    public IReadOnlyList<int[]> OutputShape => [];
    public void InitSession(string modelPath) => throw new NotSupportedException();
    public void InitSession(byte[] modelData) => throw new NotSupportedException();
    public Memory<float> Infer(List<(string, Memory<int>, Memory<float>)> inputs) => throw new NotSupportedException();
    public void Dispose() { }
}

public sealed class ProbeSession : LegacySession, IInferenceSession
{
    public override string Device => Environment.GetCommandLineArgs().Contains("--onnx-runtime-probe")
        ? Environment.GetCommandLineArgs()[3] : "cache";
    public bool? CheckAvailability()
    {
        var directory = Path.GetDirectoryName(Environment.GetCommandLineArgs()[2])!;
        File.WriteAllText(Path.Combine(directory, Device + ".pid"), Environment.ProcessId.ToString());
        File.AppendAllLines(Path.Combine(directory, Device + ".runs"), [Environment.ProcessId.ToString()]);
        Console.WriteLine("Native stdout is independent of the probe result.");
        Console.Error.WriteLine("Native stderr is independent of the probe result.");
        var outcome = Device == "cache" ? File.ReadAllText(Path.Combine(directory, "outcome.txt")) : Device;
        if (outcome == "block") Thread.Sleep(Timeout.Infinite);
        if (outcome == "crash") Environment.FailFast("Fixture native-runtime crash.");
        if (outcome == "throw") throw new DllNotFoundException("fixture-native-library.dll is missing");
        return outcome != "unavailable";
    }
}
