using Core.Services.DeviceCommunication;
using Core.Services.DeviceCommunication.Application;
using Core.Services.DeviceCommunication.Codecs;
using Core.Services.DeviceCommunication.Handlers;
using Core.Services.DeviceCommunication.Platform;
using Core.Services.DeviceCommunication.Protocol;
using Core.Services.DeviceCommunication.Security;
using Core.Services.DeviceCommunication.Sessions;
using Core.Services.Interfaces;
using Core.ViewModel.Pages.device;
using Kitopia.DeviceCommunication.Discovery;
using Kitopia.DeviceCommunication.Identity;
using Kitopia.Mobile.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;

namespace Kitopia.Mobile.Services;

public sealed class MobileCommunicationBootstrapper
{
    public MobileCommunicationBootstrapper()
    {
        TopLevelContext = new MobileTopLevelContext();

        var services = new ServiceCollection();

        services.AddSingleton(TopLevelContext);
        services.AddSingleton<MobileConfigService>();
        services.AddSingleton(_ => MobilePlatformRuntime.Current);
        services.AddSingleton<IDeviceIdentityStore, MobileDeviceIdentityStore>();
        services.AddSingleton<IDeviceCommunicationSettings, MobileDeviceCommunicationSettings>();
        services.AddSingleton<Kitopia.DeviceCommunication.Transport.ILocalDataEndpointProvider, LocalDataEndpointProvider>();
        services.AddSingleton<IToastService, MobileToastService>();
        services.AddSingleton<INavigationService, MobileNavigationService>();
        services.AddSingleton<IChatPlatformService>(sp =>
            new MobileChatPlatformService(sp.GetRequiredService<MobileTopLevelContext>()));

        services.AddSingleton<IDeviceDiscoveryService>(sp => new UiThreadDeviceDiscoveryService(
            new DeviceDiscoveryService(
                sp.GetRequiredService<IDeviceCommunicationSettings>(),
                sp.GetRequiredService<IDeviceIdentityStore>(),
                sp.GetRequiredService<Kitopia.DeviceCommunication.Transport.ILocalDataEndpointProvider>())));

        services.AddSingleton<DeviceTransportSecurity>();
        services.AddSingleton<ILocalDataListener, LocalDataListenerHost>();
        services.AddSingleton<ProtocolSession>();
        services.AddSingleton<ProtocolSender>();
        services.AddSingleton<IFileTransferSessionStore, FileTransferSessionStore>();
        services.AddSingleton<ImageTransferPolicy>();
        services.AddSingleton<MessageCodecRegistry>();
        services.AddSingleton<DeviceTransportService>();
        services.AddSingleton<FileTransferPayloadHandler>();
        services.AddSingleton<DeviceMessageDispatcher>();
        services.AddSingleton<IncomingMessageBuffer>();
        services.AddSingleton<IMessageAppService, MessageAppService>();
        services.AddSingleton<IIncomingMessageSink>(sp => sp.GetRequiredService<IncomingMessageBuffer>());
        services.AddSingleton<IDeviceCommunication, Core.Services.DeviceCommunication.DeviceCommunication>();

        services.AddSingleton<IMobileCommunicationRuntime>(sp =>
            MobilePlatformRuntime.Current.WrapCommunicationRuntime(
                new MobileCommunicationRuntime(sp.GetRequiredService<ILocalDataListener>())));
        services.AddSingleton<MobileDeviceCommunicationHost>();

        services.AddSingleton(sp => new DeviceCommunicationPageViewModel(
            sp.GetRequiredService<IDeviceDiscoveryService>(),
            sp.GetRequiredService<IMessageAppService>(),
            sp.GetRequiredService<IChatPlatformService>(),
            sp.GetRequiredService<IDeviceCommunicationSettings>(),
            sp.GetRequiredService<IToastService>(),
            clipboardService: null,
            autoSelectFirstConversation: false));
        services.AddSingleton<MainViewModel>();

        var provider = services.BuildServiceProvider();
        ServiceManager.Services = provider;

        var identity = provider.GetRequiredService<IDeviceIdentityStore>().EnsureIdentity();

        // The Core message codec derives the outgoing senderId from ConfigManger.Config.devicePrivateKey
        // (a desktop global). Seed it with the mobile identity so senderId resolves correctly on mobile.
        if (!Core.Services.Config.ConfigManger.Configs.ContainsKey("KitopiaConfig"))
        {
            Core.Services.Config.ConfigManger.Configs["KitopiaConfig"] =
                new Core.Services.Config.KitopiaConfig { Name = "KitopiaConfig" };
        }
        Core.Services.Config.ConfigManger.Config.devicePrivateKey = identity.PrivateKey;

        MainViewModel = provider.GetRequiredService<MainViewModel>();
    }

    public MobileTopLevelContext TopLevelContext { get; }
    public MainViewModel MainViewModel { get; }
}
