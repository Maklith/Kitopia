using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kitopia.DeviceCommunication.Discovery;

namespace Kitopia.Mobile.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IAsyncDisposable
{
    private readonly Mobile.Services.MobileDeviceCommunicationHost _host;

    public MainViewModel(
        DeviceListViewModel deviceList,
        ConversationViewModel conversation,
        Mobile.Services.MobileDeviceCommunicationHost host)
    {
        DeviceList = deviceList;
        Conversation = conversation;
        _host = host;

        DeviceList.SelectedDeviceChanged += OnSelectedDeviceChanged;
        DeviceList.Devices.CollectionChanged += OnDevicesCollectionChanged;
        BackToDevicesCommand = new RelayCommand(BackToDevices);
    }

    public DeviceListViewModel DeviceList { get; }
    public ConversationViewModel Conversation { get; }
    public IRelayCommand BackToDevicesCommand { get; }

    [ObservableProperty]
    private bool _isConversationOpen;

    [ObservableProperty]
    private bool _isRunning;

    public string TitleText => IsConversationOpen
        ? (DeviceList.SelectedDevice?.DisplayName ?? "Conversation")
        : "Kitopia Mobile";

    public bool ShowDeviceList => !IsConversationOpen;

    public string SubtitleText => IsConversationOpen
        ? (DeviceList.SelectedDevice?.PreferredTransportAddress.ToString() ?? string.Empty)
        : $"LAN discovery {(IsRunning ? "active" : "paused")}";

    public string EmptyDevicesText => IsRunning
        ? "Waiting for devices on the same LAN."
        : "Communication will start when the app becomes active.";

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _host.StartAsync(cancellationToken);
        await Conversation.StartAsync(cancellationToken);
        IsRunning = true;
        Conversation.SelectConversation(DeviceList.SelectedDevice?.Id);
        OnPropertyChanged(nameof(SubtitleText));
        OnPropertyChanged(nameof(EmptyDevicesText));
    }

    public async Task StopAsync()
    {
        await Conversation.StopAsync();
        await _host.StopAsync();
        IsRunning = false;
        OnPropertyChanged(nameof(SubtitleText));
        OnPropertyChanged(nameof(EmptyDevicesText));
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }

    partial void OnIsConversationOpenChanged(bool value)
    {
        _ = value;
        OnPropertyChanged(nameof(TitleText));
        OnPropertyChanged(nameof(ShowDeviceList));
        OnPropertyChanged(nameof(SubtitleText));
    }

    partial void OnIsRunningChanged(bool value)
    {
        _ = value;
        OnPropertyChanged(nameof(SubtitleText));
        OnPropertyChanged(nameof(EmptyDevicesText));
    }

    private void BackToDevices()
    {
        DeviceList.SelectedDevice = null;
    }

    private void OnSelectedDeviceChanged(DiscoveredDevice? device)
    {
        Conversation.SelectConversation(device?.Id);
        IsConversationOpen = device is not null;
        OnPropertyChanged(nameof(TitleText));
        OnPropertyChanged(nameof(SubtitleText));
    }

    private void OnDevicesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _ = sender;
        _ = e;
        OnPropertyChanged(nameof(EmptyDevicesText));
    }
}
