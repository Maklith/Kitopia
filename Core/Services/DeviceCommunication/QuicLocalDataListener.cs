using System.IO.Pipelines;
using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Core.Services.Config;
using Core.Services.DeviceCommunication.Discovery;
using Core.Services.DeviceCommunication.Protocol;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using Serilog;

namespace Core.Services.DeviceCommunication;

public sealed class QuicLocalDataListener : ILocalDataTransport
{
    private static readonly ILogger Logger = LogManager.Logger.ForContext<QuicLocalDataListener>();
    private static readonly SslApplicationProtocol ApplicationProtocol = new("kitopia-local-data");

    private readonly object _sync = new();
    private readonly ProtocolSession _protocolSession;
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

    public QuicLocalDataListener(ProtocolSession protocolSession)
    {
        _protocolSession = protocolSession;
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
            _certificate = CreateIdentityCertificate();
        }

        try
        {
            var listenerOptions = new QuicListenerOptions
            {
                ListenEndPoint = new IPEndPoint(IPAddress.IPv6Any, 0),
                ApplicationProtocols = [ApplicationProtocol],
                ConnectionOptionsCallback = (connection, _, _) =>
                {
                    var certificate = _certificate ?? throw new InvalidOperationException("Certificate is not ready.");
                    var expectedRemoteIdentityPublicKey = ResolveExpectedIdentityPublicKey(connection.RemoteEndPoint as IPEndPoint);
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
                                ValidateRemoteCertificate(remoteCertificate, expectedRemoteIdentityPublicKey)
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
        while (true)
        {
            var readResult = await payloadReader.ReadAsync(cancellationToken);
            var buffer = readResult.Buffer;
            foreach (var segment in buffer)
            {
                if (!segment.IsEmpty)
                {
                    await stream.WriteAsync(segment, cancellationToken);
                }
            }

            payloadReader.AdvanceTo(buffer.End);
            if (readResult.IsCompleted)
            {
                break;
            }
        }

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
                    ValidateRemoteCertificate(remoteCertificate, expectedRemoteIdentity)
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

                var pipe = new Pipe();

                async Task ProduceAsync()
                {
                    Exception? producerError = null;
                    try
                    {
                        await CopyStreamToPipeAsync(stream, pipe.Writer, token);
                    }
                    catch (Exception ex)
                    {
                        producerError = ex;
                    }
                    finally
                    {
                        await pipe.Writer.CompleteAsync(producerError);
                    }
                }

                var producerTask = ProduceAsync();
                Exception? consumerError = null;
                try
                {
                    await _protocolSession.HandleAsync(Protocol, remoteIpEndPoint, pipe.Reader, token);
                }
                catch (Exception ex)
                {
                    consumerError = ex;
                    throw;
                }
                finally
                {
                    await pipe.Reader.CompleteAsync(consumerError);
                }

                await producerTask;
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

    private static async Task CopyStreamToPipeAsync(Stream source, PipeWriter writer, CancellationToken token)
    {
        const int BufferSize = 64 * 1024;
        while (true)
        {
            var memory = writer.GetMemory(BufferSize);
            var read = await source.ReadAsync(memory, token);
            if (read == 0)
            {
                break;
            }

            writer.Advance(read);
            var flushResult = await writer.FlushAsync(token);
            if (flushResult.IsCanceled || flushResult.IsCompleted)
            {
                break;
            }
        }
    }

    private static X509Certificate2 CreateIdentityCertificate()
    {
        var privateKey = ConfigManger.Config.devicePrivateKey?.Trim();
        if (string.IsNullOrWhiteSpace(privateKey))
        {
            throw new InvalidOperationException("Device identity private key is not initialized.");
        }

        using var rsa = RSA.Create();
        rsa.ImportPkcs8PrivateKey(Convert.FromBase64String(privateKey), out _);
        var request = new CertificateRequest(
            "CN=Kitopia-Local-Quic",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, false));
        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                [new Oid("1.3.6.1.5.5.7.3.1")],
                false));
        request.CertificateExtensions.Add(
            new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

        var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        return new X509Certificate2(certificate.Export(X509ContentType.Pfx));
    }

    private static string? ResolveExpectedIdentityPublicKey(IPEndPoint? remoteEndPoint)
    {
        if (remoteEndPoint is null)
        {
            return null;
        }

        var discoveryService = ServiceManager.Services.GetService<IDeviceDiscoveryService>();
        if (discoveryService is null)
        {
            return null;
        }

        var remoteAddress = NormalizeAddress(remoteEndPoint.Address);
        var matchedDevice = discoveryService.Devices.FirstOrDefault(device =>
            NormalizeAddress(device.Ipv4Address).Equals(remoteAddress) ||
            NormalizeAddress(device.Ipv6Address).Equals(remoteAddress));

        return matchedDevice is null || string.IsNullOrWhiteSpace(matchedDevice.Id)
            ? null
            : matchedDevice.Id;
    }

    private static bool ValidateRemoteCertificate(X509Certificate? certificate, string? expectedIdentityPublicKey)
    {
        if (certificate is null || string.IsNullOrWhiteSpace(expectedIdentityPublicKey))
        {
            return false;
        }

        if (!TryGetCertificateIdentityPublicKey(certificate, out var certificateIdentityPublicKey))
        {
            return false;
        }

        return string.Equals(certificateIdentityPublicKey, expectedIdentityPublicKey.Trim(), StringComparison.Ordinal);
    }

    private static bool TryGetCertificateIdentityPublicKey(X509Certificate certificate, out string publicKey)
    {
        publicKey = string.Empty;
        try
        {
            using var certificate2 = certificate as X509Certificate2 ?? new X509Certificate2(certificate);
            using var rsa = certificate2.GetRSAPublicKey();
            if (rsa is null)
            {
                return false;
            }

            publicKey = Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo());
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static IPAddress NormalizeAddress(IPAddress address)
    {
        return address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
    }
}
