using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Net;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Core.Services;
using Core.Services.DeviceCommunication;
using Core.Services.DeviceCommunication.Discovery;
using PluginCore;
using Serilog;
using Serilog.Core;

namespace Core.ViewModel.Pages.device;

public partial class DeviceCommunicationPageViewModel : ObservableObject, IDisposable
{
    private static readonly ILogger Logger = LogManager.Logger.ForContext<DeviceCommunicationPageViewModel>();
    private readonly IDeviceDiscoveryService _deviceDiscoveryService;
    private readonly ILocalDataListener _localDataListener;
    private readonly ILocalDataBusService _localDataBusService;
    private readonly IToastService _toastService;
    private readonly IDisposable _chatSubscription;
    private readonly Dictionary<string, DeviceConversationItem> _conversationLookup = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DeviceModel> _trackedDevices = new(StringComparer.Ordinal);
    private readonly ObservableCollection<DeviceChatMessageItem> _emptyMessages = [];
    private bool _disposed;

    public ObservableCollection<DeviceConversationItem> Conversations { get; } = [];

    public ObservableCollection<DeviceChatMessageItem> CurrentMessages =>
        SelectedConversation?.Messages ?? _emptyMessages;

    public string CurrentConversationTitle => SelectedConversation?.DisplayName ?? "Device Chat";

    public string CurrentConversationSubtitle => SelectedConversation is null
        ? "Select a device to start chatting"
        : $"{SelectedConversation.StatusText} - {SelectedConversation.AddressText}";

    public bool HasConversationSelected => SelectedConversation is not null;
    public bool ShowConversationPlaceholder => !HasConversationSelected;
    public bool HasConversations => Conversations.Count > 0;
    public bool HasNoConversations => !HasConversations;

    public DeviceCommunicationPageViewModel(
        IDeviceDiscoveryService deviceDiscoveryService,
        ILocalDataListener localDataListener,
        ILocalDataBusService localDataBusService,
        IToastService toastService)
    {
        _deviceDiscoveryService = deviceDiscoveryService;
        _localDataListener = localDataListener;
        _localDataBusService = localDataBusService;
        _toastService = toastService;

        _deviceDiscoveryService.Devices.CollectionChanged += OnDiscoveredDevicesCollectionChanged;
        _chatSubscription = _localDataBusService.Subscribe<LocalDataChatMessage>(OnChatMessageReceived);
        SyncConversationsFromDiscovery();
    }

    [ObservableProperty]
    private DeviceConversationItem? _selectedConversation;

    [ObservableProperty]
    private string _messageText = string.Empty;

    [ObservableProperty]
    private bool _isSending;

    [RelayCommand(CanExecute = nameof(CanSendMessage))]
    private async Task SendMessageAsync()
    {
        var conversation = SelectedConversation;
        var text = MessageText.Trim();
        if (conversation is null || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var message = new DeviceChatMessageItem(text, isOutgoing: true, DateTimeOffset.Now)
        {
            IsPending = true
        };

        conversation.Messages.Add(message);
        conversation.SetLastMessage(text, message.Timestamp);
        conversation.UnreadCount = 0;
        MessageText = string.Empty;
        SortConversations();

        IsSending = true;
        try
        {
            await SendToConversationAsync(conversation, text);
            message.IsPending = false;
            message.IsFailed = false;
        }
        catch (Exception ex)
        {
            message.IsPending = false;
            message.IsFailed = true;
            Logger.Warning(ex, "Send chat message failed. DeviceId={DeviceId}", conversation.DeviceId);
            _toastService.Show("Device Chat", $"Send failed: {ex.Message}", NotificationType.Error);
        }
        finally
        {
            IsSending = false;
        }
    }

    private bool CanSendMessage()
    {
        return !IsSending &&
               SelectedConversation is not null &&
               !string.IsNullOrWhiteSpace(MessageText);
    }

    private async Task SendToConversationAsync(DeviceConversationItem conversation, string text)
    {
        if (string.IsNullOrWhiteSpace(conversation.DeviceId))
        {
            throw new InvalidOperationException("Invalid target device identity.");
        }

        var protocol = conversation.SupportQuic && conversation.QuicPort > 0
            ? LocalDataTransportProtocol.Quic
            : LocalDataTransportProtocol.Tcp;
        var port = protocol == LocalDataTransportProtocol.Quic ? conversation.QuicPort : conversation.TcpPort;

        if (port <= 0 || conversation.Address == IPAddress.None)
        {
            throw new InvalidOperationException("Invalid target address or port.");
        }

        try
        {
            await SendMessageCoreAsync(conversation, text, protocol, port);
        }
        catch (Exception ex) when (protocol == LocalDataTransportProtocol.Quic && conversation.TcpPort > 0)
        {
            Logger.Warning(ex, "Send chat message over QUIC failed, fallback to TCP. DeviceId={DeviceId}",
                conversation.DeviceId);
            await SendMessageCoreAsync(conversation, text, LocalDataTransportProtocol.Tcp, conversation.TcpPort);
        }
    }

    private async Task SendMessageCoreAsync(
        DeviceConversationItem conversation,
        string text,
        LocalDataTransportProtocol protocol,
        int port)
    {
        var remoteEndPoint = new IPEndPoint(conversation.Address, port);
        var sendContext = new LocalDataBusSendContext(
            _localDataListener,
            protocol,
            remoteEndPoint,
            conversation.DeviceId);

        await _localDataBusService.PublishAsync(sendContext, new LocalDataChatMessage(text));
    }

    private void OnDiscoveredDevicesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        Dispatcher.UIThread.Post(SyncConversationsFromDiscovery);
    }

