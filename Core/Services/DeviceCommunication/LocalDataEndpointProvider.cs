using Microsoft.Extensions.DependencyInjection;
using Kitopia.DeviceCommunication.Transport;

namespace Core.Services.DeviceCommunication;

public sealed class LocalDataEndpointProvider : ILocalDataEndpointProvider
{
    private readonly IServiceProvider _serviceProvider;

    public LocalDataEndpointProvider(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public int TcpPort => _serviceProvider.GetRequiredService<ILocalDataListener>().TcpPort;
}
