using System.Runtime.InteropServices;
using Intel.ML.OnnxRuntime.EP.OpenVINO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OnnxRuntime.OpenVino;
using PluginCore.Onnx;

namespace KitopiaTest.Services;

[TestClass]
[DoNotParallelize]
public sealed class OpenVinoExecutionProviderTests
{
    // Opset 13: Add(input, constant), with the constant also exported under a '/'-containing name.
    private static readonly byte[] Model = Convert.FromBase64String(
        "CAg6owIKgQESGC9tb2RlbC9jb25zdGFudC9vdXRwdXRfMCIIQ29uc3RhbnQqWwoFdmFsdWUqTwgBCBAQASJAAACAPwAAgD8AAIA/AACAPwAAgD8AAIA/AACAPwAAgD8AAIA/AACAPwAAgD8AAIA/AACAPwAAgD8AAIA/AACAP0IFdmFsdWWgAQQKLgoFaW5wdXQKGC9tb2RlbC9jb25zdGFudC9vdXRwdXRfMBIGb3V0cHV0IgNBZGQSDmNvbnN0YW50LW5hbWVzWhcKBWlucHV0Eg4KDAgBEggKAggBCgIIEGIYCgZvdXRwdXQSDgoMCAESCAoCCAEKAggQYioKGC9tb2RlbC9jb25zdGFudC9vdXRwdXRfMBIOCgwIARIICgIIAQoCCBBCBAoAEA0=");

    [TestInitialize]
    public void Initialize()
    {
        if (RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64 ||
            !(OperatingSystem.IsWindows() || OperatingSystem.IsLinux()))
            Assert.Inconclusive("The official OpenVINO EP package supports Windows/Linux x64.");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Infer_ConstantOutputContainingSlash_MatchesBothNamedOutputs(bool fromBytes)
    {
        var path = Path.Combine(Path.GetTempPath(), $"kitopia-openvino-{Guid.NewGuid():N}.onnx");
        try
        {
            using var runtime = new OnnxRuntimeGpuWin();
            using var session = new CPUOVInferenceSession(runtime);
            Assert.AreEqual(true, session.CheckAvailability());
            if (fromBytes)
                session.InitSession(Model);
            else
            {
                File.WriteAllBytes(path, Model);
                session.InitSession(path, true, 2);
            }
            List<(string, Memory<int>, Memory<float>)> inputs =
                [("input", new int[] { 1, 16 }, Enumerable.Repeat(1f, 16).ToArray())];
            var sum = session.Infer([], inputs, "output", CancellationToken.None);
            var constant = session.Infer([], inputs, "/model/constant/output_0", CancellationToken.None);
            CollectionAssert.AreEqual(Enumerable.Repeat(2f, 16).ToArray(), sum.ToArray());
            CollectionAssert.AreEqual(Enumerable.Repeat(1f, 16).ToArray(), constant.ToArray());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void Dispose_ContainerWithLiveSessions_UnregistersLibraryAndAllowsReload()
    {
        for (var iteration = 0; iteration < 2; iteration++)
        {
            using var services = (ServiceProvider)OnnxRuntimeGpuWin.GetServiceProvider();
            var session = services.GetRequiredService<CPUOVInferenceSession>();
            Assert.AreEqual(true, session.CheckAvailability());
            session.InitSession(Model);
            var output = session.Infer([("input", new int[] { 1, 16 }, new float[16])]);
            CollectionAssert.AreEqual(Enumerable.Repeat(1f, 16).ToArray(), output.ToArray());

            services.Dispose();
            Assert.IsFalse(OrtEnv.Instance().GetEpDevices().Any(device => device.EpName == OpenVINOEp.GetEpName()));
            Assert.ThrowsExactly<ObjectDisposedException>(() => session.CheckAvailability());
        }
    }

    [TestMethod]
    [DataRow(OrtHardwareDeviceType.GPU)]
    [DataRow(OrtHardwareDeviceType.NPU)]
    public async Task CheckAvailability_Accelerators_SelectsReportedDeviceOrRejectsMissingDevice(OrtHardwareDeviceType deviceType)
    {
        using var runtime = new OnnxRuntimeGpuWin();
        using var cpu = new CPUOVInferenceSession(runtime);
        Assert.AreEqual(true, cpu.CheckAvailability());
        var reported = OrtEnv.Instance().GetEpDevices().Any(device =>
            device.EpName == OpenVINOEp.GetEpName() && device.HardwareDevice.Type == deviceType);
        using IInferenceSession session = deviceType == OrtHardwareDeviceType.GPU
            ? new GPUOVInferenceSession(runtime) : new NPUOVInferenceSession(runtime);
        if (reported)
        {
            for (var iteration = 0; iteration < 3; iteration++)
            {
                Assert.AreEqual(true, await Task.Run(cpu.CheckAvailability));
                Assert.AreEqual(true, await Task.Run(session.CheckAvailability));
                session.InitSession(Model);
                List<(string, Memory<int>, Memory<float>)> inputs =
                    [("input", new int[] { 1, 16 }, Enumerable.Repeat(1f, 16).ToArray())];
                var output = await Task.Run(() => session.Infer([], inputs, "output", CancellationToken.None));
                CollectionAssert.AreEqual(Enumerable.Repeat(2f, 16).ToArray(), output.ToArray());
            }
        }
        else
        {
            var exception = Assert.ThrowsExactly<InvalidOperationException>(() => session.CheckAvailability());
            StringAssert.Contains(exception.Message, deviceType.ToString());
        }
    }

    [TestMethod]
    public void Infer_ReportedDevices_InputMemoryHasSessionAllocator()
    {
        using var runtime = new OnnxRuntimeGpuWin();
        using var cpu = new CPUOVInferenceSession(runtime);
        Assert.AreEqual(true, cpu.CheckAvailability());
        Assert.AreEqual("1.30.0", OrtEnv.Instance().GetVersionString());
        foreach (var device in OrtEnv.Instance().GetEpDevices().Where(device => device.EpName == OpenVINOEp.GetEpName()))
        {
            using var options = new SessionOptions
            {
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_DISABLE_ALL,
                IntraOpNumThreads = 1, InterOpNumThreads = 1
            };
            options.AddSessionConfigEntry("session.disable_cpu_ep_fallback", "1");
            options.AppendExecutionProvider(OrtEnv.Instance(), [device], new Dictionary<string, string>());
            using var session = new InferenceSession(Model, options);
            using var inputMemory = session.GetMemoryInfosForInputs();
            foreach (var memory in inputMemory)
            {
                using var allocator = new OrtAllocator(session, memory);
                using var allocation = allocator.Allocate(64);
                Assert.IsFalse(allocation.IsInvalid);
            }
            using var output = session.Run([
                NamedOnnxValue.CreateFromTensor("input",
                    new DenseTensor<float>(new float[16], [1, 16]))
            ], ["output"]);
            CollectionAssert.AreEqual(Enumerable.Repeat(1f, 16).ToArray(), output[0].AsTensor<float>().ToArray());
        }
    }
}
