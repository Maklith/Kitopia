using System.IO.Pipelines;
using System.Text.Json;
using Kitopia.Feature.DeviceCommunication.Protocol;

namespace KitopiaTest.DeviceCommunication;

[TestClass]
public sealed class SharedProtocolSessionTests
{
    [TestMethod]
    [DataRow("forged-device")]
    [DataRow("")]
    [DataRow(null)]
    public async Task HandleAsync_AuthenticatedPeer_OverridesUntrustedSenderId(string? senderId)
    {
        var envelope = new DataEnvelope
        {
            Route = "chat",
            Command = "text",
            Metadata = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["conversationId"] = "receiver-device",
                ["senderId"] = senderId,
                ["text"] = "hello"
            }
        };
        var envelopeBytes = JsonSerializer.SerializeToUtf8Bytes(envelope);
        var frameBytes = ProtocolFrame.BuildHeader(envelopeBytes.Length, 0)
            .Concat(envelopeBytes)
            .ToArray();
        DataEnvelope? dispatched = null;
        var session = new ProtocolSession((message, _, _) =>
        {
            dispatched = message;
            return ValueTask.CompletedTask;
        });
        var reader = PipeReader.Create(new MemoryStream(frameBytes));

        Assert.IsTrue(await session.HandleAsync(reader, "authenticated-device"));
        Assert.IsNotNull(dispatched);
        Assert.AreEqual("authenticated-device", dispatched.Metadata?["senderId"]);
        Assert.AreEqual("receiver-device", dispatched.Metadata?["conversationId"]);

        await reader.CompleteAsync();
    }

    [TestMethod]
    public async Task HandleAsync_AuthenticatedPeer_AddsMissingSenderId()
    {
        var envelope = new DataEnvelope
        {
            Route = "chat",
            Command = "text",
            Metadata = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["conversationId"] = "receiver-device",
                ["text"] = "hello"
            }
        };
        var envelopeBytes = JsonSerializer.SerializeToUtf8Bytes(envelope);
        var frameBytes = ProtocolFrame.BuildHeader(envelopeBytes.Length, 0)
            .Concat(envelopeBytes)
            .ToArray();
        DataEnvelope? dispatched = null;
        var session = new ProtocolSession((message, _, _) =>
        {
            dispatched = message;
            return ValueTask.CompletedTask;
        });
        var reader = PipeReader.Create(new MemoryStream(frameBytes));

        Assert.IsTrue(await session.HandleAsync(reader, "authenticated-device"));
        Assert.IsNotNull(dispatched);
        Assert.AreEqual("authenticated-device", dispatched.Metadata?["senderId"]);

        await reader.CompleteAsync();
    }

    [TestMethod]
    public async Task HandleAsync_IncompleteHeader_StopsAtEnvelopeDeadline()
    {
        var pipe = new Pipe();
        await pipe.Writer.WriteAsync(new byte[] { 0x4b });
        var dispatched = false;
        var session = new ProtocolSession((_, _, _) =>
        {
            dispatched = true;
            return ValueTask.CompletedTask;
        });

        try
        {
            await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () =>
            {
                await session.HandleAsync(
                    pipe.Reader,
                    "authenticated-device",
                    envelopeTimeout: TimeSpan.FromMilliseconds(100));
            });
            Assert.IsFalse(dispatched);
        }
        finally
        {
            await pipe.Writer.CompleteAsync();
            await pipe.Reader.CompleteAsync();
        }
    }
}
