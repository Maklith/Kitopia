using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Quic;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PluginCore;

namespace Core.Services.DeviceCommunication;

public class DeviceCommunicationService : IDeviceCommunication, IDisposable
{
    private const int DiscoveryPort = 53535;
    private const string MulticastAddress = "239.255.255.250";
    private const string ProtocolId = "kitopia-stream";
    
    private readonly Guid _myId = Guid.NewGuid();
    private readonly string _myName = Environment.MachineName;
    
    private UdpClient? _discoveryUdpClient;
    private CancellationTokenSource? _discoveryCts;
    private QuicListener? _quicListener;
    private UdpClient? _udpDataClient;
    private int _quicPort;
    private int _udpDataPort;
    private X509Certificate2? _serverCert;

    private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> _pendingFileRequests = new();
    private readonly ConcurrentDictionary<Guid, UdpReassemblySession> _udpSessions = new();

    public ObservableCollection<DeviceModel> DiscoveredDevices { get; } = new();

    public event EventHandler<DeviceStreamReceivedEventArgs>? StreamReceived;
    public event EventHandler<string>? MessageReceived;
    public event EventHandler<FileTransferRequestEventArgs>? FileTransferRequested;

    public DeviceCommunicationService()
    {
        _serverCert = GenerateCertificate();
    }

    public void StartDiscovery()
    {
        StopDiscovery();
        _discoveryCts = new CancellationTokenSource();

        // 1. Start QUIC Listener
        StartQuicListener();
        
        // 2. Start UDP Data Listener
        StartUdpDataListener();

        // 3. Start Discovery Broadcast and Listen
        Task.Run(() => DiscoveryLoop(_discoveryCts.Token));
        Task.Run(() => BroadcastLoop(_discoveryCts.Token));
    }

    public void StopDiscovery()
    {
        _discoveryCts?.Cancel();
        _discoveryUdpClient?.Close();
        _quicListener?.DisposeAsync().AsTask().Wait();
        _udpDataClient?.Close();
        _discoveryUdpClient = null;
        _quicListener = null;
        _udpDataClient = null;
    }

    public async Task SendMessageAsync(DeviceModel target, string message)
    {
        var meta = new PacketMetadata 
        { 
            Type = "Message", 
            Content = message,
            SenderPort = _quicPort
        };
        
        var json = JsonSerializer.Serialize(meta);
        using var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
        await SendStreamAsync(target, ms, json);
    }

    public async Task RequestFileTransferAsync(DeviceModel target, string filePath)
    {
        if (!File.Exists(filePath)) throw new FileNotFoundException(filePath);
        
        var fileInfo = new FileInfo(filePath);
        var requestId = Guid.NewGuid().ToString();
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        if (!_pendingFileRequests.TryAdd(requestId, tcs))
            throw new InvalidOperationException("Failed to track request");

        var meta = new PacketMetadata
        {
            Type = "FileReq",
            RequestId = requestId,
            FileName = fileInfo.Name,
            Size = fileInfo.Length,
            SenderPort = _quicPort
        };
        
        try
        {
            var json = JsonSerializer.Serialize(meta);
            using var memoryStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
            await SendStreamAsync(target, memoryStream, json);

            var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromMinutes(1)));
            
