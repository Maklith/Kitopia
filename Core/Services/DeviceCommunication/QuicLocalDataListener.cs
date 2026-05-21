using System.IO.Pipelines;
using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Core.Services.DeviceCommunication.Protocol;
using Core.Services.DeviceCommunication.Security;
using Serilog;

namespace Core.Services.DeviceCommunication;

public sealed class QuicLocalDataListener : ILocalDataTransport
{
    private static readonly ILogger Logger = LogManager.Logger.ForContext<QuicLocalDataListener>();
    private static readonly SslApplicationProtocol ApplicationProtocol = new("kitopia-local-data");
    private static readonly StreamPipeReaderOptions InboundPipeReaderOptions = new(
        bufferSize: 256 * 1024,
        minimumReadSize: 64 * 1024,
        leaveOpen: true);

    private readonly object _sync = new();
    private readonly ProtocolSession _protocolSession;
    private readonly DeviceTransportSecurity _transportSecurity;
    private int _port;

    private QuicListener? _listener;
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

    public QuicLocalDataListener(ProtocolSession protocolSession, DeviceTransportSecurity transportSecurity)
    {
        _protocolSession = protocolSession;
        _transportSecurity = transportSecurity;
    }

    public bool IsRunning { get; private set; }
    public LocalDataTransportProtocol Protocol => LocalDataTransportProtocol.Quic;


    public async Task<bool> StartAsync(CancellationToken cancellationToken = default)
    {
        if (!QuicConnection.IsSupported || !QuicListener.IsSupported)
        {
            Logger.Information("QUIC local listener skipped because QUIC is not supported.");
            return false;
        }

        lock (_sync)
        {
            if (IsRunning)
            {
                return true;
            }

            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _certificate = _transportSecurity.CreateIdentityCertificate("CN=Kitopia-Local-Quic");
        }

        try
        {
            var listenerOptions = new QuicListenerOptions
            {
                //https://source.dot.net/#System.Net.Quic/System/Net/Quic/QuicListener.cs,152
                //Using the Unspecified family makes MsQuic handle connections from all IP addresses.
                ListenEndPoint = new IPEndPoint(IPAddress.IPv6Any, 0),
                ApplicationProtocols = [ApplicationProtocol],
                ConnectionOptionsCallback = (connection, _, _) =>
                {
                    var certificate = _certificate ?? throw new InvalidOperationException("Certificate is not ready.");
                    var expectedRemoteIdentityPublicKey = _transportSecurity.ResolveExpectedIdentityPublicKey(connection.RemoteEndPoint);
                    return ValueTask.FromResult(new QuicServerConnectionOptions
                    {
                        DefaultCloseErrorCode = 0,
                        DefaultStreamErrorCode = 0,
                        ServerAuthenticationOptions = new SslServerAuthenticationOptions
                        {
                            ApplicationProtocols = [ApplicationProtocol],
                            ServerCertificate = certificate,
                            EnabledSslProtocols = SslProtocols.Tls13,
                            ClientCertificateRequired = true,
                            RemoteCertificateValidationCallback = (_, remoteCertificate, _, _) =>
                                _transportSecurity.ValidateRemoteCertificate(remoteCertificate, expectedRemoteIdentityPublicKey)
                        }
                    });
                }
            };

            var listener = await QuicListener.ListenAsync(listenerOptions, _cts!.Token);
            if (listener.LocalEndPoint is not IPEndPoint localEndPoint)
            {
                await listener.DisposeAsync();
                throw new InvalidOperationException("Failed to resolve local QUIC endpoint.");
            }

            lock (_sync)
            {
                _listener = listener;
                _port = localEndPoint.Port;
                _acceptTask = Task.Run(() => AcceptLoop(listener, _cts.Token), _cts.Token);
                IsRunning = true;
            }

            Logger.Information("QUIC local listener started on {Port}", Port);
            return true;
        }
        catch (Exception e)
        {
            Logger.Error(e, "QUIC local listener start failed");
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

        var connectionOptions = CreateClientConnectionOptions(remoteEndPoint, remoteIdentityPublicKey.Trim());

        await using var connection = await QuicConnection.ConnectAsync(connectionOptions, cancellationToken);
        await using var stream = await connection.OpenOutboundStreamAsync(QuicStreamType.Unidirectional, cancellationToken);
        await stream.WriteAsync(payload, cancellationToken);
        stream.CompleteWrites();
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

        var connectionOptions = CreateClientConnectionOptions(remoteEndPoint, remoteIdentityPublicKey.Trim());
        await using var connection = await QuicConnection.ConnectAsync(connectionOptions, cancellationToken);
        await using var stream = await connection.OpenOutboundStreamAsync(QuicStreamType.Unidirectional, cancellationToken);
        await payloadReader.CopyToAsync(stream, cancellationToken);

        stream.CompleteWrites();
    }

    public async Task StopAsync()
    {
        Task? acceptTask;
        QuicListener? listener;

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

        if (listener is not null)
        {
            await listener.DisposeAsync().ConfigureAwait(false);
        }

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

        Logger.Information("QUIC local listener stopped");
    }

    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
    }

