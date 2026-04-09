using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Quic;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Core.Services.DeviceCommunication.Protocol;
using PluginCore;

namespace Core.Services.DeviceCommunication.Transport;

public sealed class QuicTransport : IDisposable
{
    private const string ProtocolId = "kitopia-stream";

    private QuicListener? _listener;
    private readonly X509Certificate2 _serverCertificate;
    private CancellationToken _token;

    public QuicTransport()
    {
        _serverCertificate = GenerateCertificate();
    }

    public event EventHandler<TransportPacketReceivedEventArgs>? PacketReceived;

    public int Port { get; private set; }
    public bool IsRunning => _listener is not null;

    public bool Start(CancellationToken token)
    {
        if (!QuicListener.IsSupported)
        {
            Port = 0;
            return false;
        }

        try
        {
            var options = new QuicListenerOptions
            {
                ApplicationProtocols = [new SslApplicationProtocol(ProtocolId)],
                ListenEndPoint = new System.Net.IPEndPoint(System.Net.IPAddress.IPv6Any, 0),
                ConnectionOptionsCallback = (_, _, _) => ValueTask.FromResult(new QuicServerConnectionOptions
                {
                    DefaultStreamErrorCode = 0,
                    DefaultCloseErrorCode = 0,
                    ServerAuthenticationOptions = new SslServerAuthenticationOptions
                    {
                        ApplicationProtocols = [new SslApplicationProtocol(ProtocolId)],
                        ServerCertificate = _serverCertificate
                    }
                })
            };

            _listener = QuicListener.ListenAsync(options).AsTask().GetAwaiter().GetResult();
            Port = _listener.LocalEndPoint.Port;
            _token = token;
            _ = AcceptConnectionsAsync(_listener, token);
            return true;
        }
        catch
        {
            _listener = null;
            Port = 0;
            return false;
        }
    }

    public async Task StopAsync()
    {
        var listener = _listener;
        _listener = null;
        Port = 0;
        if (listener is null)
        {
            return;
        }

        try
        {
            await listener.DisposeAsync();
        }
        catch
        {
        }
    }