    private void OnTrackedDevicePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_disposed || sender is not DeviceModel device)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed)
            {
                return;
            }

            UpsertConversation(device);
            OnPropertyChanged(nameof(CurrentConversationTitle));
            OnPropertyChanged(nameof(CurrentConversationSubtitle));
            SortConversations();
        });
    }

    private void OnChatMessageReceived(object? sender, LocalDataBusMessageReceivedEventArgs<LocalDataChatMessage> e)
    {
        if (_disposed)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed)
            {
                return;
            }

            var conversation = FindConversationByAddress(e.RemoteEndPoint.Address);
            if (conversation is null)
            {
                Logger.Debug(
                    "Drop chat message because sender device is not discovered. RemoteEndPoint={RemoteEndPoint}",
                    e.RemoteEndPoint);
                return;
            }

            var text = e.Message.Text.Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            var timestamp = e.TimestampUtc.ToLocalTime();
            conversation.Messages.Add(new DeviceChatMessageItem(text, isOutgoing: false, timestamp));
            conversation.SetLastMessage(text, timestamp);

            if (!ReferenceEquals(SelectedConversation, conversation))
            {
                conversation.UnreadCount++;
            }
            else
            {
                conversation.UnreadCount = 0;
            }

            SortConversations();
        });
    }

    private DeviceConversationItem? FindConversationByAddress(IPAddress remoteAddress)
    {
        var normalized = NormalizeAddress(remoteAddress);
        return Conversations.FirstOrDefault(conversation =>
            NormalizeAddress(conversation.Address).Equals(normalized));
    }

    private void SyncConversationsFromDiscovery()
    {
        var discovered = _deviceDiscoveryService.Devices
            .Where(device => !string.IsNullOrWhiteSpace(device.Id))
            .ToList();

        var discoveredIds = new HashSet<string>(discovered.Select(device => device.Id), StringComparer.Ordinal);

        foreach (var (deviceId, trackedDevice) in _trackedDevices.Where(pair => !discoveredIds.Contains(pair.Key)).ToList())
        {
            trackedDevice.PropertyChanged -= OnTrackedDevicePropertyChanged;
            _trackedDevices.Remove(deviceId);
        }

        foreach (var device in discovered)
        {
            if (!_trackedDevices.TryAdd(device.Id, device))
            {
                if (!ReferenceEquals(_trackedDevices[device.Id], device))
                {
                    _trackedDevices[device.Id].PropertyChanged -= OnTrackedDevicePropertyChanged;
                    _trackedDevices[device.Id] = device;
                }
            }

            device.PropertyChanged -= OnTrackedDevicePropertyChanged;
            device.PropertyChanged += OnTrackedDevicePropertyChanged;
            UpsertConversation(device);
        }

        foreach (var conversation in Conversations)
        {
            conversation.IsOnline = discoveredIds.Contains(conversation.DeviceId);
        }

        if (SelectedConversation is null && Conversations.Count > 0)
        {
            SelectedConversation = Conversations[0];
        }

        OnPropertyChanged(nameof(CurrentConversationTitle));
        OnPropertyChanged(nameof(CurrentConversationSubtitle));
        OnPropertyChanged(nameof(HasConversations));
        OnPropertyChanged(nameof(HasNoConversations));
        SortConversations();
    }

    private void UpsertConversation(DeviceModel device)
    {
        if (!_conversationLookup.TryGetValue(device.Id, out var conversation))
        {
            conversation = new DeviceConversationItem(device.Id);
            _conversationLookup[device.Id] = conversation;
            Conversations.Add(conversation);
        }

        conversation.ApplyDevice(device);
    }

    private void SortConversations()
    {
        var sorted = Conversations
            .OrderByDescending(conversation => conversation.LastMessageAt ?? DateTimeOffset.MinValue)
            .ThenByDescending(conversation => conversation.IsOnline)
            .ThenBy(conversation => conversation.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        for (var index = 0; index < sorted.Count; index++)
        {
            var target = sorted[index];
            var current = Conversations.IndexOf(target);
            if (current >= 0 && current != index)
            {
                Conversations.Move(current, index);
            }
        }
    }

    private static IPAddress NormalizeAddress(IPAddress address)
    {
        return address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
    }

    partial void OnSelectedConversationChanged(DeviceConversationItem? value)
    {
        if (value is not null)
        {
            value.UnreadCount = 0;
        }

        OnPropertyChanged(nameof(HasConversationSelected));
        OnPropertyChanged(nameof(ShowConversationPlaceholder));
        OnPropertyChanged(nameof(CurrentMessages));
        OnPropertyChanged(nameof(CurrentConversationTitle));
        OnPropertyChanged(nameof(CurrentConversationSubtitle));
        SendMessageCommand.NotifyCanExecuteChanged();
    }

    partial void OnMessageTextChanged(string value)
    {
        SendMessageCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsSendingChanged(bool value)
    {
        SendMessageCommand.NotifyCanExecuteChanged();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _chatSubscription.Dispose();
        _deviceDiscoveryService.Devices.CollectionChanged -= OnDiscoveredDevicesCollectionChanged;
        foreach (var trackedDevice in _trackedDevices.Values)
        {
            trackedDevice.PropertyChanged -= OnTrackedDevicePropertyChanged;
        }

        _trackedDevices.Clear();
    }
}

public partial class DeviceConversationItem : ObservableObject
{
    public DeviceConversationItem(string deviceId)
    {
        DeviceId = deviceId;
    }

    public string DeviceId { get; }
    public ObservableCollection<DeviceChatMessageItem> Messages { get; } = [];

    [ObservableProperty]
    private string _displayName = "Unknown Device";

    [ObservableProperty]
    private IPAddress _address = IPAddress.None;

    [ObservableProperty]
    private int _tcpPort;

    [ObservableProperty]
    private int _quicPort;

    [ObservableProperty]
    private bool _supportQuic;

    [ObservableProperty]
    private bool _isOnline;

    [ObservableProperty]
    private string _lastMessagePreview = "No messages";

    [ObservableProperty]
    private DateTimeOffset? _lastMessageAt;

    [ObservableProperty]
    private int _unreadCount;

    public string AddressText => Address == IPAddress.None ? "Unknown Address" : Address.ToString();
    public string StatusText => IsOnline ? "Online" : "Offline";
    public bool HasUnread => UnreadCount > 0;
    public string UnreadCountText => UnreadCount > 99 ? "99+" : UnreadCount.ToString();
    public string LastMessageTimeText => LastMessageAt?.ToLocalTime().ToString("HH:mm") ?? string.Empty;

    public void ApplyDevice(DeviceModel device)
    {
        DisplayName = device.DisplayName;
        Address = device.Address;
        TcpPort = device.TcpPort;
        QuicPort = device.QuicPort;
        SupportQuic = device.SupportQuic;
        IsOnline = true;
    }

    public void SetLastMessage(string message, DateTimeOffset messageTime)
    {
        LastMessagePreview = BuildPreview(message);
        LastMessageAt = messageTime;
    }

    partial void OnAddressChanged(IPAddress value)
    {
        OnPropertyChanged(nameof(AddressText));
    }

    partial void OnIsOnlineChanged(bool value)
    {
        OnPropertyChanged(nameof(StatusText));
    }

    partial void OnUnreadCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasUnread));
        OnPropertyChanged(nameof(UnreadCountText));
    }

    partial void OnLastMessageAtChanged(DateTimeOffset? value)
    {
        OnPropertyChanged(nameof(LastMessageTimeText));
    }

    private static string BuildPreview(string message)
    {
        var singleLine = message.ReplaceLineEndings(" ").Trim();
        if (singleLine.Length <= 36)
        {
            return singleLine;
        }

        return $"{singleLine[..36]}...";
    }
}

