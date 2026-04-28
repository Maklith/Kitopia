using System.IO.Pipelines;
using Core.Services.DeviceCommunication;
using Core.Services.DeviceCommunication.Protocol;
using Core.Services.DeviceCommunication.Routing;
using Core.Services.DeviceCommunication.Security;

namespace KitopiaTest.DeviceCommunication;

[TestClass]
public sealed class RoutingTests
{
    #region RouteHandlerRegistry

    [TestMethod]
    public void RouteHandlerRegistry_Throws_WhenDuplicateRoute()
    {
        var handlers = new IRouteHandler[]
        {
            new StubRouteHandler("chat"),
            new StubRouteHandler("chat")
        };

        Assert.ThrowsExactly<InvalidOperationException>(() => new RouteHandlerRegistry(handlers));
    }

    [TestMethod]
    public void RouteHandlerRegistry_TryGet_ReturnsHandler_ForKnownRoute()
    {
        var handler = new StubRouteHandler("chat");
        var registry = new RouteHandlerRegistry(new[] { handler });

        var found = registry.TryGet("chat", out var resolved);

        Assert.IsTrue(found);
        Assert.AreSame(handler, resolved);
    }

    [TestMethod]
    public void RouteHandlerRegistry_TryGet_ReturnsFalse_ForUnknownRoute()
    {
        var registry = new RouteHandlerRegistry(Array.Empty<IRouteHandler>());

        var found = registry.TryGet("missing", out _);

        Assert.IsFalse(found);
    }

    [TestMethod]
    public void RouteHandlerRegistry_TryGet_IsCaseInsensitive()
    {
        var handler = new StubRouteHandler("Chat");
        var registry = new RouteHandlerRegistry(new[] { handler });

        Assert.IsTrue(registry.TryGet("chat", out _));
        Assert.IsTrue(registry.TryGet("CHAT", out _));
        Assert.IsTrue(registry.TryGet("Chat", out _));
    }

    [TestMethod]
    public void RouteHandlerRegistry_SupportsMultipleRoutes()
    {
        var chat = new StubRouteHandler("chat");
        var clipboard = new StubRouteHandler("clipboard");
        var registry = new RouteHandlerRegistry(new[] { chat, clipboard });

        Assert.IsTrue(registry.TryGet("chat", out var c1));
        Assert.AreSame(chat, c1);
        Assert.IsTrue(registry.TryGet("clipboard", out var c2));
        Assert.AreSame(clipboard, c2);
        Assert.IsFalse(registry.TryGet("missing", out _));
    }

    #endregion

    #region MessageRouter

    [TestMethod]
    public async Task RouteAsync_CallsHandler_ForKnownRoute()
    {
        var handler = new StubRouteHandler("chat");
        var router = new MessageRouter(new RouteHandlerRegistry(new[] { handler }), new StubErrorPolicy());
        var envelope = new DataEnvelope { Route = "chat", Command = "text", StreamType = DataStreamType.Text };

        await router.RouteAsync(CreateContext(), envelope, PipeReader.Create(Stream.Null));

        Assert.AreEqual(1, handler.CallCount);
    }

    [TestMethod]
    public async Task RouteAsync_UsesErrorPolicy_ForUnknownRoute()
    {
        var policy = new StubErrorPolicy();
        var router = new MessageRouter(new RouteHandlerRegistry(Array.Empty<IRouteHandler>()), policy);
        var envelope = new DataEnvelope { Route = "missing", Command = "x", StreamType = DataStreamType.Text };

        await router.RouteAsync(CreateContext(), envelope, PipeReader.Create(Stream.Null));

        Assert.AreEqual(1, policy.CallCount);
        Assert.AreEqual(ProtocolErrorCode.RouteNotFound, policy.LastError.Code);
    }

    [TestMethod]
    public async Task RouteAsync_PassesContextAndEnvelope_ToHandler()
    {
        var handler = new StubRouteHandler("chat");
        var router = new MessageRouter(new RouteHandlerRegistry(new[] { handler }), new StubErrorPolicy());
        var context = new MessageContext(LocalDataTransportProtocol.Quic, new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 9999), "key");
        var envelope = new DataEnvelope { Route = "chat", Command = "file", StreamType = DataStreamType.File };

