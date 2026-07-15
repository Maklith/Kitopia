using Kitopia.Feature.DeviceCommunication;
using Kitopia.Feature.DeviceCommunication.Application;
using Kitopia.Feature.DeviceCommunication.Codecs;
using Kitopia.Feature.DeviceCommunication.Discovery;
using Kitopia.Feature.DeviceCommunication.Identity;
using Kitopia.Feature.DeviceCommunication.Protocol;
using Kitopia.Feature.DeviceCommunication.Security;
using Kitopia.Feature.DeviceCommunication.Sessions;
using Kitopia.Feature.DeviceCommunication.Transport;
using Microsoft.Extensions.DependencyInjection;

namespace Kitopia.Mobile.Services;

public static class MobileCommunicationServiceCollectionExtensions
{
    public static IServiceCollection AddMobileCommunicationRuntime(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IDeviceIdentityStore, MobileDeviceIdentityStore>();
        services.AddSingleton<IDeviceIdentityProvider, DeviceIdentityProvider>();
        services.AddSingleton<IDeviceCommunicationSettings, MobileDeviceCommunicationSettings>();
        services.AddSingleton<ILocalDataEndpointProvider, MobileLocalDataEndpointProvider>();

        services.AddSingleton<DeviceDiscoveryService>();
        services.AddSingleton<IDeviceDiscoveryService>(serviceProvider =>
            new UiThreadDeviceDiscoveryService(
                serviceProvider.GetRequiredService<DeviceDiscoveryService>()));
        services.AddSingleton<IRemoteIdentityResolver, MobileRemoteIdentityResolver>();

        services.AddSingleton<IDeviceCertificateStoragePolicy>(
            EphemeralDeviceCertificateStoragePolicy.Instance);
        services.AddSingleton<DeviceTransportSecurity>();
        services.AddSingleton<IFileTransferSessionStore, FileTransferSessionStore>();
        services.AddSingleton<MessageCodecRegistry>();
        services.AddSingleton<IncomingMessageBuffer>();
        services.AddSingleton<IIncomingMessageSink>(serviceProvider =>
            serviceProvider.GetRequiredService<IncomingMessageBuffer>());
        services.AddSingleton<FileTransferPayloadHandler>();
        services.AddSingleton<DeviceMessageDispatcher>();
        services.AddSingleton<ProtocolSession>(serviceProvider =>
            new ProtocolSession(
                serviceProvider.GetRequiredService<DeviceMessageDispatcher>().DispatchAsync));
        services.AddSingleton<ILocalDataListener, LocalDataListenerHost>();
        services.AddSingleton<DeviceTransportService>();
        services.AddSingleton<IMessageAppService, MessageAppService>();

        services.AddSingleton<IMobileCommunicationRuntime>(serviceProvider =>
            serviceProvider.GetRequiredService<IMobilePlatformRuntimeFeatures>().WrapCommunicationRuntime(
                new MobileCommunicationRuntime(
                    serviceProvider.GetRequiredService<ILocalDataListener>())));
        services.AddSingleton<MobileDeviceCommunicationHost>();

        return services;
    }
}
