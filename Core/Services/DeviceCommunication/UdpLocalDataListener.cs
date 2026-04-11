using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Core.Services;
using Serilog;

namespace Core.Services.DeviceCommunication;

public sealed class UdpLocalDataListener : ILocalDataTransport
{
    private const int DatagramMagic = 0x4B544450; // KTDP
    private const byte DatagramVersion = 1;
    private const byte PacketTypeData = 1;
    private const byte PacketTypeTransportAck = 2;
    private const byte PacketTypeHandshakeHello = 3;
    private const byte PacketTypeHandshakeAck = 4;
    private const byte FlagAckRequired = 1 << 0;
    private const int HeaderLength = 39;
    private const int MaxChunkPayloadSize = 1024;
    private const int ReliableMaxRetryCount = 2;
    private const int HandshakeMaxRetryCount = 2;
    private const byte EncryptedPayloadVersion = 1;
    private const int AesNonceLength = 12;
    private const int AesTagLength = 16;

    private static readonly TimeSpan ReliableAckTimeout = TimeSpan.FromMilliseconds(800);
    private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromMilliseconds(800);
    private static readonly TimeSpan PendingMessageTtl = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan CompletedMessageTtl = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan SessionKeyTtl = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan PendingCleanupInterval = TimeSpan.FromSeconds(3);

    private static readonly ILogger Logger = LogManager.Logger.ForContext<UdpLocalDataListener>();
    private static readonly byte[] KeyDerivationLabel = "kitopia-udp-aesgcm-v1"u8.ToArray();

    private readonly object _sync = new();
    private readonly object _pendingSync = new();
    private readonly ConcurrentDictionary<UdpMessageKey, TaskCompletionSource<bool>> _pendingAcks = [];
    private readonly ConcurrentDictionary<UdpMessageKey, TaskCompletionSource<byte[]>> _pendingHandshakeAcks = [];
    private readonly Dictionary<UdpMessageKey, UdpPendingMessage> _pendingMessages = [];
    private readonly Dictionary<UdpMessageKey, DateTime> _completedMessages = [];
    private readonly Dictionary<UdpRemoteKey, UdpSessionKey> _sessionKeys = [];

    private int _port;
    private DateTime _lastPendingCleanupUtc = DateTime.UtcNow;

    private UdpClient? _udpClient;
    private CancellationTokenSource? _cts;
    private Task? _receiveTask;
    private ECDiffieHellman? _ecdh;
    private byte[] _localPublicKey = [];

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
    public LocalDataTransportProtocol Protocol => LocalDataTransportProtocol.Udp;
    public event LocalDataPacketReceivedHandler? PacketReceived;

    public Task<bool> StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (IsRunning)
            {
                return Task.FromResult(true);
            }

            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _udpClient = new UdpClient(AddressFamily.InterNetwork);
            _udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _udpClient.Client.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            if (_udpClient.Client.LocalEndPoint is not IPEndPoint localEndPoint)
            {
                throw new InvalidOperationException("Failed to resolve local UDP endpoint.");
            }

            _ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
            _localPublicKey = _ecdh.ExportSubjectPublicKeyInfo();

