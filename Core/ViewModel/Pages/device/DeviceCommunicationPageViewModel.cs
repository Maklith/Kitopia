using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Core.ViewModel.Pages.device;

public partial class DeviceCommunicationPageViewModel : ObservableObject, IDisposable
{
    public DeviceDiscoveryPageViewModel Discovery { get; }
    public DeviceChatPageViewModel Chat { get; }

    [ObservableProperty]
    private int _selectedTabIndex;

    public DeviceCommunicationPageViewModel(
        DeviceDiscoveryPageViewModel discovery,
        DeviceChatPageViewModel chat)
    {
        Discovery = discovery;
        Chat = chat;
    }

    public void Dispose()
    {
        Chat.Dispose();
        Discovery.Dispose();
    }
}
