using Core.Services.Interfaces;
using Core.ViewModel.Pages.device;
using Kitopia.DeviceCommunication;
using Kitopia.DeviceCommunication.Application;
using Kitopia.DeviceCommunication.Codecs;
using Kitopia.DeviceCommunication.Diagnostics;
using Kitopia.DeviceCommunication.Discovery;
using Kitopia.DeviceCommunication.Identity;
using Kitopia.DeviceCommunication.Protocol;
using Kitopia.DeviceCommunication.Security;
using Kitopia.DeviceCommunication.Sessions;
using Kitopia.DeviceCommunication.Transport;
using Kitopia.Mobile.ViewModels;

namespace Kitopia.Mobile.Services;

public sealed class MobileCommunicationBootstrapper
{
    private const string LogCategory = "MobileBootstrapper";

    public MobileCommunicationBootstrapper()
    {
        TopLevelContext = new MobileTopLevelContext();

        var configService = new MobileConfigService();
        var identityStore = new MobileDeviceIdentityStore();
        var ensuredIdentity = identityStore.EnsureIdentity();
        DeviceCommunicationDiagnostics.Info(
            LogCategory,
            $"Identity ready. DeviceId={ShortId(ensuredIdentity.IdHash)} PublicKey={ShortId(ensuredIdentity.PublicKey)}.");
        var codecRegistry = new MessageCodecRegistry(new DeviceIdentityProvider(ensuredIdentity.PublicKey));
        var incomingMessageBuffer = new IncomingMessageBuffer();
        var fileTransferSessionStore = new FileTransferSessionStore();
        ILocalDataListener? listener = null;
        var endpointProvider = new MobileLocalDataEndpointProvider(() => listener);
        var innerDiscoveryService = new DeviceDiscoveryService(
            new MobileDeviceCommunicationSettings(configService),
            identityStore,
            endpointProvider);
        var discoveryService = new UiThreadDeviceDiscoveryService(innerDiscoveryService);
        var remoteIdentityResolver = new MobileRemoteIdentityResolver(innerDiscoveryService);
        var dispatcher = new DeviceMessageDispatcher(
            codecRegistry,
            incomingMessageBuffer,
            new FileTransferPayloadHandler(incomingMessageBuffer, fileTransferSessionStore));
        var protocolSession = new ProtocolSession((envelope, payload, cancellationToken) =>
            dispatcher.DispatchAsync(envelope, payload, cancellationToken));
        listener = new LocalDataListenerHost(
            protocolSession,
            new DeviceTransportSecurity(identityStore),
            remoteIdentityResolver);

        var messageService = new Kitopia.DeviceCommunication.Application.MessageAppService(
            codecRegistry,
            new DeviceTransportService(listener, innerDiscoveryService),
            incomingMessageBuffer,
            fileTransferSessionStore);

        var filePickerService = new AvaloniaMobileFilePickerService(TopLevelContext);
        var clipboardService = new AvaloniaClipboardService(TopLevelContext);
        var runtime = MobilePlatformRuntime.Current.WrapCommunicationRuntime(new MobileCommunicationRuntime(listener));
        var host = new MobileDeviceCommunicationHost(runtime, discoveryService);

        DeviceDiscoveryPageViewModel = new DeviceDiscoveryPageViewModel(discoveryService, configService);

        MainViewModel = new MainViewModel(
            new DeviceListViewModel(discoveryService),
            new ConversationViewModel(messageService, filePickerService, clipboardService),
            host);

        DeviceCommunicationDiagnostics.Info(LogCategory, "Mobile communication graph initialized.");
    }

    public MobileTopLevelContext TopLevelContext { get; }
    public MainViewModel MainViewModel { get; }
    public DeviceDiscoveryPageViewModel DeviceDiscoveryPageViewModel { get; }

    private sealed class DeviceIdentityProvider : IDeviceIdentityProvider
    {
        private readonly string _publicKey;

        public DeviceIdentityProvider(string publicKey)
        {
            _publicKey = publicKey;
        }

        public string? GetLocalPublicKey()
        {
            return _publicKey;
        }
    }

    private static string ShortId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "n/a";
        }

        const int visibleLength = 10;
        return value.Length <= visibleLength ? value : value[..visibleLength];
    }
}
