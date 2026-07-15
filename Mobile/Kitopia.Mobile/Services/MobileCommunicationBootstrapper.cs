using Kitopia.Feature.DeviceCommunication.Application;
using Kitopia.Feature.Avalonia.DeviceCommunication.ViewModels;
using Kitopia.Feature.DeviceCommunication.Discovery;
using Kitopia.Feature.DeviceCommunication.Identity;
using Kitopia.Mobile.ViewModels;
using Microsoft.Extensions.DependencyInjection;

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
        services.AddMobileCommunicationRuntime();
        services.AddSingleton<IChatNotificationSink, MobileChatNotificationSink>();
        services.AddSingleton<IChatAttachmentStore, MobileChatAttachmentStore>();
        services.AddSingleton<IChatClipboardService, MobileChatClipboardService>();
        services.AddSingleton<IChatPlatformService>(sp =>
            new MobileChatPlatformService(sp.GetRequiredService<MobileTopLevelContext>()));

        services.AddSingleton(sp => new DeviceCommunicationPageViewModel(
            sp.GetRequiredService<IDeviceDiscoveryService>(),
            sp.GetRequiredService<IMessageAppService>(),
            sp.GetRequiredService<IChatAttachmentStore>(),
            sp.GetRequiredService<IChatPlatformService>(),
            sp.GetRequiredService<IDeviceCommunicationSettings>(),
            sp.GetRequiredService<IChatNotificationSink>(),
            sp.GetRequiredService<IChatClipboardService>(),
            autoSelectFirstConversation: false));
        services.AddSingleton<MainViewModel>();

        var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IDeviceIdentityStore>().EnsureIdentity();

        MainViewModel = provider.GetRequiredService<MainViewModel>();
    }

    public MobileTopLevelContext TopLevelContext { get; }
    public MainViewModel MainViewModel { get; }
}
