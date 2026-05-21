using System.IO.Pipelines;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Core.Services.DeviceCommunication.Protocol;
using Core.Services.DeviceCommunication.Security;
using Serilog;

namespace Core.Services.DeviceCommunication;

public sealed class TcpLocalDataListener : ILocalDataTransport
{
    private static readonly ILogger Logger = LogManager.Logger.ForContext<TcpLocalDataListener>();
    private static readonly SslApplicationProtocol ApplicationProtocol = new("kitopia-local-data");
    private static readonly StreamPipeReaderOptions InboundPipeReaderOptions = new(
        bufferSize: 256 * 1024,
        minimumReadSize: 64 * 1024,
        leaveOpen: true);

    private readonly object _sync = new();
    private readonly ProtocolSession _protocolSession;
    private readonly DeviceTransportSecurity _transportSecurity;
    private int _port;

    private TcpListener? _listener;
    private X509Certificate2? _certificate;
    private CancellationTokenSource? _cts;
    private Task? _acceptTask;

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

    public TcpLocalDataListener(ProtocolSession protocolSession, DeviceTransportSecurity transportSecurity)
    {
        _protocolSession = protocolSession;
        _transportSecurity = transportSecurity;
    }

    public bool IsRunning { get; private set; }
    public LocalDataTransportProtocol Protocol => LocalDataTransportProtocol.Tcp;

    public async Task<bool> StartAsync(CancellationToken cancellationToken = default)
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

        try
        {
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

            Logger.Information("TCP local listener started on {Port}", Port);
            return true;
        }
        catch (Exception e)
        {
            Logger.Error(e, "TCP local listener start failed");
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
        await CompleteSendAsync(sslStream, cancellationToken);
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

        await CompleteSendAsync(sslStream, cancellationToken);
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

        Logger.Information("TCP local listener stopped");
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
                EnabledSslProtocols = SslProtocols.Tls13,
                ApplicationProtocols = [ApplicationProtocol],
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
                ClientCertificates = new X509CertificateCollection { localCertificate },
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
                _ = Task.Run(() => HandleClientAsync(client, token), token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception e)
            {
                Logger.Error(e, "TCP local listener accept failed");
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

                var expectedRemoteIdentityPublicKey = _transportSecurity.ResolveExpectedIdentityPublicKey(remoteEndPoint);
                X509Certificate2? certificate;
                lock (_sync)
                {
                    certificate = _certificate;
                }

                if (certificate is null)
                {
                    throw new InvalidOperationException("TCP local certificate is not ready.");
                }

                await using var sslStream = new SslStream(client.GetStream(), leaveInnerStreamOpen: false);
                await sslStream.AuthenticateAsServerAsync(
                    new SslServerAuthenticationOptions
                    {
                        ServerCertificate = certificate,
                        ClientCertificateRequired = true,
                        EnabledSslProtocols = SslProtocols.Tls13,
                        ApplicationProtocols = [ApplicationProtocol],
                        CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
                        RemoteCertificateValidationCallback = (_, remoteCertificate, _, _) =>
                            _transportSecurity.ValidateRemoteCertificate(remoteCertificate, expectedRemoteIdentityPublicKey)
                    },
                    token);

                if (!sslStream.NegotiatedApplicationProtocol.Equals(ApplicationProtocol))
                {
                    throw new AuthenticationException(
                        $"TCP ALPN negotiation failed. Expected={ApplicationProtocol}, Actual={sslStream.NegotiatedApplicationProtocol}.");
                }

                var reader = PipeReader.Create(sslStream, InboundPipeReaderOptions);
                Exception? readerError = null;
                try
                {
                    await _protocolSession.HandleAsync(Protocol, remoteEndPoint, reader, token);
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
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            catch (Exception e)
            {
                Logger.Error(e, "TCP local listener stream read failed");
            }
        }
    }

    private static async Task CompleteSendAsync(SslStream sslStream, CancellationToken cancellationToken)
    {
        await sslStream.FlushAsync(cancellationToken);
        await sslStream.ShutdownAsync();

        var buffer = new byte[1];
        while (true)
        {
            var read = await sslStream.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }
        }
    }

}
