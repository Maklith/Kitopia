using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
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
    public void CheckAvailability_WindowsLevelZeroLoader_RetainsReferenceUntilPluginDisposal()
    {
        if (!OperatingSystem.IsWindows() || !File.Exists(Path.Combine(Environment.SystemDirectory, "ze_loader.dll")))
            Assert.Inconclusive("A system Level Zero loader is required for this lifetime test.");
        using var runtime = new OnnxRuntimeGpuWin();
        var loaderField = typeof(OnnxRuntimeGpuWin).GetField("_levelZeroLibrary", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.AreEqual(IntPtr.Zero, (IntPtr)loaderField.GetValue(runtime)!);
        using (var cpu = new CPUOVInferenceSession(runtime))
        {
            for (var iteration = 0; iteration < 3; iteration++)
            {
                Assert.AreEqual(true, cpu.CheckAvailability());
                var loader = (IntPtr)loaderField.GetValue(runtime)!;
                Assert.AreNotEqual(IntPtr.Zero, loader);
                Assert.IsTrue(NativeLibrary.TryGetExport(loader, "zeInit", out _));
            }
        }
        runtime.Dispose();
        Assert.AreEqual(IntPtr.Zero, (IntPtr)loaderField.GetValue(runtime)!);
        runtime.Dispose();
    }

    [TestMethod]
    public async Task CheckAvailability_NpuBeforeCpu_RepeatedlyRunsOnWorkerThread()
    {
        using var runtime = new OnnxRuntimeGpuWin();
        using var session = new NPUOVInferenceSession(runtime);
        try
        {
            Assert.AreEqual(true, await Task.Run(session.CheckAvailability));
        }
        catch (InvalidOperationException exception) when (
            exception.Message == "OpenVINO did not report an available NPU device.")
        {
            Assert.Inconclusive("An Intel NPU is required for this initialization test.");
        }

        for (var iteration = 0; iteration < 3; iteration++)
        {
            Assert.AreEqual(true, await Task.Run(session.CheckAvailability));
            session.InitSession(Model);
            var output = await Task.Run(() => session.Infer([
                ("input", new int[] { 1, 16 }, Enumerable.Repeat(1f, 16).ToArray())
            ]));
            CollectionAssert.AreEqual(Enumerable.Repeat(2f, 16).ToArray(), output.ToArray());
        }
    }

    [TestMethod]
    [TestCategory("NPU")]
    public void Infer_NpuReloadAcrossProfilingAndCpuThreadCounts_ReusesCompiledCache()
    {
        using var runtime = new OnnxRuntimeGpuWin();
        using (var probe = new NPUOVInferenceSession(runtime))
        {
            try { Assert.AreEqual(true, probe.CheckAvailability()); }
            catch (InvalidOperationException exception) when (
                exception.Message == "OpenVINO did not report an available NPU device.")
            {
                Assert.Inconclusive("An Intel NPU is required for this cache test.");
            }
        }
        var path = Path.Combine(Path.GetTempPath(), $"kitopia-npu-cache-{Guid.NewGuid():N}.onnx");
        try
        {
            File.WriteAllBytes(path, Model);
            using (var options = new SessionOptions
                   {
                       GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_BASIC,
                       IntraOpNumThreads = 1, InterOpNumThreads = 1, EnableProfiling = true,
                       EnableMemoryPattern = false
                   })
            {
                typeof(OnnxRuntimeGpuWin).GetMethod("AppendExecutionProvider", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(runtime, [options, OrtHardwareDeviceType.NPU, 0, true]);
                using var session = new InferenceSession(path, options);
                using var output = session.Run([
                    NamedOnnxValue.CreateFromTensor("input", new DenseTensor<float>(new float[16], [1, 16]))
                ], ["output"]);
                CollectionAssert.AreEqual(Enumerable.Repeat(1f, 16).ToArray(), output[0].AsTensor<float>().ToArray());
                File.Delete(session.EndProfiling());
            }
            var cacheDirectory = Path.Combine(Path.GetTempPath(), "Kitopia", "openvino-npu-cache");
            var cached = Directory.GetFiles(cacheDirectory, "*.blob").ToDictionary(file => file, File.GetLastWriteTimeUtc);
            Assert.IsNotEmpty(cached);
            foreach (var threads in new[] { 1, 8 })
            {
                using var session = new NPUOVInferenceSession(runtime);
                session.InitSession(path, true, threads, 0, new Dictionary<string, long> { ["batch_size"] = 1 });
                var output = session.Infer([("input", new int[] { 1, 16 }, new float[16])]);
                CollectionAssert.AreEqual(Enumerable.Repeat(1f, 16).ToArray(), output.ToArray());
                var changed = Directory.GetFiles(cacheDirectory, "*.blob").Where(file =>
                    !cached.TryGetValue(file, out var time) || time != File.GetLastWriteTimeUtc(file)).ToArray();
                Assert.HasCount(0, changed, $"Reload compiled new graphs: {string.Join(", ", changed)}.");
            }
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    [TestCategory("NPU")]
    public void Infer_EmbeddingGemmaLongTextRepeatedly_ExecutesNpuKernels()
    {
        using var runtime = new OnnxRuntimeGpuWin();
        using (var probe = new NPUOVInferenceSession(runtime))
        {
            try { Assert.AreEqual(true, probe.CheckAvailability()); }
            catch (InvalidOperationException exception) when (
                exception.Message == "OpenVINO did not report an available NPU device.")
            {
                Assert.Inconclusive("An Intel NPU is required for this execution test.");
            }
        }
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Kitopia.sln"))) root = root.Parent;
        Assert.IsNotNull(root);
        using var reference = JsonDocument.Parse(File.ReadAllText(Path.Combine(root.FullName,
            "KitopiaTest", "Search", "EmbeddingGemmaReference", "reference.json")));
        var row = reference.RootElement.GetProperty("texts")[0];
        var tokens = row.GetProperty("ids").EnumerateArray().Select(value => value.GetInt64()).ToArray();
        var expected = row.GetProperty("vector").EnumerateArray().Select(value => value.GetDouble()).ToArray();
        const int sequenceLength = 1024;
        var ids = new long[sequenceLength];
        var mask = new long[sequenceLength];
        tokens.CopyTo(ids, 0);
        Array.Fill(mask, 1L, 0, tokens.Length);
        using var options = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_BASIC,
            IntraOpNumThreads = 1, InterOpNumThreads = 1,
            ProfileOutputPathPrefix = Path.Combine(Path.GetTempPath(), $"kitopia-npu-execution-{Guid.NewGuid():N}"),
            EnableProfiling = true
        };
        options.AddFreeDimensionOverrideByName("batch_size", 1);
        options.AddFreeDimensionOverrideByName("sequence_length", sequenceLength);
        options.AddFreeDimensionOverrideByName("num_image_tokens", 280);
        foreach (var name in new[] { "num_video_tokens", "num_audio_tokens" })
            options.AddFreeDimensionOverrideByName(name, 1);
        typeof(OnnxRuntimeGpuWin).GetMethod("AppendExecutionProvider", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(runtime, [options, OrtHardwareDeviceType.NPU, 0, true]);
        using var session = new InferenceSession(Path.Combine(root.FullName,
            "Kitopia.Desktop.Features", "Assets", "EmbeddingGemma2", "onnx", "model_q4.onnx"), options);
        Parallel.For(0, 4, iteration =>
        {
            using var outputs = session.Run([
                NamedOnnxValue.CreateFromTensor("input_ids", new DenseTensor<long>(ids, [1, sequenceLength])),
                NamedOnnxValue.CreateFromTensor("attention_mask", new DenseTensor<long>(mask, [1, sequenceLength])),
                NamedOnnxValue.CreateFromTensor("image_features", new DenseTensor<float>(new float[280 * 512], [280, 512])),
                NamedOnnxValue.CreateFromTensor("video_features", new DenseTensor<float>(new float[512], [1, 512])),
                NamedOnnxValue.CreateFromTensor("audio_features", new DenseTensor<float>(new float[512], [1, 512]))
            ], ["sentence_embedding"]);
            var vector = outputs[0].AsTensor<float>();
            var norm = Math.Sqrt(vector.Sum(value => (double)value * value));
            var cosine = vector.Select((value, index) => value * expected[index]).Sum() / norm;
            Assert.IsTrue(cosine > 0.9999, $"Iteration {iteration}: cosine {cosine}.");
        });
        var profilePath = session.EndProfiling();
        try
        {
            using var profile = JsonDocument.Parse(File.ReadAllText(profilePath));
            var executed = profile.RootElement.EnumerateArray()
                .Where(item => item.TryGetProperty("args", out var args)
                               && args.TryGetProperty("ov_status", out var status) && status.GetString() == "EXECUTED")
                .Select(item => item.GetProperty("args").GetProperty("ov_exec_type").GetString()).ToArray();
            // The OpenVINO provider name alone also covers its internal CPU fallback.
            Assert.IsTrue(executed.Any(type => type == "DPU"), "No NPU compute kernels executed.");
            Assert.IsTrue(executed.All(type => type is "DPU" or "Shave" or "DMA"),
                $"Unexpected execution types: {string.Join(", ", executed.Distinct())}.");
        }
        finally { File.Delete(profilePath); }
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
            using var options = new SessionOptions();
            options.AddSessionConfigEntry("session.disable_cpu_ep_fallback", "1");
            typeof(OnnxRuntimeGpuWin).GetMethod("AppendExecutionProvider", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(runtime, [options, deviceType, 0, false]);
            // Exercise the first session after enumeration before any accelerator session stays alive.
            var probe = typeof(OnnxRuntimeGpuWin).Assembly.GetType("OnnxRuntime.Shared.RuntimeAvailabilityProbe", true)!;
            Assert.AreEqual(true, probe.GetMethod("Check")!.Invoke(null, [options, true, false]));
            var path = Path.Combine(Path.GetTempPath(), $"kitopia-openvino-{Guid.NewGuid():N}.onnx");
            try
            {
                File.WriteAllBytes(path, Model);
                for (var iteration = 0; iteration < 3; iteration++)
                {
                    Assert.AreEqual(true, await Task.Run(cpu.CheckAvailability));
                    Assert.AreEqual(true, await Task.Run(session.CheckAvailability));
                    if (iteration == 1)
                        session.InitSession(path, true, 2);
                    else
                        session.InitSession(Model);
                    List<(string, Memory<int>, Memory<float>)> inputs =
                        [("input", new int[] { 1, 16 }, Enumerable.Repeat(1f, 16).ToArray())];
                    var output = await Task.Run(() => session.Infer([], inputs, "output", CancellationToken.None));
                    CollectionAssert.AreEqual(Enumerable.Repeat(2f, 16).ToArray(), output.ToArray());
                }
            }
            finally
            {
                File.Delete(path);
            }
        }
        else
        {
            var exception = Assert.ThrowsExactly<InvalidOperationException>(() => session.CheckAvailability());
            StringAssert.Contains(exception.Message, deviceType.ToString());
        }
    }

    [TestMethod]
    public void CheckAvailability_CompilationFails_IncludesRuntimeVersionsAndNativeError()
    {
        using var runtime = new OnnxRuntimeGpuWin();
        using var cpu = new CPUOVInferenceSession(runtime);
        Assert.AreEqual(true, cpu.CheckAvailability());
        var environment = OrtEnv.Instance();
        var device = environment.GetEpDevices().First(device =>
            device.EpName == OpenVINOEp.GetEpName() && device.HardwareDevice.Type == OrtHardwareDeviceType.CPU);
        using var options = new SessionOptions();
        options.AddSessionConfigEntry("session.use_env_allocators", "1");
        options.AppendExecutionProvider(environment, [device], new Dictionary<string, string>
        {
            ["load_config"] = """{"CPU":{"KITOPIA_INVALID_PROBE_PROPERTY":"YES"}}"""
        });
        var probe = typeof(OnnxRuntimeGpuWin).Assembly.GetType("OnnxRuntime.Shared.RuntimeAvailabilityProbe", true)!;
        var invocation = Assert.ThrowsExactly<TargetInvocationException>(() =>
            probe.GetMethod("Check")!.Invoke(null, [options, true, false]));
        Assert.IsInstanceOfType<InvalidOperationException>(invocation.InnerException);
        var message = invocation.InnerException.Message;
        StringAssert.Contains(message, "KITOPIA_INVALID_PROBE_PROPERTY");
        StringAssert.Contains(message, $"OnnxRuntime.OpenVino {typeof(OnnxRuntimeGpuWin).Assembly.GetName().Version}");
        StringAssert.Contains(message, $"ONNX Runtime {environment.GetVersionString()}");
        if (OperatingSystem.IsWindows())
        {
            StringAssert.Contains(message, "openvino.dll");
            StringAssert.Contains(message, "openvino_intel_cpu_plugin.dll");
        }
    }

    [TestMethod]
    public void Infer_ReportedDevices_UsesRegisteredEnvironmentAllocator()
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
            typeof(OnnxRuntimeGpuWin).GetMethod("AppendExecutionProvider", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(runtime, [options, device.HardwareDevice.Type, 0, false]);
            using var session = new InferenceSession(Model, options);
            using var inputMemory = session.GetMemoryInfosForInputs();
            foreach (var memory in inputMemory)
            {
                using var allocator = new OrtAllocator(session, memory);
                using var shared = OrtEnv.Instance().GetSharedAllocator(memory);
                Assert.IsNotNull(shared);
                var sessionAllocation = allocator.Allocate(64);
                var pointer = sessionAllocation.DangerousGetHandle();
                sessionAllocation.Dispose();
                using var sharedAllocation = shared.Allocate(64);
                Assert.AreEqual(pointer, sharedAllocation.DangerousGetHandle(),
                    "The session and environment must reuse the same device allocator's freed block.");
            }
            using var output = session.Run([
                NamedOnnxValue.CreateFromTensor("input",
                    new DenseTensor<float>(new float[16], [1, 16]))
            ], ["output"]);
            CollectionAssert.AreEqual(Enumerable.Repeat(1f, 16).ToArray(), output[0].AsTensor<float>().ToArray());
        }
    }
}
