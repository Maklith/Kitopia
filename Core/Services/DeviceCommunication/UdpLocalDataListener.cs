using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Core.Services;
using Core.Services.Config;
using Core.Services.DeviceCommunication.Discovery;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
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
    private const int MaxDataChunkCount = 4096;
    private const int ReliableMaxRetryCount = 2;
    private const int HandshakeMaxRetryCount = 2;
    private const byte HandshakePayloadVersion = 1;
    private const byte EncryptedPayloadVersion = 1;
    private const long EncryptedPayloadTimestampToleranceSeconds = 120;
    private const byte TransportAckPayloadVersion = 1;
    private const int TransportAckTokenLength = 16;
    private const long TransportAckTimestampToleranceSeconds = 120;
    private const int TransportAckPayloadLength = 1 + 8 + 16 + TransportAckTokenLength;
    private const int AesNonceLength = 12;
    private const int AesTagLength = 16;
    private const long HandshakeTimestampToleranceSeconds = 60;

    private static readonly TimeSpan ReliableAckTimeout = TimeSpan.FromMilliseconds(800);
    private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromMilliseconds(800);
    private static readonly TimeSpan PendingMessageTtl = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan CompletedMessageTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan SessionKeyTtl = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan PendingCleanupInterval = TimeSpan.FromSeconds(3);

    private static readonly ILogger Logger = LogManager.Logger.ForContext<UdpLocalDataListener>();
    private static readonly byte[] KeyDerivationLabel = "kitopia-udp-aesgcm-v1"u8.ToArray();

    private readonly object _sync = new();
    private readonly object _pendingSync = new();
    private readonly ConcurrentDictionary<UdpMessageKey, TaskCompletionSource<bool>> _pendingAcks = [];
    private readonly ConcurrentDictionary<UdpMessageKey, UdpPendingHandshakeAck> _pendingHandshakeAcks = [];
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
            _udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, 0));
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

                var sessionKey = await EnsureSessionKeyAsync(client, remoteEndPoint, remoteIdentityPublicKey, cancellationToken);
                var encryptedPayload = EncryptPayload(payload.Span, sessionKey, ackKey.MessageId);
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
        string expectedRemoteIdentityPublicKey,
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
            UdpPendingHandshakeAck pendingHandshakeAck;
            while (true)
            {
                var messageId = Guid.NewGuid();
                handshakeKey = new UdpMessageKey(remoteEndPoint.Address, remoteEndPoint.Port, messageId);
                pendingHandshakeAck = new UdpPendingHandshakeAck(
                    expectedRemoteIdentityPublicKey,
                    new TaskCompletionSource<UdpHandshakeEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously));
                if (_pendingHandshakeAcks.TryAdd(handshakeKey, pendingHandshakeAck))
                {
                    break;
                }
            }

            try
            {
                var helloDatagram = BuildSignedHandshakeDatagram(PacketTypeHandshakeHello, handshakeKey.MessageId, localPublicKey);
                await client.SendAsync(helloDatagram, helloDatagram.Length, remoteEndPoint);

                var remoteHandshake = await pendingHandshakeAck.Waiter.Task.WaitAsync(HandshakeTimeout, cancellationToken);
                var sessionKey = DeriveSessionKey(remoteHandshake.EcdhPublicKey);
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
            catch (CryptographicException) when (attempt + 1 < attemptCount)
            {
                Logger.Warning(
                    "UDP handshake verification failed for {RemoteEndPoint}, retry {Retry}/{TotalRetry}",
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
            pendingHandshakeAck.Value.Waiter.TrySetCanceled();
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
        var payloadSpan = datagram.AsSpan(payloadOffset, payloadLength);
        if (Crc32.Compute(payloadSpan) != payloadChecksum)
        {
            Logger.Warning("UDP local listener dropped corrupted datagram from {RemoteEndPoint}", remoteEndPoint);
            return;
        }

        if (packetType == PacketTypeTransportAck)
        {
            if (_pendingAcks.TryGetValue(messageKey, out var ackWaiter) &&
                TryGetSessionKey(remoteEndPoint, out var ackSessionKey) &&
                TryParseAndVerifyTransportAckPayload(payloadSpan, messageId, ackSessionKey))
            {
                _pendingAcks.TryRemove(messageKey, out _);
                ackWaiter.TrySetResult(true);
            }

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
                if (TryGetSessionKey(remoteEndPoint, out var duplicateSessionKey))
                {
                    await SendTransportAckAsync(client, remoteEndPoint, messageId, duplicateSessionKey);
                }
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

        if (!TryDecryptPayload(encryptedPayload, sessionKey, messageId, out var plainPayload))
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
            await SendTransportAckAsync(client, remoteEndPoint, messageId, sessionKey);
        }

        await PublishPacketAsync(plainPayload, remoteEndPoint, token);
    }

    private async Task HandleHandshakeHelloAsync(
        UdpClient client,
        IPEndPoint remoteEndPoint,
        Guid messageId,
        byte[] payload)
    {
        try
        {
            var expectedRemoteIdentityPublicKey = ResolveExpectedIdentityPublicKey(remoteEndPoint);
            if (string.IsNullOrWhiteSpace(expectedRemoteIdentityPublicKey) ||
                !TryParseAndVerifyHandshakePayload(
                    payload,
                    PacketTypeHandshakeHello,
                    messageId,
                    expectedRemoteIdentityPublicKey,
                    out var remoteHandshake))
            {
                return;
            }

            var sessionKey = DeriveSessionKey(remoteHandshake.EcdhPublicKey);
            SetSessionKey(new UdpRemoteKey(remoteEndPoint.Address, remoteEndPoint.Port), sessionKey);

            var localPublicKey = GetLocalPublicKey();
            var ackDatagram = BuildSignedHandshakeDatagram(PacketTypeHandshakeAck, messageId, localPublicKey);
            await client.SendAsync(ackDatagram, ackDatagram.Length, remoteEndPoint);
        }
        catch (Exception e)
        {
            Logger.Warning(e, "UDP handshake HELLO processing failed for {RemoteEndPoint}", remoteEndPoint);
        }
    }

    private void HandleHandshakeAck(IPEndPoint remoteEndPoint, Guid messageId, ReadOnlySpan<byte> payload)
    {
        var messageKey = new UdpMessageKey(remoteEndPoint.Address, remoteEndPoint.Port, messageId);
        if (_pendingHandshakeAcks.TryGetValue(messageKey, out var pendingHandshakeAck))
        {
            if (TryParseAndVerifyHandshakePayload(
                    payload,
                    PacketTypeHandshakeAck,
                    messageId,
                    pendingHandshakeAck.ExpectedIdentityPublicKey,
                    out var handshakeEnvelope))
            {
                pendingHandshakeAck.Waiter.TrySetResult(handshakeEnvelope);
            }
            else
            {
                pendingHandshakeAck.Waiter.TrySetException(new CryptographicException("UDP handshake ACK verification failed."));
            }

            return;
        }

        try
        {
            var expectedRemoteIdentityPublicKey = ResolveExpectedIdentityPublicKey(remoteEndPoint);
            if (string.IsNullOrWhiteSpace(expectedRemoteIdentityPublicKey) ||
                !TryParseAndVerifyHandshakePayload(
                    payload,
                    PacketTypeHandshakeAck,
                    messageId,
                    expectedRemoteIdentityPublicKey,
                    out var handshakeEnvelope))
            {
                return;
            }

            var sessionKey = DeriveSessionKey(handshakeEnvelope.EcdhPublicKey);
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
            var helloDatagram = BuildSignedHandshakeDatagram(PacketTypeHandshakeHello, Guid.NewGuid(), localPublicKey);
            await client.SendAsync(helloDatagram, helloDatagram.Length, remoteEndPoint);
        }
        catch (Exception e)
        {
            Logger.Warning(e, "UDP handshake HELLO send failed for {RemoteEndPoint}", remoteEndPoint);
        }
    }

    private async Task SendTransportAckAsync(
        UdpClient client,
        IPEndPoint remoteEndPoint,
        Guid messageId,
        byte[] sessionKey)
    {
        var ackDatagram = BuildTransportAckDatagram(messageId, sessionKey);
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
            return chunkIndex == 0 && totalChunkCount == 0 && payloadLength == TransportAckPayloadLength;
        }

        if (packetType is PacketTypeHandshakeHello or PacketTypeHandshakeAck)
        {
            return chunkIndex == 0 && totalChunkCount == 1 && payloadLength > 0;
        }

        return totalChunkCount > 0 &&
               totalChunkCount <= MaxDataChunkCount &&
               chunkIndex >= 0 &&
               chunkIndex < totalChunkCount &&
               payloadLength > 0 &&
               payloadLength <= MaxChunkPayloadSize;
    }

    private static byte[][] BuildDataDatagrams(ReadOnlySpan<byte> payload, Guid messageId)
    {
        var totalChunkCount = (payload.Length + MaxChunkPayloadSize - 1) / MaxChunkPayloadSize;
        if (totalChunkCount <= 0 || totalChunkCount > MaxDataChunkCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(payload),
                payload.Length,
                $"Payload is too large for UDP transport. Max chunk count is {MaxDataChunkCount}.");
        }

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

    private static byte[] BuildTransportAckDatagram(Guid messageId, ReadOnlySpan<byte> sessionKey)
    {
        var ackPayload = BuildTransportAckPayload(messageId, sessionKey);
        var datagram = new byte[HeaderLength + ackPayload.Length];
        var span = datagram.AsSpan();
        WriteDatagramHeader(
            span,
            PacketTypeTransportAck,
            0,
            messageId,
            0,
            0,
            ackPayload.Length,
            Crc32.Compute(ackPayload));
        ackPayload.CopyTo(span.Slice(HeaderLength, ackPayload.Length));
        return datagram;
    }

    private static byte[] BuildTransportAckPayload(Guid messageId, ReadOnlySpan<byte> sessionKey)
    {
        var timestampUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var payload = new byte[TransportAckPayloadLength];
        var span = payload.AsSpan();
        span[0] = TransportAckPayloadVersion;
        BinaryPrimitives.WriteInt64BigEndian(span.Slice(1, 8), timestampUnixSeconds);
        messageId.TryWriteBytes(span.Slice(9, 16));
        var token = ComputeTransportAckToken(messageId, timestampUnixSeconds, sessionKey);
        token.CopyTo(span.Slice(25, TransportAckTokenLength));
        CryptographicOperations.ZeroMemory(token);
        return payload;
    }

    private static bool TryParseAndVerifyTransportAckPayload(
        ReadOnlySpan<byte> payload,
        Guid expectedMessageId,
        ReadOnlySpan<byte> sessionKey)
    {
        if (payload.Length != TransportAckPayloadLength || payload[0] != TransportAckPayloadVersion)
        {
            return false;
        }

        var timestampUnixSeconds = BinaryPrimitives.ReadInt64BigEndian(payload.Slice(1, 8));
        var nowUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var skew = nowUnixSeconds >= timestampUnixSeconds
            ? nowUnixSeconds - timestampUnixSeconds
            : timestampUnixSeconds - nowUnixSeconds;
        if (skew > TransportAckTimestampToleranceSeconds)
        {
            return false;
        }

        var messageId = new Guid(payload.Slice(9, 16));
        if (messageId != expectedMessageId)
        {
            return false;
        }

        var expectedToken = ComputeTransportAckToken(messageId, timestampUnixSeconds, sessionKey);
        var isValid = CryptographicOperations.FixedTimeEquals(payload.Slice(25, TransportAckTokenLength), expectedToken);
        CryptographicOperations.ZeroMemory(expectedToken);
        return isValid;
    }

    private static byte[] ComputeTransportAckToken(Guid messageId, long timestampUnixSeconds, ReadOnlySpan<byte> sessionKey)
    {
        var signPayload = new byte[1 + 8 + 16];
        var span = signPayload.AsSpan();
        span[0] = TransportAckPayloadVersion;
        BinaryPrimitives.WriteInt64BigEndian(span.Slice(1, 8), timestampUnixSeconds);
        messageId.TryWriteBytes(span.Slice(9, 16));

        using var hmac = new HMACSHA256(sessionKey.ToArray());
        var digest = hmac.ComputeHash(signPayload);
        var token = new byte[TransportAckTokenLength];
        digest.AsSpan(0, TransportAckTokenLength).CopyTo(token);
        CryptographicOperations.ZeroMemory(digest);
        CryptographicOperations.ZeroMemory(signPayload);
        return token;
    }

    private static byte[] BuildSignedHandshakeDatagram(byte packetType, Guid messageId, ReadOnlySpan<byte> ecdhPublicKey)
    {
        var localIdentityPublicKey = GetLocalIdentityPublicKey();
        var localIdentityPrivateKey = GetLocalIdentityPrivateKey();
        if (string.IsNullOrWhiteSpace(localIdentityPublicKey) || string.IsNullOrWhiteSpace(localIdentityPrivateKey))
        {
            throw new InvalidOperationException("Device identity key is not initialized.");
        }

        var timestampUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var signPayload = BuildHandshakeSignPayload(packetType, messageId, timestampUnixSeconds, localIdentityPublicKey, ecdhPublicKey);
        if (!DeviceDiscoverySignature.TrySignData(signPayload, localIdentityPrivateKey, out var signature))
        {
            throw new InvalidOperationException("Failed to sign UDP handshake.");
        }

        var handshakePayload = BuildHandshakePayload(timestampUnixSeconds, localIdentityPublicKey, ecdhPublicKey, signature);
        var datagram = new byte[HeaderLength + handshakePayload.Length];
        var span = datagram.AsSpan();
        WriteDatagramHeader(
            span,
            packetType,
            0,
            messageId,
            0,
            1,
            handshakePayload.Length,
            Crc32.Compute(handshakePayload));
        handshakePayload.CopyTo(span.Slice(HeaderLength, handshakePayload.Length));
        return datagram;
    }

    private static bool TryParseAndVerifyHandshakePayload(
        ReadOnlySpan<byte> payload,
        byte packetType,
        Guid messageId,
        string? expectedIdentityPublicKey,
        out UdpHandshakeEnvelope envelope)
    {
        envelope = default;

        if (payload.Length < 21)
        {
            return false;
        }

        if (payload[0] != HandshakePayloadVersion)
        {
            return false;
        }

        var timestampUnixSeconds = BinaryPrimitives.ReadInt64BigEndian(payload.Slice(1, 8));
        var identityLength = BinaryPrimitives.ReadInt32BigEndian(payload.Slice(9, 4));
        var ecdhPublicKeyLength = BinaryPrimitives.ReadInt32BigEndian(payload.Slice(13, 4));
        var signatureLength = BinaryPrimitives.ReadInt32BigEndian(payload.Slice(17, 4));

        if (identityLength <= 0 || ecdhPublicKeyLength <= 0 || signatureLength <= 0)
        {
            return false;
        }

        var totalLength = 21 + identityLength + ecdhPublicKeyLength + signatureLength;
        if (totalLength != payload.Length)
        {
            return false;
        }

        var offset = 21;
        var identityBytes = payload.Slice(offset, identityLength).ToArray();
        offset += identityLength;
        var ecdhPublicKey = payload.Slice(offset, ecdhPublicKeyLength).ToArray();
        offset += ecdhPublicKeyLength;
        var signature = payload.Slice(offset, signatureLength).ToArray();
        var identityPublicKey = System.Text.Encoding.UTF8.GetString(identityBytes).Trim();

        if (string.IsNullOrWhiteSpace(identityPublicKey))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(expectedIdentityPublicKey) &&
            !string.Equals(identityPublicKey, expectedIdentityPublicKey, StringComparison.Ordinal))
        {
            return false;
        }

        var nowUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var skew = nowUnixSeconds >= timestampUnixSeconds
            ? nowUnixSeconds - timestampUnixSeconds
            : timestampUnixSeconds - nowUnixSeconds;

        if (skew > HandshakeTimestampToleranceSeconds)
        {
            return false;
        }

        var signPayload = BuildHandshakeSignPayload(packetType, messageId, timestampUnixSeconds, identityPublicKey, ecdhPublicKey);
        if (!DeviceDiscoverySignature.VerifyData(signPayload, identityPublicKey, signature))
        {
            return false;
        }

        envelope = new UdpHandshakeEnvelope(identityPublicKey, ecdhPublicKey);
        return true;
    }

    private static byte[] BuildHandshakePayload(
        long timestampUnixSeconds,
        string identityPublicKey,
        ReadOnlySpan<byte> ecdhPublicKey,
        ReadOnlySpan<byte> signature)
    {
        var identityBytes = System.Text.Encoding.UTF8.GetBytes(identityPublicKey);
        var payload = new byte[21 + identityBytes.Length + ecdhPublicKey.Length + signature.Length];
        var span = payload.AsSpan();
        span[0] = HandshakePayloadVersion;
        BinaryPrimitives.WriteInt64BigEndian(span.Slice(1, 8), timestampUnixSeconds);
        BinaryPrimitives.WriteInt32BigEndian(span.Slice(9, 4), identityBytes.Length);
        BinaryPrimitives.WriteInt32BigEndian(span.Slice(13, 4), ecdhPublicKey.Length);
        BinaryPrimitives.WriteInt32BigEndian(span.Slice(17, 4), signature.Length);

        var offset = 21;
        identityBytes.CopyTo(span.Slice(offset, identityBytes.Length));
        offset += identityBytes.Length;
        ecdhPublicKey.CopyTo(span.Slice(offset, ecdhPublicKey.Length));
        offset += ecdhPublicKey.Length;
        signature.CopyTo(span.Slice(offset, signature.Length));
        return payload;
    }

    private static byte[] BuildHandshakeSignPayload(
        byte packetType,
        Guid messageId,
        long timestampUnixSeconds,
        string identityPublicKey,
        ReadOnlySpan<byte> ecdhPublicKey)
    {
        var identityBytes = System.Text.Encoding.UTF8.GetBytes(identityPublicKey);
        var signPayload = new byte[1 + 16 + 8 + 4 + identityBytes.Length + 4 + ecdhPublicKey.Length];
        var span = signPayload.AsSpan();
        span[0] = packetType;
        messageId.TryWriteBytes(span.Slice(1, 16));
        BinaryPrimitives.WriteInt64BigEndian(span.Slice(17, 8), timestampUnixSeconds);
        BinaryPrimitives.WriteInt32BigEndian(span.Slice(25, 4), identityBytes.Length);
        identityBytes.CopyTo(span.Slice(29, identityBytes.Length));
        var offset = 29 + identityBytes.Length;
        BinaryPrimitives.WriteInt32BigEndian(span.Slice(offset, 4), ecdhPublicKey.Length);
        offset += 4;
        ecdhPublicKey.CopyTo(span.Slice(offset, ecdhPublicKey.Length));
        return signPayload;
    }

    private static string GetLocalIdentityPublicKey()
    {
        return ConfigManger.Config.devicePersistentId?.Trim() ?? string.Empty;
    }

    private static string GetLocalIdentityPrivateKey()
    {
        return ConfigManger.Config.devicePrivateKey?.Trim() ?? string.Empty;
    }

    private static string? ResolveExpectedIdentityPublicKey(IPEndPoint remoteEndPoint)
    {
        var discoveryService = ServiceManager.Services.GetService<IDeviceDiscoveryService>();
        if (discoveryService is null)
        {
            return null;
        }

        var normalizedAddress = NormalizeAddress(remoteEndPoint.Address);
        var matchedDevice = discoveryService.Devices.FirstOrDefault(device =>
            NormalizeAddress(device.Address).Equals(normalizedAddress));

        return matchedDevice is null || string.IsNullOrWhiteSpace(matchedDevice.Id)
            ? null
            : matchedDevice.Id;
    }

    private static IPAddress NormalizeAddress(IPAddress address)
    {
        return address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
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

    private static byte[] EncryptPayload(ReadOnlySpan<byte> plainPayload, ReadOnlySpan<byte> key, Guid messageId)
    {
        var timestampUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var plaintextEnvelope = new byte[8 + 16 + plainPayload.Length];
        var envelopeSpan = plaintextEnvelope.AsSpan();
        BinaryPrimitives.WriteInt64BigEndian(envelopeSpan.Slice(0, 8), timestampUnixSeconds);
        messageId.TryWriteBytes(envelopeSpan.Slice(8, 16));
        plainPayload.CopyTo(envelopeSpan.Slice(24));

        var nonce = new byte[AesNonceLength];
        RandomNumberGenerator.Fill(nonce);
        var cipher = new byte[plaintextEnvelope.Length];
        var tag = new byte[AesTagLength];

        using (var aes = new AesGcm(key, AesTagLength))
        {
            aes.Encrypt(nonce, plaintextEnvelope, cipher, tag);
        }

        var encryptedPayload = new byte[1 + AesNonceLength + AesTagLength + cipher.Length];
        encryptedPayload[0] = EncryptedPayloadVersion;
        nonce.CopyTo(encryptedPayload, 1);
        tag.CopyTo(encryptedPayload, 1 + AesNonceLength);
        cipher.CopyTo(encryptedPayload, 1 + AesNonceLength + AesTagLength);
        CryptographicOperations.ZeroMemory(plaintextEnvelope);
        return encryptedPayload;
    }

    private static bool TryDecryptPayload(
        ReadOnlySpan<byte> encryptedPayload,
        ReadOnlySpan<byte> key,
        Guid expectedMessageId,
        out byte[] plainPayload)
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
        var decryptedPayload = new byte[cipher.Length];
        try
        {
            using var aes = new AesGcm(key, AesTagLength);
            aes.Decrypt(nonce, cipher, tag, decryptedPayload);
            if (decryptedPayload.Length < 24)
            {
                CryptographicOperations.ZeroMemory(decryptedPayload);
                return false;
            }

            var timestampUnixSeconds = BinaryPrimitives.ReadInt64BigEndian(decryptedPayload.AsSpan(0, 8));
            var nowUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var skew = nowUnixSeconds >= timestampUnixSeconds
                ? nowUnixSeconds - timestampUnixSeconds
                : timestampUnixSeconds - nowUnixSeconds;
            if (skew > EncryptedPayloadTimestampToleranceSeconds)
            {
                CryptographicOperations.ZeroMemory(decryptedPayload);
                return false;
            }

            var messageId = new Guid(decryptedPayload.AsSpan(8, 16));
            if (messageId != expectedMessageId)
            {
                CryptographicOperations.ZeroMemory(decryptedPayload);
                return false;
            }

            plainPayload = decryptedPayload.AsSpan(24).ToArray();
            CryptographicOperations.ZeroMemory(decryptedPayload);
            return true;
        }
        catch (CryptographicException)
        {
            CryptographicOperations.ZeroMemory(decryptedPayload);
            plainPayload = [];
            return false;
        }
    }

    private readonly record struct UdpMessageKey(IPAddress Address, int Port, Guid MessageId);
    private readonly record struct UdpRemoteKey(IPAddress Address, int Port);
    private readonly record struct UdpHandshakeEnvelope(string IdentityPublicKey, byte[] EcdhPublicKey);

    private sealed class UdpPendingHandshakeAck
    {
        public UdpPendingHandshakeAck(string expectedIdentityPublicKey, TaskCompletionSource<UdpHandshakeEnvelope> waiter)
        {
            ExpectedIdentityPublicKey = expectedIdentityPublicKey;
            Waiter = waiter;
        }

        public string ExpectedIdentityPublicKey { get; }
        public TaskCompletionSource<UdpHandshakeEnvelope> Waiter { get; }
    }

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
