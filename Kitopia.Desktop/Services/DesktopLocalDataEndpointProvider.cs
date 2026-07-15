using System;
using Kitopia.Feature.DeviceCommunication.Transport;
using Microsoft.Extensions.DependencyInjection;

namespace Kitopia.Desktop.Services;

public sealed class DesktopLocalDataEndpointProvider : ILocalDataEndpointProvider
{
    private readonly IServiceProvider _serviceProvider;

    public DesktopLocalDataEndpointProvider(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public int TcpPort => _serviceProvider.GetRequiredService<ILocalDataListener>().TcpPort;
}
