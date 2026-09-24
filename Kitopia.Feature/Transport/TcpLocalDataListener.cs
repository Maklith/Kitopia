using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Kitopia.Feature.DeviceCommunication.Diagnostics;
using Kitopia.Feature.DeviceCommunication.Protocol;
using Kitopia.Feature.DeviceCommunication.Security;

namespace Kitopia.Feature.DeviceCommunication.Transport;

public sealed class TcpLocalDataListener : ILocalDataTransport
{
    private const string LogCategory = "TcpLocalDataListener";
    private const int MaximumConcurrentConnections = 32;
    private const long MinimumBytesPerWindow = 64 * 1024;
    private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan EnvelopeTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan ReadWindow = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(5);
    private static readonly SslApplicationProtocol ApplicationProtocol = new("kitopia-local-data");
    private static readonly SslProtocols EnabledProtocols = SslProtocols.Tls13 | SslProtocols.Tls12;
    private static readonly StreamPipeReaderOptions InboundPipeReaderOptions = new(
        bufferSize: 256 * 1024,
        minimumReadSize: 64 * 1024,
        leaveOpen: true);

    private readonly object _sync = new();
    private readonly ProtocolSession _protocolSession;
    private readonly DeviceTransportSecurity _transportSecurity;
    private readonly IRemoteIdentityResolver _remoteIdentityResolver;
    private int _activeConnections;
    private int _port;

    private TcpListener? _listener;
    private X509Certificate2? _certificate;
    private CancellationTokenSource? _cts;
    private Task? _acceptTask;

    public TcpLocalDataListener(
        ProtocolSession protocolSession,
        DeviceTransportSecurity transportSecurity,
        IRemoteIdentityResolver remoteIdentityResolver)
    {
        _protocolSession = protocolSession;
        _transportSecurity = transportSecurity;
        _remoteIdentityResolver = remoteIdentityResolver;
    }

    public int Port
    {
        get
        {
            lock (_sync)
            {
                return _port;
            }
        }
    }

    public bool IsRunning { get; private set; }

    public LocalDataTransportProtocol Protocol => LocalDataTransportProtocol.Tcp;

    public async Task<bool> StartAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            lock (_sync)
            {
                if (IsRunning)
                {
                    return true;
                }

                _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                _certificate = _transportSecurity.CreateIdentityCertificate("CN=Kitopia-Local-Tcp");
            }

            var listener = new TcpListener(IPAddress.IPv6Any, 0);
            listener.Server.DualMode = true;
            listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            listener.Start();
            if (listener.LocalEndpoint is not IPEndPoint localEndPoint)
            {
                listener.Stop();
                throw new InvalidOperationException("Failed to resolve local TCP endpoint.");
            }

            lock (_sync)
            {
                _listener = listener;
                _port = localEndPoint.Port;
                _acceptTask = Task.Run(() => AcceptLoop(listener, _cts!.Token), _cts.Token);
                IsRunning = true;
            }

