using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Core.Services.DeviceCommunication.Models;
using Core.Services.DeviceCommunication.Protocol;
using PluginCore;

namespace Core.Services.DeviceCommunication.Transport;

public sealed class UdpFallbackTransport : IDisposable
{
    private readonly ConcurrentDictionary<Guid, UdpReassemblySession> _udpSessions = new();

    private UdpClient? _udpDataClientV4;
    private UdpClient? _udpDataClientV6;
    private CancellationToken _token;

    public event EventHandler<TransportPacketReceivedEventArgs>? PacketReceived;

    public int DataPort { get; private set; }

    public void Start(bool quicAvailable, int quicPort, CancellationToken token)
    {
        Stop();
        _token = token;

        if (quicAvailable && quicPort > 0)
        {
            DataPort = quicPort + 1;
            _udpDataClientV4 = new UdpClient(new IPEndPoint(IPAddress.Any, DataPort));
        }
        else
        {
            _udpDataClientV4 = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
            DataPort = ((IPEndPoint)_udpDataClientV4.Client.LocalEndPoint!).Port;
        }

        _ = UdpListenLoop(_udpDataClientV4, token);

        try
        {
            _udpDataClientV6 = new UdpClient(AddressFamily.InterNetworkV6);
            _udpDataClientV6.Client.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, true);
            _udpDataClientV6.Client.Bind(new IPEndPoint(IPAddress.IPv6Any, DataPort));
            _ = UdpListenLoop(_udpDataClientV6, token);
        }
        catch
        {
            _udpDataClientV6 = null;
        }
    }

    public void Stop()
    {
        CloseUdpClient(ref _udpDataClientV4);
        CloseUdpClient(ref _udpDataClientV6);
        DataPort = 0;
        foreach (var session in _udpSessions.Values)
        {
            session.DataStream.Dispose();
        }
        _udpSessions.Clear();
    }

    public async Task SendAsync(
        DeviceModel target,
        PacketMetadata packet,
        Stream payload,
        Action<long>? onProgress = null)
    {
        if (target.Port <= 0)
        {
            throw new InvalidOperationException("目标端口不可用。");
        }

        var targetEndPoint = TransportAddressHelper.CreateTargetEndPoint(target.Address, target.Port + 1);
        using var tempClient = targetEndPoint.AddressFamily == AddressFamily.InterNetworkV6
            ? new UdpClient(AddressFamily.InterNetworkV6)
            : new UdpClient(AddressFamily.InterNetwork);

        var sessionId = Guid.NewGuid();
        var metaJson = JsonSerializer.Serialize(packet);
        var metaBytes = Encoding.UTF8.GetBytes(metaJson);
        await SendUdpPacket(tempClient, targetEndPoint, sessionId, 0, 0, metaBytes, false);

        const int chunkSize = 4096;
        var buffer = new byte[chunkSize];
        var offset = 0L;
        var packetCount = 0;

        if (payload.CanSeek)
        {
            payload.Position = 0;
        }

        int read;
        while ((read = await payload.ReadAsync(buffer.AsMemory(0, buffer.Length))) > 0)
        {
            await SendUdpPacket(
                tempClient,
                targetEndPoint,
                sessionId,
                offset,
                1,
                buffer.AsSpan(0, read).ToArray(),
                false);
            offset += read;
            onProgress?.Invoke(read);
            packetCount++;
            if (packetCount % 10 == 0)
            {
                await Task.Delay(1);
            }
        }

        await SendUdpPacket(tempClient, targetEndPoint, sessionId, offset, 1, Array.Empty<byte>(), true);
    }

    public void Dispose()
    {
        Stop();
    }

    private async Task UdpListenLoop(UdpClient client, CancellationToken token)
    {
        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                var now = DateTime.UtcNow;
                var staleSessionIds = _udpSessions
                    .Where(pair => (now - pair.Value.LastActivity).TotalMinutes > 2)
                    .Select(pair => pair.Key)
                    .ToList();
                foreach (var staleSessionId in staleSessionIds)
                {
                    if (_udpSessions.TryRemove(staleSessionId, out var session))
                    {
                        session.DataStream.Dispose();
                    }
                }
            }
        }, token);

        while (!token.IsCancellationRequested)
        {
            try
            {
                var result = await client.ReceiveAsync(token);
                if (result.Buffer.Length < 26)
                {
                    continue;
                }

                var buf = result.Buffer;
                var sessionId = new Guid(buf.AsSpan(0, 16));
                var offset = BitConverter.ToInt64(buf, 16);
                var type = buf[24];
                var isEnd = buf[25] == 1;
                var payloadLen = buf.Length - 26;

                var session = _udpSessions.GetOrAdd(sessionId, id => new UdpReassemblySession
                {
                    SessionId = id,
                    Sender = new DeviceModel
                    {
                        Address = TransportAddressHelper.NormalizeAddress(result.RemoteEndPoint.Address),
                        Port = result.RemoteEndPoint.Port
                    }
                });
                session.LastActivity = DateTime.UtcNow;

                if (type == 0)
                {
                    if (payloadLen > 0)
                    {
                        session.MetadataJson = Encoding.UTF8.GetString(buf, 26, payloadLen);
                    }
                }
                else if (type == 1)
                {
                    if (payloadLen > 0)
                    {
                        lock (session)
                        {
                            if (offset < 100 * 1024 * 1024)
                            {
                                if (session.DataStream.Position != offset)
                                {
                                    session.DataStream.Seek(offset, SeekOrigin.Begin);
                                }

                                session.DataStream.Write(buf, 26, payloadLen);
                            }
                        }
                    }
                }

                if (!isEnd)
                {
                    continue;
                }

                if (!_udpSessions.TryRemove(sessionId, out var completedSession))
                {
                    continue;
                }

                completedSession.DataStream.Position = 0;
                if (string.IsNullOrWhiteSpace(completedSession.MetadataJson))
                {
                    completedSession.DataStream.Dispose();
                    continue;
                }

                PacketMetadata? packet = null;
                try
                {
                    packet = JsonSerializer.Deserialize<PacketMetadata>(
                        completedSession.MetadataJson,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }
                catch
                {
                }

                if (packet is null)
                {
                    completedSession.DataStream.Dispose();
                    continue;
                }

                if (packet.SenderPort > 0)
                {
                    completedSession.Sender.Port = packet.SenderPort;
                }

                if (!string.IsNullOrWhiteSpace(packet.SenderId))
                {
                    completedSession.Sender.Id = packet.SenderId;
                }

                if (!string.IsNullOrWhiteSpace(packet.SenderName))
                {
                    completedSession.Sender.Name = packet.SenderName.Trim();
                }
                else if (string.IsNullOrWhiteSpace(completedSession.Sender.Name))
                {
                    completedSession.Sender.Name = "未知设备";
                }

                PacketReceived?.Invoke(
                    this,
                    new TransportPacketReceivedEventArgs(packet, completedSession.DataStream, completedSession.Sender));
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                break;
            }
        }
    }

    private static async Task SendUdpPacket(
        UdpClient client,
        IPEndPoint target,
        Guid sessionId,
        long offset,
        byte type,
        byte[] data,
        bool isEnd)
    {
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

    private static void CloseUdpClient(ref UdpClient? client)
    {
        if (client is null)
        {
            return;
        }

        try
        {
            client.Close();
        }
        catch
        {
        }
        finally
        {
            client = null;
        }
    }
}