        await router.RouteAsync(context, envelope, PipeReader.Create(Stream.Null));

        Assert.AreEqual(1, handler.CallCount);
        Assert.AreEqual("chat", handler.LastContext.HasValue ? "chat" : null);
    }

    [TestMethod]
    public async Task RouteAsync_CallsCorrectHandler_WhenMultipleRegistered()
    {
        var chat = new StubRouteHandler("chat");
        var clipboard = new StubRouteHandler("clipboard");
        var router = new MessageRouter(new RouteHandlerRegistry(new[] { chat, clipboard }), new StubErrorPolicy());

        await router.RouteAsync(CreateContext(),
            new DataEnvelope { Route = "clipboard", Command = "text", StreamType = DataStreamType.Text },
            PipeReader.Create(Stream.Null));

        Assert.AreEqual(0, chat.CallCount);
        Assert.AreEqual(1, clipboard.CallCount);
    }

    #endregion

    #region ProtocolErrorPolicy

    [TestMethod]
    public void ProtocolErrorPolicy_ResolveScope_ReturnsConnection_ForSecurityValidationFailed()
    {
        var policy = new ProtocolErrorPolicy();
        var error = new ProtocolError(ProtocolErrorCode.SecurityValidationFailed, "bad sig");

        Assert.AreEqual(ProtocolErrorScope.Connection, policy.ResolveScope(error));
    }

    [TestMethod]
    public void ProtocolErrorPolicy_ResolveScope_ReturnsSession_ForInvalidFrame()
    {
        var policy = new ProtocolErrorPolicy();
        var error = new ProtocolError(ProtocolErrorCode.InvalidFrame, "bad frame");

        Assert.AreEqual(ProtocolErrorScope.Session, policy.ResolveScope(error));
    }

    [TestMethod]
    public void ProtocolErrorPolicy_ResolveScope_ReturnsSession_ForChannelNotFound()
    {
        var policy = new ProtocolErrorPolicy();
        var error = new ProtocolError(ProtocolErrorCode.ChannelNotFound, "no channel");

        Assert.AreEqual(ProtocolErrorScope.Session, policy.ResolveScope(error));
    }

    [TestMethod]
    public void ProtocolErrorPolicy_ResolveScope_ReturnsMessage_ForRouteNotFound()
    {
        var policy = new ProtocolErrorPolicy();
        var error = new ProtocolError(ProtocolErrorCode.RouteNotFound, "no route");

        Assert.AreEqual(ProtocolErrorScope.Message, policy.ResolveScope(error));
    }

    [TestMethod]
    public void ProtocolErrorPolicy_ResolveScope_ReturnsMessage_ForUnknown()
    {
        var policy = new ProtocolErrorPolicy();
        var error = new ProtocolError(ProtocolErrorCode.Unknown, "unknown");

        Assert.AreEqual(ProtocolErrorScope.Message, policy.ResolveScope(error));
    }

    [TestMethod]
    public async Task ProtocolErrorPolicy_HandleAsync_CompletesWithoutError()
    {
        var policy = new ProtocolErrorPolicy();
        var context = CreateContext();
        var error = new ProtocolError(ProtocolErrorCode.RouteNotFound, "test");

        await policy.HandleAsync(context, error);
    }

    #endregion

    #region Helpers

    private static MessageContext CreateContext()
    {
        return new MessageContext(
            LocalDataTransportProtocol.Tcp,
            new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 12345),
            "peer");
    }

    private sealed class StubRouteHandler : IRouteHandler
    {
        public string Route { get; }
        public int CallCount { get; private set; }
        public MessageContext? LastContext { get; private set; }

        public StubRouteHandler(string route) => Route = route;

        public ValueTask HandleAsync(MessageContext context, DataEnvelope envelope, PipeReader payload,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastContext = context;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class StubErrorPolicy : IProtocolErrorPolicy
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

    #endregion
}