            _port = localEndPoint.Port;
            _receiveTask = Task.Run(() => ReceiveLoop(_udpClient, _cts.Token), _cts.Token);
            IsRunning = true;
        }

        Logger.Information("UDP local listener started on {Port}", Port);
        return Task.FromResult(true);
    }

    public async Task SendAsync(
        ReadOnlyMemory<byte> payload,
        IPEndPoint remoteEndPoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(remoteEndPoint);
        if (payload.IsEmpty)
        {
            return;
        }

        UdpClient client;
        lock (_sync)
        {
            if (!IsRunning || _udpClient is null)
            {
                throw new InvalidOperationException("UDP local listener is not running.");
            }

            client = _udpClient;
        }

        UdpMessageKey ackKey;
        TaskCompletionSource<bool> ackWaiter;
        while (true)
        {
            var messageId = Guid.NewGuid();
            ackKey = new UdpMessageKey(remoteEndPoint.Address, remoteEndPoint.Port, messageId);
            ackWaiter = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (_pendingAcks.TryAdd(ackKey, ackWaiter))
            {
                break;
            }
        }

        var remoteKey = new UdpRemoteKey(remoteEndPoint.Address, remoteEndPoint.Port);
        var attemptCount = ReliableMaxRetryCount + 1;
        try
        {
            for (var attempt = 0; attempt < attemptCount; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (attempt > 0)
                {
                    InvalidateSessionKey(remoteKey);
                }

                var sessionKey = await EnsureSessionKeyAsync(client, remoteEndPoint, cancellationToken);
                var encryptedPayload = EncryptPayload(payload.Span, sessionKey);
                var datagrams = BuildDataDatagrams(encryptedPayload, ackKey.MessageId);
                await SendDatagramsAsync(client, datagrams, remoteEndPoint, cancellationToken);

                try
                {
                    await ackWaiter.Task.WaitAsync(ReliableAckTimeout, cancellationToken);
                    return;
                }
                catch (TimeoutException) when (attempt + 1 < attemptCount)
                {
                    Logger.Warning(
                        "UDP reliable send timed out waiting ACK from {RemoteEndPoint}, retry {Retry}/{TotalRetry}",
                        remoteEndPoint,
                        attempt + 1,
                        ReliableMaxRetryCount);
                }
            }
        }
        finally
        {
            _pendingAcks.TryRemove(ackKey, out _);
        }

        throw new TimeoutException($"UDP reliable send failed to receive ACK from {remoteEndPoint}.");
    }

    private async Task<byte[]> EnsureSessionKeyAsync(
        UdpClient client,
        IPEndPoint remoteEndPoint,
        CancellationToken cancellationToken)
    {
        if (TryGetSessionKey(remoteEndPoint, out var existingKey))
        {
            return existingKey;
        }

        var localPublicKey = GetLocalPublicKey();
        var attemptCount = HandshakeMaxRetryCount + 1;
        for (var attempt = 0; attempt < attemptCount; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            UdpMessageKey handshakeKey;
            TaskCompletionSource<byte[]> waiter;
            while (true)
            {
                var messageId = Guid.NewGuid();
                handshakeKey = new UdpMessageKey(remoteEndPoint.Address, remoteEndPoint.Port, messageId);
                waiter = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
                if (_pendingHandshakeAcks.TryAdd(handshakeKey, waiter))
                {
                    break;
                }
            }

            try
            {
                var helloDatagram = BuildHandshakeDatagram(PacketTypeHandshakeHello, handshakeKey.MessageId, localPublicKey);
                await client.SendAsync(helloDatagram, helloDatagram.Length, remoteEndPoint);

                var remotePublicKey = await waiter.Task.WaitAsync(HandshakeTimeout, cancellationToken);
                var sessionKey = DeriveSessionKey(remotePublicKey);
                SetSessionKey(new UdpRemoteKey(remoteEndPoint.Address, remoteEndPoint.Port), sessionKey);
                return sessionKey;
            }
            catch (TimeoutException) when (attempt + 1 < attemptCount)
            {
                Logger.Warning(
                    "UDP handshake timed out for {RemoteEndPoint}, retry {Retry}/{TotalRetry}",
                    remoteEndPoint,
                    attempt + 1,
                    HandshakeMaxRetryCount);
            }
            finally
            {
                _pendingHandshakeAcks.TryRemove(handshakeKey, out _);
            }
        }

        throw new TimeoutException($"UDP key handshake failed with {remoteEndPoint}.");
    }

    public async Task StopAsync()
    {
        Task? receiveTask;

        lock (_sync)
        {
            if (!IsRunning)
            {
                return;
            }

            IsRunning = false;
            _cts?.Cancel();
            _udpClient?.Close();
            receiveTask = _receiveTask;
            _receiveTask = null;
        }

        if (receiveTask is not null)
        {
            try
            {
                await receiveTask;
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
            _udpClient?.Dispose();
            _udpClient = null;
            _cts?.Dispose();
            _cts = null;
            _port = 0;
            _ecdh?.Dispose();
            _ecdh = null;
            _localPublicKey = [];
        }

        lock (_pendingSync)
        {
            _pendingMessages.Clear();
            _completedMessages.Clear();
            foreach (var session in _sessionKeys.Values)
            {
                CryptographicOperations.ZeroMemory(session.Key);
            }

            _sessionKeys.Clear();
        }

        foreach (var pendingAck in _pendingAcks)
        {
            pendingAck.Value.TrySetCanceled();
        }

        foreach (var pendingHandshakeAck in _pendingHandshakeAcks)
        {
            pendingHandshakeAck.Value.TrySetCanceled();
        }

        _pendingAcks.Clear();
        _pendingHandshakeAcks.Clear();

        Logger.Information("UDP local listener stopped");
    }

    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
    }

    private async Task ReceiveLoop(UdpClient client, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var result = await client.ReceiveAsync(token);
                await ProcessDatagramAsync(client, result.Buffer, result.RemoteEndPoint, token);
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
                Logger.Error(e, "UDP local listener receive failed");
            }
        }
    }

    private async Task ProcessDatagramAsync(
        UdpClient client,
        byte[] datagram,
        IPEndPoint remoteEndPoint,
        CancellationToken token)
    {
        if (!TryParseDatagram(
                datagram,
                out var packetType,
                out var flags,
                out var messageId,
                out var chunkIndex,
                out var totalChunkCount,
                out var payloadOffset,
                out var payloadLength,
                out var payloadChecksum))
        {
            return;
        }

        var messageKey = new UdpMessageKey(remoteEndPoint.Address, remoteEndPoint.Port, messageId);
        if (packetType == PacketTypeTransportAck)
        {
            if (_pendingAcks.TryRemove(messageKey, out var ackWaiter))
            {
                ackWaiter.TrySetResult(true);
            }

            return;
        }

        var payloadSpan = datagram.AsSpan(payloadOffset, payloadLength);
        if (Crc32.Compute(payloadSpan) != payloadChecksum)
        {
            Logger.Warning("UDP local listener dropped corrupted datagram from {RemoteEndPoint}", remoteEndPoint);
            return;
        }

        if (packetType == PacketTypeHandshakeHello)
        {
            await HandleHandshakeHelloAsync(client, remoteEndPoint, messageId, payloadSpan.ToArray());
            return;
        }

        if (packetType == PacketTypeHandshakeAck)
        {
            HandleHandshakeAck(remoteEndPoint, messageId, payloadSpan);
            return;
        }

        var nowUtc = DateTime.UtcNow;
        var requireAck = (flags & FlagAckRequired) != 0;
        UdpPendingMessage? completedMessage = null;
        var isDuplicate = false;
        lock (_pendingSync)
        {
            CleanupPendingStateUnsafe(nowUtc);

            if (!_pendingMessages.TryGetValue(messageKey, out var pendingMessage) ||
                pendingMessage.ChunkCount != totalChunkCount)
            {
                pendingMessage = new UdpPendingMessage(totalChunkCount);
                _pendingMessages[messageKey] = pendingMessage;
            }

            pendingMessage.UpdatedUtc = nowUtc;
            if (!pendingMessage.TryAddChunk(chunkIndex, payloadSpan))
            {
                return;
            }

            if (!pendingMessage.IsComplete)
            {
                return;
            }

            _pendingMessages.Remove(messageKey);
            isDuplicate = _completedMessages.TryGetValue(messageKey, out var completedUtc) &&
                          nowUtc - completedUtc <= CompletedMessageTtl;
            if (!isDuplicate)
            {
                completedMessage = pendingMessage;
            }
        }

        if (isDuplicate)
        {
            if (requireAck)
            {
                await SendTransportAckAsync(client, remoteEndPoint, messageId);
            }

            return;
        }

        if (completedMessage is null)
        {
            return;
        }

        var encryptedPayload = completedMessage.AssemblePayload();
        if (!TryGetSessionKey(remoteEndPoint, out var sessionKey))
        {
            await TrySendHandshakeHelloAsync(client, remoteEndPoint);
            return;
        }

        if (!TryDecryptPayload(encryptedPayload, sessionKey, out var plainPayload))
        {
            InvalidateSessionKey(new UdpRemoteKey(remoteEndPoint.Address, remoteEndPoint.Port));
            await TrySendHandshakeHelloAsync(client, remoteEndPoint);
            return;
        }

        lock (_pendingSync)
        {
            _completedMessages[messageKey] = nowUtc;
        }

        if (requireAck)
        {
            await SendTransportAckAsync(client, remoteEndPoint, messageId);
        }

        await PublishPacketAsync(plainPayload, remoteEndPoint, token);
    }

    private async Task HandleHandshakeHelloAsync(
        UdpClient client,
        IPEndPoint remoteEndPoint,
        Guid messageId,
        byte[] remotePublicKey)
    {
        try
        {
            var sessionKey = DeriveSessionKey(remotePublicKey);
            SetSessionKey(new UdpRemoteKey(remoteEndPoint.Address, remoteEndPoint.Port), sessionKey);

            var localPublicKey = GetLocalPublicKey();
            var ackDatagram = BuildHandshakeDatagram(PacketTypeHandshakeAck, messageId, localPublicKey);
            await client.SendAsync(ackDatagram, ackDatagram.Length, remoteEndPoint);
        }
        catch (Exception e)
        {
            Logger.Warning(e, "UDP handshake HELLO processing failed for {RemoteEndPoint}", remoteEndPoint);
        }
    }

    private void HandleHandshakeAck(IPEndPoint remoteEndPoint, Guid messageId, ReadOnlySpan<byte> remotePublicKey)
    {
        var messageKey = new UdpMessageKey(remoteEndPoint.Address, remoteEndPoint.Port, messageId);
        if (_pendingHandshakeAcks.TryRemove(messageKey, out var waiter))
        {
            waiter.TrySetResult(remotePublicKey.ToArray());
            return;
        }

        try
        {
            var sessionKey = DeriveSessionKey(remotePublicKey);
            SetSessionKey(new UdpRemoteKey(remoteEndPoint.Address, remoteEndPoint.Port), sessionKey);
        }
        catch (Exception e)
        {
            Logger.Warning(e, "UDP handshake ACK processing failed for {RemoteEndPoint}", remoteEndPoint);
        }
    }

    private async Task TrySendHandshakeHelloAsync(UdpClient client, IPEndPoint remoteEndPoint)
    {
        try
        {
            var localPublicKey = GetLocalPublicKey();
            var helloDatagram = BuildHandshakeDatagram(PacketTypeHandshakeHello, Guid.NewGuid(), localPublicKey);
            await client.SendAsync(helloDatagram, helloDatagram.Length, remoteEndPoint);
        }
        catch (Exception e)
        {
            Logger.Warning(e, "UDP handshake HELLO send failed for {RemoteEndPoint}", remoteEndPoint);
        }
    }

    private async Task SendTransportAckAsync(UdpClient client, IPEndPoint remoteEndPoint, Guid messageId)
    {
        var ackDatagram = BuildTransportAckDatagram(messageId);
        try
        {
            await client.SendAsync(ackDatagram, ackDatagram.Length, remoteEndPoint);
        }
        catch (Exception e)
        {
            Logger.Warning(e, "UDP local listener failed to send ACK to {RemoteEndPoint}", remoteEndPoint);
        }
    }

    private bool TryGetSessionKey(IPEndPoint remoteEndPoint, out byte[] sessionKey)
    {
        var remoteKey = new UdpRemoteKey(remoteEndPoint.Address, remoteEndPoint.Port);
        lock (_pendingSync)
        {
            if (!_sessionKeys.TryGetValue(remoteKey, out var session))
            {
                sessionKey = [];
                return false;
            }

            var nowUtc = DateTime.UtcNow;
            if (nowUtc - session.UpdatedUtc > SessionKeyTtl)
            {
                CryptographicOperations.ZeroMemory(session.Key);
                _sessionKeys.Remove(remoteKey);
                sessionKey = [];
                return false;
            }

            session.UpdatedUtc = nowUtc;
            sessionKey = session.Key;
            return true;
        }
    }

    private void SetSessionKey(UdpRemoteKey remoteKey, byte[] key)
    {
        lock (_pendingSync)
        {
            if (_sessionKeys.TryGetValue(remoteKey, out var previous))
            {
                CryptographicOperations.ZeroMemory(previous.Key);
            }

            _sessionKeys[remoteKey] = new UdpSessionKey(key, DateTime.UtcNow);
        }
    }

    private void InvalidateSessionKey(UdpRemoteKey remoteKey)
    {
        lock (_pendingSync)
        {
            if (_sessionKeys.Remove(remoteKey, out var oldSession))
            {
                CryptographicOperations.ZeroMemory(oldSession.Key);
            }
        }
    }

    private byte[] GetLocalPublicKey()
    {
        lock (_sync)
        {
            if (_localPublicKey.Length == 0)
            {
                throw new InvalidOperationException("UDP local encryption key is not initialized.");
            }

            return _localPublicKey.ToArray();
        }
    }

    private byte[] DeriveSessionKey(ReadOnlySpan<byte> remotePublicKey)
    {
        lock (_sync)
        {
            if (_ecdh is null)
            {
                throw new InvalidOperationException("UDP local encryption key is not initialized.");
            }

            using var remote = ECDiffieHellman.Create();
            remote.ImportSubjectPublicKeyInfo(remotePublicKey, out _);
            var sharedSecret = _ecdh.DeriveKeyMaterial(remote.PublicKey);
            try
            {
                var material = new byte[sharedSecret.Length + KeyDerivationLabel.Length];
                sharedSecret.CopyTo(material, 0);
                KeyDerivationLabel.CopyTo(material, sharedSecret.Length);
                var key = SHA256.HashData(material);
                CryptographicOperations.ZeroMemory(material);
                return key;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(sharedSecret);
            }
        }
    }

    private void CleanupPendingStateUnsafe(DateTime nowUtc)
    {
        if (nowUtc - _lastPendingCleanupUtc < PendingCleanupInterval)
        {
            return;
        }

        _lastPendingCleanupUtc = nowUtc;

        List<UdpMessageKey>? staleMessageKeys = null;
        foreach (var kv in _pendingMessages)
        {
            if (nowUtc - kv.Value.UpdatedUtc <= PendingMessageTtl)
            {
                continue;
            }

            staleMessageKeys ??= [];
            staleMessageKeys.Add(kv.Key);
        }

        if (staleMessageKeys is not null)
        {
            foreach (var key in staleMessageKeys)
            {
                _pendingMessages.Remove(key);
            }
        }

        List<UdpMessageKey>? staleCompletedKeys = null;
        foreach (var kv in _completedMessages)
        {
            if (nowUtc - kv.Value <= CompletedMessageTtl)
            {
                continue;
            }

            staleCompletedKeys ??= [];
            staleCompletedKeys.Add(kv.Key);
        }

        if (staleCompletedKeys is not null)
        {
            foreach (var key in staleCompletedKeys)
            {
                _completedMessages.Remove(key);
            }
        }

        List<UdpRemoteKey>? staleSessionKeys = null;
        foreach (var kv in _sessionKeys)
        {
            if (nowUtc - kv.Value.UpdatedUtc <= SessionKeyTtl)
            {
                continue;
            }

            staleSessionKeys ??= [];
            staleSessionKeys.Add(kv.Key);
        }

        if (staleSessionKeys is null)
        {
            return;
        }

        foreach (var key in staleSessionKeys)
        {
            if (_sessionKeys.Remove(key, out var oldSession))
            {
                CryptographicOperations.ZeroMemory(oldSession.Key);
            }
        }
    }

    private async Task PublishPacketAsync(byte[] payload, IPEndPoint remoteEndPoint, CancellationToken token)
    {
        var handler = PacketReceived;
        if (handler is null)
        {
            return;
        }

        var packet = new LocalDataPacket(Protocol, remoteEndPoint, payload);
        foreach (LocalDataPacketReceivedHandler subscriber in handler.GetInvocationList())
        {
            try
            {
                await subscriber(packet, token);
            }
            catch (Exception e)
            {
                Logger.Error(e, "UDP local listener packet callback failed");
            }
        }
    }

    private static async Task SendDatagramsAsync(
        UdpClient client,
        IReadOnlyList<byte[]> datagrams,
        IPEndPoint remoteEndPoint,
        CancellationToken cancellationToken)
    {
        foreach (var datagram in datagrams)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await client.SendAsync(datagram, datagram.Length, remoteEndPoint);
        }
    }

    private static bool TryParseDatagram(
        byte[] datagram,
        out byte packetType,
        out byte flags,
        out Guid messageId,
        out int chunkIndex,
        out int totalChunkCount,
        out int payloadOffset,
        out int payloadLength,
        out uint payloadChecksum)
    {
        packetType = 0;
        flags = 0;
        messageId = Guid.Empty;
        chunkIndex = 0;
        totalChunkCount = 0;
        payloadOffset = 0;
        payloadLength = 0;
        payloadChecksum = 0;

        if (datagram.Length < HeaderLength)
        {
            return false;
        }

        var span = datagram.AsSpan();
        if (BinaryPrimitives.ReadInt32BigEndian(span[..4]) != DatagramMagic)
        {
            return false;
        }

        if (span[4] != DatagramVersion)
        {
            return false;
        }

        packetType = span[5];
        if (packetType is not PacketTypeData and not PacketTypeTransportAck and not PacketTypeHandshakeHello and not PacketTypeHandshakeAck)
        {
            return false;
        }

        flags = span[6];
        messageId = new Guid(span.Slice(7, 16));
        chunkIndex = BinaryPrimitives.ReadInt32BigEndian(span.Slice(23, 4));
        totalChunkCount = BinaryPrimitives.ReadInt32BigEndian(span.Slice(27, 4));
        payloadLength = BinaryPrimitives.ReadInt32BigEndian(span.Slice(31, 4));
        payloadChecksum = BinaryPrimitives.ReadUInt32BigEndian(span.Slice(35, 4));
        payloadOffset = HeaderLength;

        if (payloadLength < 0 || payloadOffset + payloadLength > datagram.Length)
        {
            return false;
        }

        if (packetType == PacketTypeTransportAck)
        {
            return chunkIndex == 0 && totalChunkCount == 0 && payloadLength == 0;
        }

        if (packetType is PacketTypeHandshakeHello or PacketTypeHandshakeAck)
        {
            return chunkIndex == 0 && totalChunkCount == 1 && payloadLength > 0;
        }

        return totalChunkCount > 0 && chunkIndex >= 0 && chunkIndex < totalChunkCount;
    }

    private static byte[][] BuildDataDatagrams(ReadOnlySpan<byte> payload, Guid messageId)
    {
        var totalChunkCount = (payload.Length + MaxChunkPayloadSize - 1) / MaxChunkPayloadSize;
        var datagrams = new byte[totalChunkCount][];
        const byte flags = FlagAckRequired;
        for (var chunkIndex = 0; chunkIndex < totalChunkCount; chunkIndex++)
        {
            var offset = chunkIndex * MaxChunkPayloadSize;
            var length = Math.Min(MaxChunkPayloadSize, payload.Length - offset);
            var chunk = payload.Slice(offset, length);
            var datagram = new byte[HeaderLength + length];
            var span = datagram.AsSpan();

            WriteDatagramHeader(
                span,
                PacketTypeData,
                flags,
                messageId,
                chunkIndex,
                totalChunkCount,
                length,
                Crc32.Compute(chunk));
            chunk.CopyTo(span.Slice(HeaderLength, length));
            datagrams[chunkIndex] = datagram;
        }

        return datagrams;
    }

    private static byte[] BuildTransportAckDatagram(Guid messageId)
    {
        var datagram = new byte[HeaderLength];
        WriteDatagramHeader(datagram, PacketTypeTransportAck, 0, messageId, 0, 0, 0, 0);
        return datagram;
    }

    private static byte[] BuildHandshakeDatagram(byte packetType, Guid messageId, ReadOnlySpan<byte> publicKey)
    {
        var datagram = new byte[HeaderLength + publicKey.Length];
        var span = datagram.AsSpan();
        WriteDatagramHeader(
            span,
            packetType,
            0,
            messageId,
            0,
            1,
            publicKey.Length,
            Crc32.Compute(publicKey));
        publicKey.CopyTo(span.Slice(HeaderLength, publicKey.Length));
        return datagram;
    }

    private static void WriteDatagramHeader(
        Span<byte> span,
        byte packetType,
        byte flags,
        Guid messageId,
        int chunkIndex,
        int totalChunkCount,
        int payloadLength,
        uint payloadChecksum)
    {
        BinaryPrimitives.WriteInt32BigEndian(span[..4], DatagramMagic);
        span[4] = DatagramVersion;
        span[5] = packetType;
        span[6] = flags;
        messageId.TryWriteBytes(span.Slice(7, 16));
        BinaryPrimitives.WriteInt32BigEndian(span.Slice(23, 4), chunkIndex);
        BinaryPrimitives.WriteInt32BigEndian(span.Slice(27, 4), totalChunkCount);
        BinaryPrimitives.WriteInt32BigEndian(span.Slice(31, 4), payloadLength);
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(35, 4), payloadChecksum);
    }

    private static byte[] EncryptPayload(ReadOnlySpan<byte> plainPayload, ReadOnlySpan<byte> key)
    {
        var nonce = new byte[AesNonceLength];
        RandomNumberGenerator.Fill(nonce);
        var cipher = new byte[plainPayload.Length];
        var tag = new byte[AesTagLength];

        using (var aes = new AesGcm(key, AesTagLength))
        {
            aes.Encrypt(nonce, plainPayload, cipher, tag);
        }

        var encryptedPayload = new byte[1 + AesNonceLength + AesTagLength + cipher.Length];
        encryptedPayload[0] = EncryptedPayloadVersion;
        nonce.CopyTo(encryptedPayload, 1);
        tag.CopyTo(encryptedPayload, 1 + AesNonceLength);
        cipher.CopyTo(encryptedPayload, 1 + AesNonceLength + AesTagLength);
        return encryptedPayload;
    }

    private static bool TryDecryptPayload(ReadOnlySpan<byte> encryptedPayload, ReadOnlySpan<byte> key, out byte[] plainPayload)
    {
        plainPayload = [];
        if (encryptedPayload.Length < 1 + AesNonceLength + AesTagLength)
        {
            return false;
        }

        if (encryptedPayload[0] != EncryptedPayloadVersion)
        {
            return false;
        }

        var nonce = encryptedPayload.Slice(1, AesNonceLength);
        var tag = encryptedPayload.Slice(1 + AesNonceLength, AesTagLength);
        var cipher = encryptedPayload.Slice(1 + AesNonceLength + AesTagLength);
        plainPayload = new byte[cipher.Length];
        try
        {
            using var aes = new AesGcm(key, AesTagLength);
            aes.Decrypt(nonce, cipher, tag, plainPayload);
            return true;
        }
        catch (CryptographicException)
        {
            plainPayload = [];
            return false;
        }
    }

    private readonly record struct UdpMessageKey(IPAddress Address, int Port, Guid MessageId);
    private readonly record struct UdpRemoteKey(IPAddress Address, int Port);

    private sealed class UdpSessionKey
    {
        public UdpSessionKey(byte[] key, DateTime updatedUtc)
        {
            Key = key;
            UpdatedUtc = updatedUtc;
        }

        public byte[] Key { get; }
        public DateTime UpdatedUtc { get; set; }
    }

    private sealed class UdpPendingMessage
    {
        private readonly byte[]?[] _chunks;
        private int _receivedCount;
        private int _totalLength;

        public UdpPendingMessage(int chunkCount)
        {
            _chunks = new byte[chunkCount][];
            UpdatedUtc = DateTime.UtcNow;
        }

        public int ChunkCount => _chunks.Length;
        public bool IsComplete => _receivedCount == _chunks.Length;
        public DateTime UpdatedUtc { get; set; }

        public bool TryAddChunk(int chunkIndex, ReadOnlySpan<byte> payload)
        {
            if ((uint)chunkIndex >= (uint)_chunks.Length)
            {
                return false;
            }

            if (_chunks[chunkIndex] is not null)
            {
                return true;
            }

            var chunk = payload.ToArray();
            _chunks[chunkIndex] = chunk;
            _receivedCount++;
            _totalLength += chunk.Length;
            return true;
        }

        public byte[] AssemblePayload()
        {
            if (!IsComplete)
            {
                throw new InvalidOperationException("The UDP message is not complete.");
            }

            var payload = new byte[_totalLength];
            var offset = 0;
            for (var i = 0; i < _chunks.Length; i++)
            {
                var chunk = _chunks[i] ?? throw new InvalidOperationException("The UDP message chunk is missing.");
                chunk.CopyTo(payload, offset);
                offset += chunk.Length;
            }

            return payload;
        }
    }

    private static class Crc32
    {
        private static readonly uint[] Table = BuildTable();

        public static uint Compute(ReadOnlySpan<byte> data)
        {
            var crc = uint.MaxValue;
            foreach (var value in data)
            {
                var index = (byte)(crc ^ value);
                crc = Table[index] ^ (crc >> 8);
            }

            return ~crc;
        }

        private static uint[] BuildTable()
        {
            var table = new uint[256];
            for (var i = 0; i < table.Length; i++)
            {
                uint value = (uint)i;
                for (var bit = 0; bit < 8; bit++)
                {
                    value = (value & 1) == 0 ? value >> 1 : 0xEDB88320u ^ (value >> 1);
                }

                table[i] = value;
            }

            return table;
        }
    }
}