            if (completedTask == tcs.Task && tcs.Task.Result)
            {
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                var fileMeta = new PacketMetadata
                {
                    Type = "FileTransfer",
                    RequestId = requestId,
                    FileName = fileInfo.Name,
                    Size = fileInfo.Length,
                    SenderPort = _quicPort
                };
                await SendStreamAsync(target, fs, JsonSerializer.Serialize(fileMeta));
            }
            else
            {
                _pendingFileRequests.TryRemove(requestId, out _);
                if (completedTask != tcs.Task) throw new TimeoutException("User did not respond in time.");
            }
        }
        finally
        {
            _pendingFileRequests.TryRemove(requestId, out _);
        }
    }

    public async Task RespondToFileRequestAsync(DeviceModel target, string requestId, bool accepted)
    {
        var meta = new PacketMetadata
        {
            Type = "FileResp",
            RequestId = requestId,
            Accepted = accepted,
            SenderPort = _quicPort
        };
        var json = JsonSerializer.Serialize(meta);
        using var memoryStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
        await SendStreamAsync(target, memoryStream, json);
    }

    public async Task SendStreamAsync(DeviceModel target, Stream stream, string? metaData = null)
    {
        // Prioritize QUIC
        if (await TrySendQuicAsync(target, stream, metaData))
            return;

        // Fallback to UDP
        await SendUdpAsync(target, stream, metaData);
    }

    private async Task<bool> TrySendQuicAsync(DeviceModel target, Stream stream, string? metaData)
    {
        try
        {
            if (!QuicConnection.IsSupported) return false;

            var endPoint = new IPEndPoint(target.Address, target.Port); // Use QUIC port from discovery?
            // Assuming target.Port is the QUIC port.

            var connectionOptions = new QuicClientConnectionOptions
            {
                RemoteEndPoint = endPoint,
                DefaultStreamErrorCode = 0,
                DefaultCloseErrorCode = 0,
                ClientAuthenticationOptions = new SslClientAuthenticationOptions
                {
                    ApplicationProtocols = new List<SslApplicationProtocol> { new SslApplicationProtocol(ProtocolId) },
                    RemoteCertificateValidationCallback = (_, _, _, _) => true // Self-signed certs
                }
            };

            await using var connection = await QuicConnection.ConnectAsync(connectionOptions);
            await using var quicStream = await connection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional);

            // Send Metadata
            await WriteMetaDataAsync(quicStream, metaData);
            
            // Send Data
            await stream.CopyToAsync(quicStream);
            
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private async Task SendUdpAsync(DeviceModel target, Stream stream, string? metaData)
    {
        // Simple UDP impl with chunking and reassembly support
        using var tempClient = new UdpClient();
        var targetEp = new IPEndPoint(target.Address, target.Port + 1);

        var sessionId = Guid.NewGuid();
        
        // 1. Send Metadata (Offset 0, Type 0)
        var metaBytes = System.Text.Encoding.UTF8.GetBytes(metaData ?? string.Empty);
        await SendUdpPacket(tempClient, targetEp, sessionId, 0, 0, metaBytes, false);
        
        // 2. Send Data
        const int ChunkSize = 4096; // Safe payload size
        var buffer = new byte[ChunkSize];
        int read;
        long offset = 0;

        if (stream.CanSeek) stream.Position = 0;

        while ((read = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
        {
            await SendUdpPacket(tempClient, targetEp, sessionId, offset, 1, buffer.AsSpan(0, read).ToArray(), false);
            offset += read;
            // Throttle slightly to reduce packet loss?
            await Task.Delay(1); 
        }

        // Send End Packet
        await SendUdpPacket(tempClient, targetEp, sessionId, offset, 1, Array.Empty<byte>(), true);
    }
    
    private async Task SendUdpPacket(UdpClient client, IPEndPoint target, Guid sessionId, long offset, byte type, byte[] data, bool isEnd)
    {
        // Header: [SessionId 16][Offset 8][Type 1][IsEnd 1] = 26 bytes
        var packet = new byte[26 + data.Length];
        sessionId.TryWriteBytes(packet.AsSpan(0, 16));
        BitConverter.TryWriteBytes(packet.AsSpan(16, 8), offset);
        packet[24] = type;
        packet[25] = isEnd ? (byte)1 : (byte)0;
        
        if (data.Length > 0)
        {
            data.CopyTo(packet.AsSpan(26));
        }
        
        await client.SendAsync(packet, packet.Length, target);
    }

    private async Task WriteMetaDataAsync(Stream stream, string? metaData)
    {
        var json = metaData ?? string.Empty;
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        var lenBytes = BitConverter.GetBytes(bytes.Length);
        await stream.WriteAsync(lenBytes);
        await stream.WriteAsync(bytes);
    }
    
    private void StartQuicListener()
    {
        if (!QuicListener.IsSupported) return;

        var options = new QuicListenerOptions
        {
            ApplicationProtocols = new List<SslApplicationProtocol> { new SslApplicationProtocol(ProtocolId) },
            ListenEndPoint = new IPEndPoint(IPAddress.Any, 0), // Random port
            ConnectionOptionsCallback = (_, _, _) => ValueTask.FromResult(new QuicServerConnectionOptions
            {
                DefaultStreamErrorCode = 0,
                DefaultCloseErrorCode = 0,
                ServerAuthenticationOptions = new SslServerAuthenticationOptions
                {
                    ApplicationProtocols = new List<SslApplicationProtocol> { new SslApplicationProtocol(ProtocolId) },
                    ServerCertificate = _serverCert
                }
            })
        };

        _quicListener = ListenAsync(options).Result; // Sync start
        _quicPort = _quicListener.LocalEndPoint.Port;
        
        async Task<QuicListener> ListenAsync(QuicListenerOptions opts)
        {
            var listener = await QuicListener.ListenAsync(opts);
            _ = AcceptConnectionsAsync(listener, _discoveryCts!.Token);
            return listener;
        }
    }
    
    private async Task AcceptConnectionsAsync(QuicListener listener, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var connection = await listener.AcceptConnectionAsync(token);
                _ = HandleQuicConnectionAsync(connection);
            }
            catch { break; }
        }
    }

    private void DispatchPacket(PacketMetadata packet, Stream dataStream, DeviceModel sender)
    {
        System.Diagnostics.Debug.WriteLine($"[Dispatch] Processing packet Type={packet.Type}, ID={packet.RequestId}");
        // Dispatch
        switch (packet.Type)
        {
            case "Message":
                MessageReceived?.Invoke(this, packet.Content);
                break;

            case "FileReq":
                FileTransferRequested?.Invoke(
                    this, 
                    new FileTransferRequestEventArgs(packet.RequestId, packet.FileName, packet.Size, sender));
                break;

            case "FileResp":
                if (_pendingFileRequests.TryGetValue(packet.RequestId, out var tcs))
                {
                    tcs.TrySetResult(packet.Accepted);
                }
                break;
                
            case "FileTransfer":
            case "Legacy":
            default:
                System.Diagnostics.Debug.WriteLine($"[Dispatch] Handling Stream for {packet.Type}");
                // For file transfer, we stream the payload.
                Stream resultStream = dataStream;
                if (!dataStream.CanSeek)
                {
                     System.Diagnostics.Debug.WriteLine($"[Dispatch] Buffering stream...");
                     var ms = new MemoryStream();
                     dataStream.CopyTo(ms);
                     ms.Position = 0;
                     resultStream = ms;
                     System.Diagnostics.Debug.WriteLine($"[Dispatch] Buffered {ms.Length} bytes.");
                }
                else
                {
                    // If it is already seekable (MemoryStream from UDP), ensure position is 0
                    if (dataStream.Position != 0) dataStream.Position = 0;
                }
                    
                System.Diagnostics.Debug.WriteLine($"[Dispatch] Invoking StreamReceived event...");
                StreamReceived?.Invoke(this, new DeviceStreamReceivedEventArgs(
                    sender, 
                    resultStream, 
                    JsonSerializer.Serialize(packet))); 
                break;
        }
    }

    private async Task HandleQuicConnectionAsync(QuicConnection connection)
    {
        try
        {
            await using var stream = await connection.AcceptInboundStreamAsync();
            
            // Read Metadata
            var lenBuffer = new byte[4];
            await ReadExactAsync(stream, lenBuffer);
            var len = BitConverter.ToInt32(lenBuffer);
            var metaBuffer = new byte[len];
            await ReadExactAsync(stream, metaBuffer);
            var metaJson = System.Text.Encoding.UTF8.GetString(metaBuffer);
            
            System.Diagnostics.Debug.WriteLine($"[QUIC] Received Metadata: {metaJson}");

            // Try parse as PacketMetadata
            PacketMetadata? packet = null;
            var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            try 
            {
                packet = JsonSerializer.Deserialize<PacketMetadata>(metaJson, jsonOptions);
            }
            catch 
            {
                // Fallback for legacy format: {"Meta": "..."}
                try  
                {
                    var doc = JsonDocument.Parse(metaJson);
                    if (doc.RootElement.TryGetProperty("Meta", out var metaProp))
                    {
                        packet = new PacketMetadata { Type = "Legacy", Meta = metaProp.GetString() ?? "" };
                    }   
                }
                catch { }
            }

            if (packet == null) 
            {
                System.Diagnostics.Debug.WriteLine($"[QUIC] Failed to parse metadata: {metaJson}");
                return;
            }

            var sender = new DeviceModel { Address = connection.RemoteEndPoint.Address, Port = connection.RemoteEndPoint.Port };
            if (packet.SenderPort > 0) sender.Port = packet.SenderPort;
            
            System.Diagnostics.Debug.WriteLine($"[QUIC] Dispatching packet type: {packet.Type} from {sender.Address}:{sender.Port}");
            DispatchPacket(packet, stream, sender);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[QUIC] Handle connection error: {ex}");
        }
    }
    
    private void StartUdpDataListener()
    {
        // Listen on QuicPort + 1 (Fallback convention)
        // Ensure _quicPort is set.
        _udpDataPort = _quicPort + 1;
        _udpDataClient = new UdpClient(new IPEndPoint(IPAddress.Any, _udpDataPort));
        _ = UdpListenLoop(_udpDataClient, _discoveryCts!.Token);
    }
    
    private async Task UdpListenLoop(UdpClient client, CancellationToken token)
    {
        // Periodic cleanup task for stale sessions
        _ = Task.Run(new Func<Task>(async () => 
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(30), token);
                var now = DateTime.UtcNow;
                var stale = _udpSessions.Where(kv => (now - kv.Value.LastActivity).TotalMinutes > 2).Select(kv => kv.Key).ToList();
                foreach (var id in stale)
                {
                    if (_udpSessions.TryRemove(id, out var session))
                    {
                         session.DataStream.Dispose();
                    }
                }
            }
        }), token);

        while (!token.IsCancellationRequested)
        {
            try
            {
                var result = await client.ReceiveAsync(token);
                if (result.Buffer.Length < 26) continue;
                
                var buf = result.Buffer;
                var sessionId = new Guid(buf.AsSpan(0, 16));
                var offset = BitConverter.ToInt64(buf, 16);
                var type = buf[24];
                var isEnd = buf[25] == 1;
                var payloadLen = buf.Length - 26;
                
                var session = _udpSessions.GetOrAdd(sessionId, id => new UdpReassemblySession 
                { 
                    SessionId = id, 
                    Sender = new DeviceModel { Address = result.RemoteEndPoint.Address, Port = result.RemoteEndPoint.Port }
                });
                
                session.LastActivity = DateTime.UtcNow;
                
                if (type == 0) // Metadata
                {
                    if (payloadLen > 0)
                    {
                         var metaStr = System.Text.Encoding.UTF8.GetString(buf, 26, payloadLen);
                         session.MetadataJson = metaStr;
                    }
                }
                else if (type == 1) // Data
                {
                    if (payloadLen > 0)
                    {
                        lock (session)
                        {
                            if (offset < 100 * 1024 * 1024) // 100MB RAM limit guard
                            {
                                if (session.DataStream.Position != offset)
                                    session.DataStream.Seek(offset, SeekOrigin.Begin);
                                session.DataStream.Write(buf, 26, payloadLen);
                            }
                        }
                    }
                }
                
                if (isEnd)
                {
                     if (_udpSessions.TryRemove(sessionId, out var completedSession))
                     {
                         completedSession.DataStream.Position = 0;
                         
                         PacketMetadata? packet = null;
                         if (!string.IsNullOrEmpty(completedSession.MetadataJson))
                         {
                             var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                             try { packet = JsonSerializer.Deserialize<PacketMetadata>(completedSession.MetadataJson, jsonOptions); } catch {}
                         }
                         
                         if (packet != null)
                         {
                             if (packet.SenderPort > 0) completedSession.Sender.Port = packet.SenderPort;
                             DispatchPacket(packet, completedSession.DataStream, completedSession.Sender);
                         }
                     }
                }
            }
            catch { break; }
        }
    }

    private async Task ReadExactAsync(Stream stream, byte[] buffer)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer, offset, buffer.Length - offset);
            if (read == 0) throw new EndOfStreamException();
            offset += read;
        }
    }

    private async Task DiscoveryLoop(CancellationToken token)
    {
        _discoveryUdpClient = new UdpClient();
        _discoveryUdpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _discoveryUdpClient.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
        
        var multicastIp = IPAddress.Parse(MulticastAddress);
        try
        {
            // Try allow loopback for testing
            _discoveryUdpClient.MulticastLoopback = true;

            // Simple join (picks default interface)
            try 
            {
                _discoveryUdpClient.JoinMulticastGroup(multicastIp);
            }
            catch { }

            // Iterate interfaces to ensure we listen on all of them
            foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (networkInterface.OperationalStatus == OperationalStatus.Up &&
                    networkInterface.SupportsMulticast &&
                    networkInterface.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                {
                    var props = networkInterface.GetIPProperties();
                    foreach (var unicast in props.UnicastAddresses)
                    {
                        if (unicast.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            try
                            {
                                _discoveryUdpClient.JoinMulticastGroup(multicastIp, unicast.Address);
                            }
                            catch { }
                        }
                    }
                }
            }
        }
        catch (Exception ex) 
        { 
             System.Diagnostics.Debug.WriteLine($"Multicast Setup Error: {ex}");
        }

        while (!token.IsCancellationRequested)
        {
            try
            {
                var result = await _discoveryUdpClient.ReceiveAsync(token);
                var json = System.Text.Encoding.UTF8.GetString(result.Buffer);
                var info = JsonSerializer.Deserialize<DiscoveryInfo>(json);
                
                if (info != null && !string.IsNullOrEmpty(info.Id))
                {
                    // Allow self-discovery for debug if needed, but usually filtered
                    if (info.Id == _myId.ToString()) continue;

                    _ = Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        var existing = DiscoveredDevices.FirstOrDefault(d => d.Id == info.Id);
                        if (existing == null)
                        {
                            var device = new DeviceModel
                            {
                                Id = info.Id,
                                Name = info.Name,
                                Address = result.RemoteEndPoint.Address,
                                Port = info.Port,
                                LastSeen = DateTime.Now
                            };
                            DiscoveredDevices.Add(device);
                        }
                        else
                        {
                            existing.LastSeen = DateTime.Now;
                            existing.Address = result.RemoteEndPoint.Address;
                            existing.Port = info.Port;
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Discovery Receive Error: {ex}");
            }
        }
    }

    private async Task BroadcastLoop(CancellationToken token)
    {
        var multicastEndpoint = new IPEndPoint(IPAddress.Parse(MulticastAddress), DiscoveryPort);
        
        while (!token.IsCancellationRequested)
        {
            try
            {
                var info = new DiscoveryInfo { Id = _myId.ToString(), Name = _myName, Port = _quicPort };
                var json = JsonSerializer.Serialize(info);
                var bytes = System.Text.Encoding.UTF8.GetBytes(json);

                // Broadcast on all suitable interfaces
                foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (networkInterface.OperationalStatus == OperationalStatus.Up &&
                        networkInterface.SupportsMulticast &&
                        networkInterface.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    {
                        var props = networkInterface.GetIPProperties();
                        foreach (var unicast in props.UnicastAddresses)
                        {
                            if (unicast.Address.AddressFamily == AddressFamily.InterNetwork)
                            {
                                try
                                {
                                    using var client = new UdpClient();
                                    // Bind to the specific interface address to ensure the packet goes out through it
                                    client.Client.Bind(new IPEndPoint(unicast.Address, 0));
                                    client.Ttl = 20; // Increase TTL slightly
                                    await client.SendAsync(bytes, bytes.Length, multicastEndpoint);
                                }
                                catch { }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Broadcast Error: {ex}");
            }
            await Task.Delay(5000, token);
        }
    }

    private X509Certificate2 GenerateCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=KitopiaDevice", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        
        // Key Usage: DigitalSignature is required for TLS 1.3
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, 
            false));
            
        // Enhanced Key Usage: Server Authentication (1.3.6.1.5.5.7.3.1) is required for QUIC/TLS servers
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, 
            false));

        var cert = request.CreateSelfSigned(DateTimeOffset.Now, DateTimeOffset.Now.AddYears(1));
        
        // Export/Import as PFX to ensure the private key is properly associated and accessible for SChannel/MsQuic on Windows
        return new X509Certificate2(cert.Export(X509ContentType.Pfx));
    }
    
    public void Dispose()
    {
        StopDiscovery();
    }
    
    private class UdpReassemblySession
    {
        public Guid SessionId { get; set; }
        public DeviceModel Sender { get; set; } = new();
        public MemoryStream DataStream { get; } = new();
        public string MetadataJson { get; set; } = string.Empty;
        public DateTime LastActivity { get; set; } = DateTime.UtcNow;
    }

    private class DiscoveryInfo
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public int Port { get; set; }
    }

    private class PacketMetadata
    {
        public string Type { get; set; } = "";
        public string Meta { get; set; } = "";
        public string Content { get; set; } = "";
        public long Size { get; set; }
        public string RequestId { get; set; } = "";
        public string FileName { get; set; } = "";
        public bool Accepted { get; set; }
        public int SenderPort { get; set; }
    }
}
