using Kitopia.DeviceCommunication.Transport;

namespace Kitopia.Mobile.Services;

public sealed class MobileLocalDataEndpointProvider : ILocalDataEndpointProvider
{
    private readonly Func<ILocalDataListener?> _listenerAccessor;

    public MobileLocalDataEndpointProvider(Func<ILocalDataListener?> listenerAccessor)
    {
        _listenerAccessor = listenerAccessor;
    }

    public int TcpPort => _listenerAccessor()?.TcpPort ?? 0;
}