public partial class DeviceChatMessageItem : ObservableObject
{
    public DeviceChatMessageItem(string text, bool isOutgoing, DateTimeOffset timestamp)
    {
        _text = text;
        _isOutgoing = isOutgoing;
        _timestamp = timestamp;
    }

    [ObservableProperty]
    private string _text;

    [ObservableProperty]
    private bool _isOutgoing;

    [ObservableProperty]
    private DateTimeOffset _timestamp;

    [ObservableProperty]
    private bool _isPending;

    [ObservableProperty]
    private bool _isFailed;

    public bool IsIncoming => !IsOutgoing;
    public string TimeText => Timestamp.ToLocalTime().ToString("HH:mm");
    public string StateText => IsFailed ? "Failed" : IsPending ? "Sending..." : string.Empty;
    public bool HasState => !string.IsNullOrEmpty(StateText);

    partial void OnIsOutgoingChanged(bool value)
    {
        OnPropertyChanged(nameof(IsIncoming));
    }

    partial void OnTimestampChanged(DateTimeOffset value)
    {
        OnPropertyChanged(nameof(TimeText));
    }

    partial void OnIsPendingChanged(bool value)
    {
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(HasState));
    }

    partial void OnIsFailedChanged(bool value)
    {
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(HasState));
    }
}
