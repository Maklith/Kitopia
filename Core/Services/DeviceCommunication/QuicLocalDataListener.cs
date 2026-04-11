using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Core.Services;
using Serilog;

namespace Core.Services.DeviceCommunication;

public sealed class QuicLocalDataListener : IDisposable
{
    private static readonly ILogger Logger = LogManager.Logger.ForContext<QuicLocalDataListener>();
    private static readonly SslApplicationProtocol ApplicationProtocol = new("kitopia-local-data");

    private readonly object _sync = new();
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

    public bool IsRunning { get; private set; }


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
            _certificate = CreateSelfSignedCertificate();
        }

        try
        {
            var listenerOptions = new QuicListenerOptions
            {
                ListenEndPoint = new IPEndPoint(IPAddress.Loopback, 0),
                ApplicationProtocols = [ApplicationProtocol],
                ConnectionOptionsCallback = (_, _, _) =>
                {
                    var certificate = _certificate ?? throw new InvalidOperationException("Certificate is not ready.");
                    return ValueTask.FromResult(new QuicServerConnectionOptions
                    {
                        DefaultCloseErrorCode = 0,
                        DefaultStreamErrorCode = 0,
                        ServerAuthenticationOptions = new SslServerAuthenticationOptions
                        {
                            ApplicationProtocols = [ApplicationProtocol],
                            ServerCertificate = certificate,
                            EnabledSslProtocols = SslProtocols.Tls13
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
            await StopAsync();
            return false;
        }
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
            await listener.DisposeAsync();
        }

        if (acceptTask is not null)
        {
            try
            {
                await acceptTask;
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
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var stream = await connection.AcceptInboundStreamAsync(token);
                    _ = Task.Run(() => HandleStreamAsync(stream, connection.RemoteEndPoint, token), token);
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
                    Logger.Error(e, "QUIC local listener stream accept failed");
                    break;
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
                using var memory = new MemoryStream();
                var buffer = new byte[8192];

                while (true)
                {
                    var read = await stream.ReadAsync(buffer, token);
                    if (read == 0)
                    {
                        break;
                    }

                    memory.Write(buffer, 0, read);
                }

                if (memory.Length > 0)
                {
                    
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

    private static X509Certificate2 CreateSelfSignedCertificate()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest(
            "CN=Kitopia-Local-Quic",
            ecdsa,
            HashAlgorithmName.SHA256);

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
}
