using System.Net;
using System.Net.Sockets;
using Core.Services;
using Serilog;

namespace Core.Services.DeviceCommunication;

public sealed class UdpLocalDataListener : IDisposable
{
    private static readonly ILogger Logger = LogManager.Logger.ForContext<UdpLocalDataListener>();

    private readonly object _sync = new();
    private int _port;

    private UdpClient? _udpClient;
    private CancellationTokenSource? _cts;
    private Task? _receiveTask;

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

  

    public void Start()
    {
        lock (_sync)
        {
            if (IsRunning)
            {
                return;
            }

            _cts = new CancellationTokenSource();
            _udpClient = new UdpClient(AddressFamily.InterNetwork);
            _udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _udpClient.Client.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            if (_udpClient.Client.LocalEndPoint is not IPEndPoint localEndPoint)
            {
                throw new InvalidOperationException("Failed to resolve local UDP endpoint.");
            }

            _port = localEndPoint.Port;
            _receiveTask = Task.Run(() => ReceiveLoop(_udpClient, _cts.Token), _cts.Token);
            IsRunning = true;
        }

        Logger.Information("UDP local listener started on {Port}", Port);
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
        }

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
}
