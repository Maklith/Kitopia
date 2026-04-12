using System.IO.Pipelines;
using System.Net;
using System.Net.Quic;
using Core.Services;
using Serilog;

namespace Core.Services.DeviceCommunication;

public sealed class LocalDataListenerHost : IDisposable, ILocalDataListener
{
    private static readonly ILogger Logger = LogManager.Logger.ForContext<LocalDataListenerHost>();

    private readonly object _sync = new();
    private readonly TcpLocalDataListener _tcpListener;
    private readonly QuicLocalDataListener _quicListener;

    private bool _isStarted;

    public LocalDataListenerHost(Protocol.ProtocolSession protocolSession)
    {
        _tcpListener = new TcpLocalDataListener(protocolSession);
        _quicListener = new QuicLocalDataListener(protocolSession);
    }

    public int TcpPort => _tcpListener.Port;

    public int QuicPort => _quicListener.Port;

    public bool SupportsQuic => _quicListener.IsRunning;

    public void Dispose()
    {
        StopListeningAsync().GetAwaiter().GetResult();
        _quicListener.Dispose();
        _tcpListener.Dispose();
    }

    public async Task StartListeningAsync(CancellationToken token = default)
    {
        lock (_sync)
        {
            if (_isStarted)
            {
                return;
            }

            _isStarted = true;
        }

        await _tcpListener.StartAsync(token);

        if (QuicConnection.IsSupported && QuicListener.IsSupported)
        {
            await _quicListener.StartAsync(token);
            return;
        }

        Logger.Information(
            "QUIC local listener skipped. QuicConnectionSupported={ConnectionSupported}, QuicListenerSupported={ListenerSupported}",
            QuicConnection.IsSupported,
            QuicListener.IsSupported);
    }

    public Task SendAsync(
        LocalDataTransportProtocol protocol,
        ReadOnlyMemory<byte> payload,
        IPEndPoint remoteEndPoint,
        string? remoteIdentityPublicKey = null,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(remoteEndPoint);
        if (string.IsNullOrWhiteSpace(remoteIdentityPublicKey))
        {
            throw new ArgumentException("Remote identity public key is required.", nameof(remoteIdentityPublicKey));
        }

        return protocol switch
        {
            LocalDataTransportProtocol.Tcp => _tcpListener.SendAsync(payload, remoteEndPoint, remoteIdentityPublicKey, token),
            LocalDataTransportProtocol.Quic => _quicListener.SendAsync(payload, remoteEndPoint, remoteIdentityPublicKey, token),
            _ => throw new ArgumentOutOfRangeException(nameof(protocol), protocol, "Unsupported transport protocol.")
        };
    }

    public async Task SendAsync(
        LocalDataTransportProtocol protocol,
        PipeReader payloadReader,
        IPEndPoint remoteEndPoint,
        string? remoteIdentityPublicKey = null,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(payloadReader);
        ArgumentNullException.ThrowIfNull(remoteEndPoint);
        if (string.IsNullOrWhiteSpace(remoteIdentityPublicKey))
        {
            throw new ArgumentException("Remote identity public key is required.", nameof(remoteIdentityPublicKey));
        }

        switch (protocol)
        {
            case LocalDataTransportProtocol.Tcp:
                await _tcpListener.SendAsync(payloadReader, remoteEndPoint, remoteIdentityPublicKey, token);
                break;
            case LocalDataTransportProtocol.Quic:
                await _quicListener.SendAsync(payloadReader, remoteEndPoint, remoteIdentityPublicKey, token);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(protocol), protocol, "Unsupported transport protocol.");
        }
    }

    public async Task SendAsync(
        LocalDataTransportProtocol protocol,
        Stream stream,
        IPEndPoint remoteEndPoint,
        string? remoteIdentityPublicKey = null,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(remoteEndPoint);
        if (string.IsNullOrWhiteSpace(remoteIdentityPublicKey))
        {
            throw new ArgumentException("Remote identity public key is required.", nameof(remoteIdentityPublicKey));
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
            await SendAsync(protocol, pipe.Reader, remoteEndPoint, remoteIdentityPublicKey, token);
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

    public async Task StopListeningAsync()
    {
        bool shouldStop;

        lock (_sync)
        {
            shouldStop = _isStarted;
            _isStarted = false;
        }

        if (!shouldStop)
        {
            return;
        }

        await _quicListener.StopAsync();
        await _tcpListener.StopAsync();
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
}
