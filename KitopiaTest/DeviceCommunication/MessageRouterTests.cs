using System.IO.Pipelines;
using Core.Services.DeviceCommunication;
using Core.Services.DeviceCommunication.Protocol;
using Core.Services.DeviceCommunication.Routing;
using Core.Services.DeviceCommunication.Security;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KitopiaTest.DeviceCommunication;

[TestClass]
public sealed class MessageRouterTests
{
    [TestMethod]
    public async Task RouteAsync_CallsHandler_ForKnownRoute()
    {
        var handler = new TestRouteHandler("chat");
        var router = new MessageRouter(new RouteHandlerRegistry(new[] { handler }), new TestErrorPolicy());
        var envelope = new DataEnvelope { Route = "chat", Command = "text", StreamType = DataStreamType.Text };

        await router.RouteAsync(CreateContext(), envelope, PipeReader.Create(Stream.Null));

        Assert.AreEqual(1, handler.CallCount);
    }

    [TestMethod]
    public async Task RouteAsync_UsesErrorPolicy_ForUnknownRoute()
    {
        var policy = new TestErrorPolicy();
        var router = new MessageRouter(new RouteHandlerRegistry(Array.Empty<IRouteHandler>()), policy);
        var envelope = new DataEnvelope { Route = "missing", Command = "x", StreamType = DataStreamType.Text };

        await router.RouteAsync(CreateContext(), envelope, PipeReader.Create(Stream.Null));

        Assert.AreEqual(1, policy.CallCount);
        Assert.AreEqual(ProtocolErrorCode.RouteNotFound, policy.LastError.Code);
    }

    private static MessageContext CreateContext()
    {
        return new MessageContext(
            LocalDataTransportProtocol.Tcp,
            new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 12345),
            "peer");
    }

    private sealed class TestRouteHandler : IRouteHandler
    {
        public TestRouteHandler(string route) => Route = route;
        public string Route { get; }
        public int CallCount { get; private set; }

        public ValueTask HandleAsync(MessageContext context, DataEnvelope envelope, PipeReader payload,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TestErrorPolicy : IProtocolErrorPolicy
    {
        public int CallCount { get; private set; }
        public ProtocolError LastError { get; private set; }

        public ProtocolErrorScope ResolveScope(ProtocolError error) => ProtocolErrorScope.Message;

        public ValueTask HandleAsync(MessageContext context, ProtocolError error,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastError = error;
            return ValueTask.CompletedTask;
        }
    }
}
