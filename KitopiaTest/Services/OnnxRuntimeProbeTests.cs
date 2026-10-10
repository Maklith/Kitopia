using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Avalonia.Headless;
using Kitopia.Desktop.Features.Services.Config;
using Kitopia.Desktop.Features.Services.Plugin;
using Kitopia.Desktop.Services;
using Microsoft.Extensions.DependencyInjection;
using MQTTnet.Server;
using PluginCore;

namespace KitopiaTest.Services;

[TestClass]
[DoNotParallelize]
public sealed class OnnxRuntimeProbeTests
{
    private string _directory = null!;
    private string _pluginPath = null!;
    private string _executablePath = null!;
    private string _assemblyPath = null!;
    private MqttServer _server = null!;
    private int _port;

    [TestInitialize]
    public async Task Initialize()
    {
        _directory = Path.Combine(Path.GetTempPath(), "Kitopia ONNX probe " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        foreach (var path in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "PluginFixture"), "OnnxProbeFixture.*"))
            File.Copy(path, Path.Combine(_directory, Path.GetFileName(path)));
        _pluginPath = Path.Combine(_directory, "OnnxProbeFixture.dll");
        _assemblyPath = Path.Combine(AppContext.BaseDirectory, "Kitopia.Desktop.dll");
        _executablePath = Path.Combine(AppContext.BaseDirectory,
            OperatingSystem.IsWindows() ? "Kitopia.Desktop.exe" : "Kitopia.Desktop");
        Assert.IsTrue(File.Exists(_pluginPath));
        Assert.IsTrue(File.Exists(_executablePath));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        _port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var factory = new MqttServerFactory();
        _server = factory.CreateMqttServer(factory.CreateServerOptionsBuilder().WithDefaultEndpoint()
            .WithDefaultEndpointBoundIPAddress(IPAddress.Loopback).WithDefaultEndpointPort(_port).Build());
        await _server.StartAsync();
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        await _server.StopAsync();
        _server.Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    [TestMethod]
    [DataRow("good", true)]
    [DataRow("unavailable", false)]
    [DataRow("legacy", null)]
    public async Task CheckAsync_StandalonePlugin_ReturnsAvailabilityFromChild(string device, bool? expected)
    {
        var probe = new OnnxRuntimeProbe(_executablePath, _assemblyPath, _port);
        Assert.AreEqual(expected, await probe.CheckAsync(_pluginPath, device));
        if (device != "legacy")
        {
            var pid = int.Parse(await File.ReadAllTextAsync(Path.Combine(_directory, device + ".pid")));
            Assert.AreNotEqual(Environment.ProcessId, pid);
            Assert.ThrowsExactly<ArgumentException>(() => Process.GetProcessById(pid));
        }
    }

