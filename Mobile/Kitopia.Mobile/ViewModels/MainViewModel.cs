using CommunityToolkit.Mvvm.ComponentModel;
using Core.ViewModel.Pages.device;
using Kitopia.Mobile.Services;

namespace Kitopia.Mobile.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IAsyncDisposable
{
    private readonly MobileDeviceCommunicationHost _host;

    public MainViewModel(DeviceCommunicationPageViewModel chat, MobileDeviceCommunicationHost host)
    {
        Chat = chat;
        _host = host;
    }

    public DeviceCommunicationPageViewModel Chat { get; }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _host.StartAsync(cancellationToken);
        Chat.RefreshCurrentConversationView();
    }

    public async Task StopAsync()
    {
        await _host.StopAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        Chat.Dispose();
    }
}