            DeviceCommunicationDiagnostics.Info(LogCategory, $"TCP local listener started on port {Port}.");
            return true;
        }
        catch (Exception exception)
        {
            DeviceCommunicationDiagnostics.Error(LogCategory, "TCP local listener start failed.", exception);
            await StopAsync().ConfigureAwait(false);
            return false;
        }
    }

    public async Task SendAsync(
        ReadOnlyMemory<byte> payload,
        IPEndPoint remoteEndPoint,
        string? remoteIdentityPublicKey = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(remoteEndPoint);
        if (string.IsNullOrWhiteSpace(remoteIdentityPublicKey))
        {
            throw new ArgumentException("Remote identity public key is required.", nameof(remoteIdentityPublicKey));
        }

        if (payload.IsEmpty)
        {
            return;
        }

        using var client = new TcpClient(remoteEndPoint.AddressFamily);
        await client.ConnectAsync(remoteEndPoint.Address, remoteEndPoint.Port, cancellationToken);
        await using var sslStream = await AuthenticateAsClientAsync(
            client,
            remoteIdentityPublicKey.Trim(),
            cancellationToken);
        await sslStream.WriteAsync(payload, cancellationToken);
        await CompleteSendAsync(sslStream, remoteEndPoint, cancellationToken);
    }

    public async Task SendAsync(
        PipeReader payloadReader,
        IPEndPoint remoteEndPoint,
        string? remoteIdentityPublicKey = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payloadReader);
        ArgumentNullException.ThrowIfNull(remoteEndPoint);
        if (string.IsNullOrWhiteSpace(remoteIdentityPublicKey))
        {
            throw new ArgumentException("Remote identity public key is required.", nameof(remoteIdentityPublicKey));
        }

        using var client = new TcpClient(remoteEndPoint.AddressFamily);
        await client.ConnectAsync(remoteEndPoint.Address, remoteEndPoint.Port, cancellationToken);
        await using var sslStream = await AuthenticateAsClientAsync(
            client,
            remoteIdentityPublicKey.Trim(),
            cancellationToken);
        await payloadReader.CopyToAsync(sslStream, cancellationToken);
        await CompleteSendAsync(sslStream, remoteEndPoint, cancellationToken);
    }

    public async Task StopAsync()
    {
        Task? acceptTask;
        TcpListener? listener;

        lock (_sync)
        {
            if (!IsRunning && _listener is null)
            {
                return;
            }

            IsRunning = false;
            _cts?.Cancel();
            listener = _listener;
            _listener = null;
            acceptTask = _acceptTask;
            _acceptTask = null;
        }

        listener?.Stop();

        if (acceptTask is not null)
        {
            try
            {
                await acceptTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }

        lock (_sync)
        {
            _cts?.Dispose();
            _cts = null;
            _certificate?.Dispose();
            _certificate = null;
            _port = 0;
        }

        DeviceCommunicationDiagnostics.Info(LogCategory, "TCP local listener stopped.");
    }

    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
    }

    private async Task<SslStream> AuthenticateAsClientAsync(
        TcpClient client,
        string expectedRemoteIdentity,
        CancellationToken token)
    {
        X509Certificate2? localCertificate;
        lock (_sync)
        {
            localCertificate = _certificate;
        }

        if (localCertificate is null)
        {
            throw new InvalidOperationException("TCP local certificate is not ready.");
        }

        var stream = new SslStream(client.GetStream(), leaveInnerStreamOpen: false);
        await stream.AuthenticateAsClientAsync(
            new SslClientAuthenticationOptions
            {
                TargetHost = "Kitopia-Local-Tcp",
                EnabledSslProtocols = EnabledProtocols,
                ApplicationProtocols = [ApplicationProtocol],
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
                ClientCertificates = [localCertificate],
                RemoteCertificateValidationCallback = (_, remoteCertificate, _, _) =>
                    _transportSecurity.ValidateRemoteCertificate(remoteCertificate, expectedRemoteIdentity)
            },
            token);

        if (!stream.NegotiatedApplicationProtocol.Equals(ApplicationProtocol))
        {
            throw new AuthenticationException(
                $"TCP ALPN negotiation failed. Expected={ApplicationProtocol}, Actual={stream.NegotiatedApplicationProtocol}.");
        }

        return stream;
    }

    private async Task AcceptLoop(TcpListener listener, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var client = await listener.AcceptTcpClientAsync(token);
                if (Interlocked.Increment(ref _activeConnections) > MaximumConcurrentConnections)
                {
                    Interlocked.Decrement(ref _activeConnections);
                    client.Dispose();
                    continue;
                }

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await HandleClientAsync(client, token);
                    }
                    finally
                    {
                        Interlocked.Decrement(ref _activeConnections);
                    }
                });
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception exception)
            {
                DeviceCommunicationDiagnostics.Error(LogCategory, "TCP local listener accept failed.", exception);
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken token)
    {
        using (client)
        {
            try
            {
                if (client.Client.RemoteEndPoint is not IPEndPoint remoteEndPoint)
                {
                    return;
                }

                var expectedRemoteIdentityPublicKey =
                    _remoteIdentityResolver.ResolveExpectedIdentityPublicKey(remoteEndPoint);
                if (string.IsNullOrWhiteSpace(expectedRemoteIdentityPublicKey))
                {
                    return;
                }

                X509Certificate2? certificate;
                lock (_sync)
                {
                    certificate = _certificate;
                }

                if (certificate is null)
                {
                    throw new InvalidOperationException("TCP local certificate is not ready.");
                }

                var serverCertificateContext = SslStreamCertificateContext.Create(
                    certificate,
                    additionalCertificates: null,
                    offline: true);
                await using var sslStream = new SslStream(client.GetStream(), leaveInnerStreamOpen: false);
                using var handshakeCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
                handshakeCancellation.CancelAfter(HandshakeTimeout);
                await sslStream.AuthenticateAsServerAsync(
                    new SslServerAuthenticationOptions
                    {
                        ServerCertificateContext = serverCertificateContext,
                        ClientCertificateRequired = true,
                        EnabledSslProtocols = EnabledProtocols,
                        ApplicationProtocols = [ApplicationProtocol],
                        CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
                        RemoteCertificateValidationCallback = (_, remoteCertificate, _, _) =>
                            _transportSecurity.ValidateRemoteCertificate(remoteCertificate, expectedRemoteIdentityPublicKey)
                    },
                    handshakeCancellation.Token);

                if (!sslStream.NegotiatedApplicationProtocol.Equals(ApplicationProtocol))
                {
                    throw new AuthenticationException(
                        $"TCP ALPN negotiation failed. Expected={ApplicationProtocol}, Actual={sslStream.NegotiatedApplicationProtocol}.");
                }

                var reader = PipeReader.Create(sslStream, InboundPipeReaderOptions);
                Exception? readerError = null;
                try
                {
                    await _protocolSession.HandleAsync(
                        new RateLimitedPipeReader(reader),
                        expectedRemoteIdentityPublicKey,
                        token,
                        envelopeTimeout: EnvelopeTimeout);
                }
                catch (Exception ex)
                {
                    readerError = ex;
                    throw;
                }
                finally
                {
                    await reader.CompleteAsync(readerError);
                }

                using var closeCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
                closeCancellation.CancelAfter(CloseTimeout);
                await CompleteReceiveAsync(sslStream, remoteEndPoint, closeCancellation.Token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            catch (Exception exception)
            {
                DeviceCommunicationDiagnostics.Error(
                    LogCategory,
                    $"TCP local listener stream handling failed for {client.Client.RemoteEndPoint}.",
                    exception);
            }
        }
    }

    private static async Task CompleteSendAsync(
        SslStream sslStream,
        IPEndPoint remoteEndPoint,
        CancellationToken cancellationToken)
    {
        try
        {
            await sslStream.FlushAsync(cancellationToken);
            await sslStream.ShutdownAsync();
        }
        catch (IOException exception)
        {
            if (IsExpectedTlsShutdownIOException(exception))
            {
                return;
            }

            DeviceCommunicationDiagnostics.Debug(
                LogCategory,
                $"TLS shutdown write failed when sending to {remoteEndPoint}: {exception.Message}");
            return;
        }
        catch (AuthenticationException exception)
        {
            DeviceCommunicationDiagnostics.Debug(
                LogCategory,
                $"TLS shutdown auth failed when sending to {remoteEndPoint}: {exception.Message}");
            return;
        }

        var buffer = new byte[1];
        try
        {
            while (true)
            {
                var read = await sslStream.ReadAsync(buffer, cancellationToken);
                if (read == 0)
                {
                    break;
                }
            }
        }
        catch (IOException exception)
        {
            if (IsExpectedTlsShutdownIOException(exception))
            {
                return;
            }

            DeviceCommunicationDiagnostics.Debug(
                LogCategory,
                $"TLS close notification read failed from {remoteEndPoint}: {exception.Message}");
        }
        catch (AuthenticationException exception)
        {
            DeviceCommunicationDiagnostics.Debug(
                LogCategory,
                $"TLS session close failed from {remoteEndPoint}: {exception.Message}");
        }
    }

    private static async Task CompleteReceiveAsync(
        SslStream sslStream,
        IPEndPoint remoteEndPoint,
        CancellationToken cancellationToken)
    {
        try
        {
            await sslStream.FlushAsync(cancellationToken);
            await sslStream.ShutdownAsync();
        }
        catch (IOException exception)
        {
            if (IsExpectedTlsShutdownIOException(exception))
            {
                return;
            }

            DeviceCommunicationDiagnostics.Debug(
                LogCategory,
                $"Server-side TLS close failed for {remoteEndPoint}: {exception.Message}");
        }
        catch (AuthenticationException exception)
        {
            DeviceCommunicationDiagnostics.Debug(
                LogCategory,
                $"Server-side TLS shutdown auth failure for {remoteEndPoint}: {exception.Message}");
        }
    }

    private static bool IsExpectedTlsShutdownIOException(IOException exception)
    {
        return exception.InnerException is SocketException
        {
            SocketErrorCode: SocketError.ConnectionReset or
            SocketError.ConnectionAborted or
            SocketError.OperationAborted or
            SocketError.Shutdown
        };
    }

    private sealed class RateLimitedPipeReader : PipeReader
    {
        private readonly PipeReader _inner;
        private ReadOnlySequence<byte> _lastBuffer;
        private long _windowStarted = Stopwatch.GetTimestamp();
        private long _windowBytes;

        public RateLimitedPipeReader(PipeReader inner)
        {
            _inner = inner;
        }

        public override void AdvanceTo(SequencePosition consumed) => AdvanceTo(consumed, consumed);

        public override void AdvanceTo(SequencePosition consumed, SequencePosition examined)
        {
            _windowBytes += _lastBuffer.Slice(0, consumed).Length;
            _inner.AdvanceTo(consumed, examined);
        }

        public override void CancelPendingRead() => _inner.CancelPendingRead();

        public override void Complete(Exception? exception = null) => _inner.Complete(exception);

        public override ValueTask CompleteAsync(Exception? exception = null) => _inner.CompleteAsync(exception);

        public override async ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default)
        {
            var readBudget = GetReadBudget();
            using var readCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            readCancellation.CancelAfter(readBudget);
            try
            {
                var result = await _inner.ReadAsync(readCancellation.Token);
                if (result.IsCanceled)
                {
                    throw new OperationCanceledException(readCancellation.Token);
                }

                _lastBuffer = result.Buffer;
                return result;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("TCP peer stopped making progress during frame receive.");
            }
        }

        public override bool TryRead(out ReadResult result)
        {
            _ = GetReadBudget();
            if (!_inner.TryRead(out result))
            {
                return false;
            }

            _lastBuffer = result.Buffer;
            return true;
        }

        private TimeSpan GetReadBudget()
        {
            var elapsed = Stopwatch.GetElapsedTime(_windowStarted);
            if (elapsed >= ReadWindow)
            {
                if (_windowBytes < MinimumBytesPerWindow)
                {
                    throw new TimeoutException("TCP peer transfer rate is below the minimum receive rate.");
                }

                _windowStarted = Stopwatch.GetTimestamp();
                _windowBytes = 0;
                elapsed = TimeSpan.Zero;
            }

            return _windowBytes < MinimumBytesPerWindow ? ReadWindow - elapsed : ReadWindow;
        }
    }
}