    public async Task<bool> TrySendAsync(
        DeviceModel target,
        PacketMetadata packet,
        Stream payload,
        Action<long>? onProgress = null)
    {
        try
        {
            if (!QuicConnection.IsSupported || target.Port <= 0)
            {
                return false;
            }

            var endPoint = TransportAddressHelper.CreateTargetEndPoint(target.Address, target.Port);
            var connectionOptions = new QuicClientConnectionOptions
            {
                RemoteEndPoint = endPoint,
                DefaultStreamErrorCode = 0,
                DefaultCloseErrorCode = 0,
                ClientAuthenticationOptions = new SslClientAuthenticationOptions
                {
                    ApplicationProtocols = [new SslApplicationProtocol(ProtocolId)],
                    RemoteCertificateValidationCallback = (_, _, _, _) => true
                }
            };

            using var connectCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await using var connection = await QuicConnection.ConnectAsync(connectionOptions, connectCts.Token);
            using var streamCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await using var quicStream = await connection.OpenOutboundStreamAsync(
                QuicStreamType.Bidirectional,
                streamCts.Token);

            await WriteMetaDataAsync(quicStream, JsonSerializer.Serialize(packet), TimeSpan.FromSeconds(2));
            await CopyStreamWithTimeoutAsync(payload, quicStream, TimeSpan.FromSeconds(10), onProgress: onProgress);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        try
        {
            StopAsync().GetAwaiter().GetResult();
        }
        catch
        {
        }
    }

    private async Task AcceptConnectionsAsync(QuicListener listener, CancellationToken token)
    {
        while (!token.IsCancellationRequested && ReferenceEquals(_listener, listener))
        {
            try
            {
                var connection = await listener.AcceptConnectionAsync(token);
                _ = HandleConnectionAsync(connection);
            }
            catch
            {
                break;
            }
        }
    }

    private async Task HandleConnectionAsync(QuicConnection connection)
    {
        try
        {
            using var streamCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await using var stream = await connection.AcceptInboundStreamAsync(streamCts.Token);

            var lenBuffer = new byte[4];
            await ReadExactAsync(stream, lenBuffer, TimeSpan.FromSeconds(5));
            var len = BitConverter.ToInt32(lenBuffer);
            var metaBuffer = new byte[len];
            await ReadExactAsync(stream, metaBuffer, TimeSpan.FromSeconds(5));
            var metaJson = Encoding.UTF8.GetString(metaBuffer);

            PacketMetadata? packet = null;
            var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            try
            {
                packet = JsonSerializer.Deserialize<PacketMetadata>(metaJson, jsonOptions);
            }
            catch
            {
                try
                {
                    var doc = JsonDocument.Parse(metaJson);
                    if (doc.RootElement.TryGetProperty("Meta", out var metaProp))
                    {
                        packet = new PacketMetadata { Type = PacketTypes.Legacy, Meta = metaProp.GetString() ?? string.Empty };
                    }
                }
                catch
                {
                }
            }

            if (packet is null)
            {
                return;
            }

            var sender = new DeviceModel
            {
                Address = TransportAddressHelper.NormalizeAddress(connection.RemoteEndPoint.Address),
                Port = connection.RemoteEndPoint.Port
            };

            if (packet.SenderPort > 0)
            {
                sender.Port = packet.SenderPort;
            }

            if (!string.IsNullOrWhiteSpace(packet.SenderId))
            {
                sender.Id = packet.SenderId;
            }

            if (!string.IsNullOrWhiteSpace(packet.SenderName))
            {
                sender.Name = packet.SenderName.Trim();
            }
            else if (string.IsNullOrWhiteSpace(sender.Name))
            {
                sender.Name = "未知设备";
            }

            PacketReceived?.Invoke(this, new TransportPacketReceivedEventArgs(packet, stream, sender));
        }
        catch
        {
        }
        finally
        {
            try
            {
                await connection.DisposeAsync();
            }
            catch
            {
            }
        }
    }

    private static async Task ReadExactAsync(Stream stream, byte[] buffer, TimeSpan timeout)
    {
        var offset = 0;
        using var cts = new CancellationTokenSource();
        while (offset < buffer.Length)
        {
            cts.CancelAfter(timeout);
            try
            {
                var read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), cts.Token);
                if (read == 0)
                {
                    throw new EndOfStreamException();
                }

                offset += read;
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException("读取数据超时。");
            }
        }
    }

    private static async Task WriteMetaDataAsync(Stream stream, string metaData, TimeSpan timeout)
    {
        var bytes = Encoding.UTF8.GetBytes(metaData);
        var lenBytes = BitConverter.GetBytes(bytes.Length);
        using var cts = new CancellationTokenSource();
        try
        {
            cts.CancelAfter(timeout);
            await stream.WriteAsync(lenBytes, cts.Token);
            cts.CancelAfter(timeout);
            await stream.WriteAsync(bytes, cts.Token);
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException("写入元数据超时。");
        }
    }

    private static async Task CopyStreamWithTimeoutAsync(
        Stream source,
        Stream destination,
        TimeSpan timeout,
        int bufferSize = 8192,
        Action<long>? onProgress = null)
    {
        var buffer = new byte[bufferSize];
        using var cts = new CancellationTokenSource();

        while (true)
        {
            int bytesRead;
            try
            {
                cts.CancelAfter(timeout);
                bytesRead = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cts.Token);
                if (bytesRead == 0)
                {
                    break;
                }

                cts.CancelAfter(timeout);
                await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cts.Token);
                onProgress?.Invoke(bytesRead);
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException("数据传输超时。");
            }
        }
    }

    private static X509Certificate2 GenerateCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=KitopiaDevice", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                false));
        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                new OidCollection { new("1.3.6.1.5.5.7.3.1") },
                false));

        var cert = request.CreateSelfSigned(DateTimeOffset.Now, DateTimeOffset.Now.AddYears(1));
        return new X509Certificate2(cert.Export(X509ContentType.Pfx));
    }
}
