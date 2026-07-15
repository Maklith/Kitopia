using Kitopia.Feature.DeviceCommunication.Transport;
using Microsoft.Extensions.DependencyInjection;

namespace Kitopia.Mobile.Services;

public sealed class MobileLocalDataEndpointProvider : ILocalDataEndpointProvider
{
    private readonly IServiceProvider _serviceProvider;

    public MobileLocalDataEndpointProvider(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public int TcpPort => _serviceProvider.GetRequiredService<ILocalDataListener>().TcpPort;
}
