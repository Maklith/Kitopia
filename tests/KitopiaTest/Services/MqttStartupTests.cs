using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Kitopia.Desktop.Features.Services.MQTT;
using MQTTnet.Server;
using Newtonsoft.Json.Linq;
using PluginCore;

namespace KitopiaTest.Services;

[TestClass]
public sealed class MqttStartupTests
{
    private string _directory = null!;
    private string _portFilePath = null!;

    [TestInitialize]
    public void Initialize()
    {
        _directory = Path.Combine(Path.GetTempPath(), "Kitopia MQTT " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _portFilePath = Path.Combine(_directory, ".port");
    }

    [TestCleanup]
    public void Cleanup()
    {
        Directory.Delete(_directory, recursive: true);
    }

    [TestMethod]
    public async Task AcquirePortFileAsync_NewFile_AllowsReadersAndExcludesOtherWriters()
    {
        using var owner = await MqttManager.AcquirePortFileAsync(_portFilePath, []);
        Assert.IsNotNull(owner);
        owner.Write(Encoding.UTF8.GetBytes("6600"));
        owner.Flush();

        using var reader = new FileStream(_portFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var textReader = new StreamReader(reader);
        Assert.AreEqual("6600", await textReader.ReadToEndAsync());
        Assert.Throws<IOException>(() =>
        {
            using var contender = new FileStream(_portFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
        });
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AcquirePortFileAsync_StaleFile_ReusesFileWithoutDeletingIt(bool hidden)
    {
        if (hidden && !OperatingSystem.IsWindows()) return;
        await File.WriteAllTextAsync(_portFilePath, "invalid stale port");
        if (hidden) File.SetAttributes(_portFilePath, FileAttributes.Hidden);

        using var owner = await MqttManager.AcquirePortFileAsync(_portFilePath, []);
        Assert.IsNotNull(owner);
        owner.SetLength(0);
        owner.Write(Encoding.UTF8.GetBytes("6601"));
        owner.Flush();
        if (hidden) Assert.IsTrue(File.GetAttributes(_portFilePath).HasFlag(FileAttributes.Hidden));
    }

    [TestMethod]
    public async Task AcquirePortFileAsync_OwnerExitsDuringStartup_AcquiresReleasedFile()
    {
        using var owner = await MqttManager.AcquirePortFileAsync(_portFilePath, []);
        Assert.IsNotNull(owner);
        var pending = MqttManager.AcquirePortFileAsync(_portFilePath, []);
        Assert.IsFalse(pending.IsCompleted);
        owner.Dispose();

        using var nextOwner = await pending.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.IsNotNull(nextOwner);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AcquirePortFileAsync_ExistingOwner_ForwardsArgumentsAfterBrokerStarts(bool delayedStartup)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var factory = new MqttServerFactory();
        using var server = factory.CreateMqttServer(factory.CreateServerOptionsBuilder()
            .WithDefaultEndpoint().WithDefaultEndpointBoundIPAddress(IPAddress.Loopback)
            .WithDefaultEndpointPort(port).Build());
        var received = new TaskCompletionSource<JObject>(TaskCreationOptions.RunContinuationsAsynchronously);
        server.InterceptingPublishAsync += args =>
        {
            if (args.ApplicationMessage.Topic == "test")
                received.TrySetResult(JObject.Parse(Encoding.UTF8.GetString(args.ApplicationMessage.Payload.ToArray())));
            return Task.CompletedTask;
        };

        using var owner = await MqttManager.AcquirePortFileAsync(_portFilePath, []);
        Assert.IsNotNull(owner);
        string[] paths = [@"C:\Downloads\first document.png", @"C:\Downloads\第二个文件.png"];
        string[] arguments = ["-action:BatchRename", "-value:" + paths[0], paths[1]];
        Task<FileStream?>? pending = null;
        if (delayedStartup)
        {
            pending = MqttManager.AcquirePortFileAsync(_portFilePath, arguments);
            Assert.IsFalse(pending.IsCompleted);
        }

        await server.StartAsync();
        try
        {
            owner.Write(Encoding.UTF8.GetBytes(port.ToString()));
            owner.Flush();
            pending ??= MqttManager.AcquirePortFileAsync(_portFilePath, arguments);
            using var acquired = await pending.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.IsNull(acquired);
            var payload = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.AreEqual((int)StartupAction.BatchRename, payload["type"]!.Value<int>());
            Assert.AreEqual(paths[0], payload["value"]!.Value<string>());
            CollectionAssert.AreEqual(paths, payload["values"]!.ToObject<string[]>()!);
        }
        finally
        {
            await server.StopAsync();
        }
    }

    [TestMethod]
    public async Task AcquirePortFileAsync_LegacyExclusiveOwnerExits_AcquiresReleasedFile()
    {
        using var legacyOwner = new FileStream(_portFilePath, FileMode.CreateNew);
        legacyOwner.Write(Encoding.UTF8.GetBytes("6600"));
        legacyOwner.Flush();
        var pending = MqttManager.AcquirePortFileAsync(_portFilePath, []);
        Assert.IsFalse(pending.IsCompleted);
        legacyOwner.Dispose();

        using var nextOwner = await pending.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.IsNotNull(nextOwner);
    }
}