    private QuicClientConnectionOptions CreateClientConnectionOptions(IPEndPoint remoteEndPoint, string expectedRemoteIdentity)
    {
        X509Certificate2? localCertificate;
        lock (_sync)
        {
            localCertificate = _certificate;
        }

        if (localCertificate is null)
        {
            throw new InvalidOperationException("QUIC local certificate is not ready.");
        }

        return new QuicClientConnectionOptions
        {
            RemoteEndPoint = remoteEndPoint,
            DefaultCloseErrorCode = 0,
            DefaultStreamErrorCode = 0,
            ClientAuthenticationOptions = new SslClientAuthenticationOptions
            {
                ApplicationProtocols = [ApplicationProtocol],
                TargetHost = "Kitopia-Local-Quic",
                EnabledSslProtocols = SslProtocols.Tls13,
                ClientCertificates = new X509CertificateCollection { localCertificate },
                RemoteCertificateValidationCallback = (_, remoteCertificate, _, _) =>
                    _transportSecurity.ValidateRemoteCertificate(remoteCertificate, expectedRemoteIdentity)
            }
        };
    }

    private async Task AcceptLoop(QuicListener listener, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var connection = await listener.AcceptConnectionAsync(token);
                _ = Task.Run(() => HandleConnectionAsync(connection, token), token);
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
                Logger.Error(e, "QUIC local listener accept failed");
            }
        }
    }

    private async Task HandleConnectionAsync(QuicConnection connection, CancellationToken token)
    {
        await using (connection)
        {
            var streamTasks = new List<Task>();
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (streamTasks.Count > 0)
                    {
                        streamTasks.RemoveAll(task => task.IsCompleted);
                    }

                    var stream = await connection.AcceptInboundStreamAsync(token);
                    var streamTask = Task.Run(() => HandleStreamAsync(stream, connection.RemoteEndPoint, token), token);
                    streamTasks.Add(streamTask);
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
                    if (e is QuicException { QuicError: QuicError.ConnectionAborted or QuicError.OperationAborted })
                    {
                        break;
                    }

                    Logger.Error(e, "QUIC local listener stream accept failed");
                    break;
                }
            }

            if (streamTasks.Count > 0)
            {
                try
                {
                    await Task.WhenAll(streamTasks);
                }
                catch
                {
                }
            }
        }
    }

    private async Task HandleStreamAsync(QuicStream stream, EndPoint remoteEndPoint, CancellationToken token)
    {
        await using (stream)
        {
            try
            {
                if (remoteEndPoint is not IPEndPoint remoteIpEndPoint)
                {
                    return;
                }

                var reader = PipeReader.Create(stream, InboundPipeReaderOptions);
                Exception? readerError = null;
                try
                {
                    await _protocolSession.HandleAsync(Protocol, remoteIpEndPoint, reader, token);
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
                Logger.Error(e, "QUIC local listener stream read failed");
            }
        }
    }

}