    [TestMethod]
    public async Task CheckAsync_ManagedFailure_ReturnsNativeDiagnostic()
    {
        var probe = new OnnxRuntimeProbe(_executablePath, _assemblyPath, _port);
        var error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => probe.CheckAsync(_pluginPath, "throw"));
        StringAssert.Contains(error.Message, "fixture-native-library.dll is missing");
        Assert.AreEqual(true, await probe.CheckAsync(_pluginPath, "good"));
    }

    [TestMethod]
    public async Task CheckAsync_ChildCrashes_ReportsFailureAndNextProbeStillRuns()
    {
        var probe = new OnnxRuntimeProbe(_executablePath, _assemblyPath, _port);
        var error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => probe.CheckAsync(_pluginPath, "crash"));
        StringAssert.Contains(error.Message, "0x");
        Assert.AreEqual(true, await probe.CheckAsync(_pluginPath, "good"));
    }

    [TestMethod]
    public async Task CheckAsync_TimesOut_TerminatesChild()
    {
        var probe = new OnnxRuntimeProbe(_executablePath, _assemblyPath, _port, TimeSpan.FromSeconds(3));
        var task = probe.CheckAsync(_pluginPath, "block");
        using var child = await WaitForChildAsync();
        await Assert.ThrowsExactlyAsync<TimeoutException>(() => task);
        Assert.IsTrue(child.HasExited);
    }

    [TestMethod]
    public async Task CheckAsync_Canceled_TerminatesChildAndPreservesCancellation()
    {
        var probe = new OnnxRuntimeProbe(_executablePath, _assemblyPath, _port);
        using var cancellation = new CancellationTokenSource();
        var task = probe.CheckAsync(_pluginPath, "block", cancellation.Token);
        using var child = await WaitForChildAsync();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => task);
        Assert.IsTrue(child.HasExited);
    }

    [TestMethod]
    public async Task CheckAsync_AlreadyCanceled_DoesNotStartChild()
    {
        var probe = new OnnxRuntimeProbe(_executablePath, _assemblyPath, _port);
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            probe.CheckAsync(_pluginPath, "good", new CancellationToken(canceled: true)));
        Assert.IsFalse(File.Exists(Path.Combine(_directory, "good.pid")));
    }

    [TestMethod]
    public async Task CheckAsync_DotnetLaunch_UsesDesktopAssemblyAndExitsBeforeNormalStartup()
    {
        var dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
        var probe = new OnnxRuntimeProbe(dotnet, _assemblyPath, _port);
        Assert.AreEqual(true, await probe.CheckAsync(_pluginPath, "good"));
    }

    [TestMethod]
    public async Task CheckAsync_ConcurrentMqttReplies_ReturnToTheirOwnRequests()
    {
        var probe = new OnnxRuntimeProbe(_executablePath, _assemblyPath, _port);
        var results = await Task.WhenAll(probe.CheckAsync(_pluginPath, "good"), probe.CheckAsync(_pluginPath, "unavailable"));
        CollectionAssert.AreEqual(new bool?[] { true, false }, results);
    }

    [TestMethod]
    [DataRow("good")]
    [DataRow("unavailable")]
    [DataRow("throw")]
    [DataRow("crash")]
    public async Task CheckAsync_CompletedResult_ReusesMemoryCacheUntilForcedRefresh(string outcome)
    {
        await WithEnabledPluginAsync(async (probe, _) =>
        {
            File.WriteAllText(Path.Combine(_directory, "outcome.txt"), outcome);
            string? originalError = null;
            if (outcome is "throw" or "crash")
                originalError = (await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => probe.CheckAsync("cache"))).Message;
            else Assert.AreEqual(outcome == "good", await probe.CheckAsync("cache"));

            var nextOutcome = outcome == "good" ? "unavailable" : "good";
            File.WriteAllText(Path.Combine(_directory, "outcome.txt"), nextOutcome);
            for (var attempt = 0; attempt < 2; attempt++)
            {
                if (originalError is not null)
                    Assert.AreEqual(originalError,
                        (await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => probe.CheckAsync("cache"))).Message);
                else Assert.AreEqual(outcome == "good", await probe.CheckAsync("cache"));
            }
            Assert.HasCount(1, File.ReadAllLines(Path.Combine(_directory, "cache.runs")));
            Assert.IsNull(await probe.CheckAsync("legacy"));
            Assert.IsNull(await probe.CheckAsync("legacy"));

            Assert.AreEqual(nextOutcome == "good", await probe.CheckAsync("cache", forceRefresh: true));
            File.WriteAllText(Path.Combine(_directory, "outcome.txt"), outcome);
            Assert.AreEqual(nextOutcome == "good", await probe.CheckAsync("cache"));
            Assert.HasCount(2, File.ReadAllLines(Path.Combine(_directory, "cache.runs")));
        });
    }

    [TestMethod]
    public async Task CheckAsync_PluginDisabledAndReloaded_DropsOldCachedResult()
    {
        await WithEnabledPluginAsync(async (probe, info) =>
        {
            File.WriteAllText(Path.Combine(_directory, "outcome.txt"), "good");
            Assert.AreEqual(true, await probe.CheckAsync("cache"));
            Assert.IsTrue(await PluginManager.UnloadCoreAsync(info));
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => probe.CheckAsync("cache"));
            File.WriteAllText(Path.Combine(_directory, "outcome.txt"), "unavailable");
            var pluginInfo = info.PluginBaseInfo;
            pluginInfo.Version = "2.0.0";
            info.PluginBaseInfo = pluginInfo;
            await PluginManager.EnableOneAsync(info);
            Assert.AreEqual(false, await probe.CheckAsync("cache"));
            Assert.HasCount(2, File.ReadAllLines(Path.Combine(_directory, "cache.runs")));
        });
    }

    [TestMethod]
    public async Task CheckAsync_CanceledProbe_IsNotCached()
    {
        await WithEnabledPluginAsync(async (probe, _) =>
        {
            File.WriteAllText(Path.Combine(_directory, "outcome.txt"), "block");
            using var cancellation = new CancellationTokenSource();
            var check = probe.CheckAsync("cache", cancellation.Token);
            using var child = await WaitForChildAsync("cache");
            cancellation.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => check);
            Assert.IsTrue(child.HasExited);
            File.WriteAllText(Path.Combine(_directory, "outcome.txt"), "good");
            Assert.AreEqual(true, await probe.CheckAsync("cache"));
            Assert.HasCount(2, File.ReadAllLines(Path.Combine(_directory, "cache.runs")));
        });
    }

    private async Task WithEnabledPluginAsync(Func<OnnxRuntimeProbe, PluginLocalInfo, Task> action)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(OnnxRuntimeAvailabilityTests));
        await session.Dispatch(async () =>
        {
            var name = "onnx-cache-" + Guid.NewGuid().ToString("N");
            var info = new PluginLocalInfo
            {
                PluginBaseInfo = new PluginBaseInfo
                {
                    Name = name, NameSign = name, Version = "1.0.0", Main = "OnnxProbeFixture.dll", Dependencies = new()
                },
                Path = _directory + Path.DirectorySeparatorChar,
                FullPath = _pluginPath
            };
            var previousServices = ServiceManager.Services;
            using var provider = new ServiceCollection().BuildServiceProvider();
            ServiceManager.Services = provider;
            var probe = new OnnxRuntimeProbe(_executablePath, _assemblyPath, _port);
            try
            {
                await PluginManager.EnableOneAsync(info);
                await action(probe, info);
            }
            finally
            {
                try
                {
                    Assert.IsTrue(await PluginManager.UnloadCoreAsync(info), "Cached results must not keep the plugin loaded.");
                    GC.KeepAlive(probe);
                }
                finally { ServiceManager.Services = previousServices; }
            }
            return true;
        }, CancellationToken.None);
    }

    private async Task<Process> WaitForChildAsync(string device = "block")
    {
        var path = Path.Combine(_directory, device + ".pid");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!File.Exists(path)) await Task.Delay(20, deadline.Token);
        while (true)
        {
            var text = await File.ReadAllTextAsync(path, deadline.Token);
            if (int.TryParse(text, out var pid)) return Process.GetProcessById(pid);
            await Task.Delay(20, deadline.Token);
        }
    }
}
