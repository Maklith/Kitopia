using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kitopia.DeviceCommunication.Application;
using Kitopia.DeviceCommunication.Messages.Chat;
using Kitopia.DeviceCommunication.Messages.Clipboard;
using Kitopia.Mobile.Services;

namespace Kitopia.Mobile.ViewModels;

public sealed partial class ConversationViewModel : ObservableObject
{
    private readonly IMessageAppService _messageAppService;
    private readonly IMobileFilePickerService _filePickerService;
    private readonly IMobileClipboardService _clipboardService;
    private readonly Dictionary<string, ObservableCollection<MobileMessageItemViewModel>> _messageMap = new(StringComparer.Ordinal);
    private CancellationTokenSource? _receiveCts;
    private Task? _receiveTask;

    public ConversationViewModel(
        IMessageAppService messageAppService,
        IMobileFilePickerService filePickerService,
        IMobileClipboardService clipboardService)
    {
        _messageAppService = messageAppService;
        _filePickerService = filePickerService;
        _clipboardService = clipboardService;
        SendTextCommand = new AsyncRelayCommand(SendTextAsync, CanSendText);
        SendClipboardCommand = new AsyncRelayCommand(SendClipboardAsync, CanShare);
        SendFileCommand = new AsyncRelayCommand(SendFileAsync, CanShare);
        SendImageCommand = new AsyncRelayCommand(SendImageAsync, CanShare);
        CancelTransferCommand = new AsyncRelayCommand<MobileMessageItemViewModel?>(CancelTransferAsync);
        AcceptIncomingOfferCommand = new AsyncRelayCommand<MobileMessageItemViewModel?>(AcceptIncomingOfferAsync);
        RejectIncomingOfferCommand = new AsyncRelayCommand<MobileMessageItemViewModel?>(RejectIncomingOfferAsync);
    }

    [ObservableProperty]
    private ObservableCollection<MobileMessageItemViewModel> _messages = [];

    [ObservableProperty]
    private string? _selectedConversationId;

    [ObservableProperty]
    private string _draftText = string.Empty;

    public IAsyncRelayCommand SendTextCommand { get; }
    public IAsyncRelayCommand SendClipboardCommand { get; }
    public IAsyncRelayCommand SendFileCommand { get; }
    public IAsyncRelayCommand SendImageCommand { get; }
    public IAsyncRelayCommand<MobileMessageItemViewModel?> CancelTransferCommand { get; }
    public IAsyncRelayCommand<MobileMessageItemViewModel?> AcceptIncomingOfferCommand { get; }
    public IAsyncRelayCommand<MobileMessageItemViewModel?> RejectIncomingOfferCommand { get; }

    public bool HasSelectedConversation => !string.IsNullOrWhiteSpace(SelectedConversationId);
    public string HeaderText => string.IsNullOrWhiteSpace(SelectedConversationId) ? "No conversation selected" : SelectedConversationId;
    public string SubheaderText => string.IsNullOrWhiteSpace(SelectedConversationId) ? "Pick a device to start chatting." : $"{Messages.Count} items";

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_receiveTask is not null)
        {
            return Task.CompletedTask;
        }

        _receiveCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _receiveTask = Task.Run(() => ReceiveLoopAsync(_receiveCts.Token), _receiveCts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (_receiveCts is null)
        {
            return;
        }

        _messageAppService.UpdateDisplayContext(false, false, null);
        _receiveCts.Cancel();
        if (_receiveTask is not null)
        {
            try
            {
                await _receiveTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _receiveTask = null;
        _receiveCts.Dispose();
        _receiveCts = null;
    }

    public void SelectConversation(string? conversationId)
    {
        SelectedConversationId = conversationId;
        Messages = string.IsNullOrWhiteSpace(conversationId) ? [] : GetMessages(conversationId);
        _messageAppService.UpdateDisplayContext(true, !string.IsNullOrWhiteSpace(conversationId), conversationId);
        OnPropertyChanged(nameof(HeaderText));
        OnPropertyChanged(nameof(SubheaderText));
        OnPropertyChanged(nameof(HasSelectedConversation));
        SendTextCommand.NotifyCanExecuteChanged();
        SendClipboardCommand.NotifyCanExecuteChanged();
        SendFileCommand.NotifyCanExecuteChanged();
        SendImageCommand.NotifyCanExecuteChanged();
    }

    private bool CanSendText()
    {
        return !string.IsNullOrWhiteSpace(SelectedConversationId) && !string.IsNullOrWhiteSpace(DraftText);
    }

    private bool CanShare()
    {
        return !string.IsNullOrWhiteSpace(SelectedConversationId);
    }

    private async Task SendTextAsync()
    {
        var conversationId = SelectedConversationId;
        var text = DraftText.Trim();
        if (string.IsNullOrWhiteSpace(conversationId) || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        DraftText = string.Empty;
        var item = new MobileMessageItemViewModel(conversationId, isOutgoing: true, DateTimeOffset.UtcNow)
        {
            Text = text,
            IsPending = true
        };
        GetMessages(conversationId).Add(item);
        OnPropertyChanged(nameof(SubheaderText));

        try
        {
            await _messageAppService.SendTextChatAsync(conversationId, text);
            item.IsPending = false;
        }
        catch (Exception ex)
        {
            item.IsPending = false;
            item.IsFailed = true;
            item.Reason = ex.Message;
        }
    }

    private async Task SendClipboardAsync()
    {
        var conversationId = SelectedConversationId;
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return;
        }

        var text = (await _clipboardService.GetTextAsync())?.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var item = new MobileMessageItemViewModel(conversationId, isOutgoing: true, DateTimeOffset.UtcNow)
        {
            Text = $"[Clipboard] {text}",
            IsPending = true
        };
        GetMessages(conversationId).Add(item);
        OnPropertyChanged(nameof(SubheaderText));

        try
        {
            await _messageAppService.SendClipboardTextAsync(conversationId, new TextClipboardMessage(conversationId, text));
            item.IsPending = false;
        }
        catch (Exception ex)
        {
            item.IsPending = false;
            item.IsFailed = true;
            item.Reason = ex.Message;
        }
    }

    private Task SendFileAsync()
    {
        return SendPickedFileAsync(MobilePickedFileKind.Any);
    }

    private Task SendImageAsync()
    {
        return SendPickedFileAsync(MobilePickedFileKind.Image);
    }

    private async Task AcceptIncomingOfferAsync(MobileMessageItemViewModel? item)
    {
        if (item is null || item.TransferId is not Guid transferId)
        {
            return;
        }

        var suggestedFileName = string.IsNullOrWhiteSpace(item.FileName) ? transferId.ToString("D") : item.FileName;
        var savePath = await _filePickerService.PickSavePathAsync(suggestedFileName);
        if (string.IsNullOrWhiteSpace(savePath))
        {
            return;
        }

        await _messageAppService.AcceptFileAsync(item.ConversationId, transferId, savePath);
        item.IsHandled = true;
    }

    private async Task RejectIncomingOfferAsync(MobileMessageItemViewModel? item)
    {
        if (item is null || item.TransferId is not Guid transferId)
        {
            return;
        }

        await _messageAppService.RejectFileAsync(item.ConversationId, transferId, "rejected_by_user");
        item.IsHandled = true;
    }

    private async Task CancelTransferAsync(MobileMessageItemViewModel? item)
    {
        if (item is null || item.TransferId is not Guid transferId)
        {
            return;
        }

        await _messageAppService.CancelTransferAsync(item.ConversationId, transferId, "cancelled_by_user");
        item.IsPending = false;
        item.IsReceiving = false;
        item.IsHandled = true;
        item.IsFailed = true;
        item.Reason = "cancelled_by_user";
    }

    partial void OnDraftTextChanged(string value)
    {
        _ = value;
        SendTextCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedConversationIdChanged(string? value)
    {
        _ = value;
        OnPropertyChanged(nameof(HeaderText));
        OnPropertyChanged(nameof(HasSelectedConversation));
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        await foreach (var messageEvent in _messageAppService.ReceiveAsync(cancellationToken))
        {
            HandleMessageEvent(messageEvent);
        }
    }

    private void HandleMessageEvent(DeviceMessageEvent messageEvent)
    {
        var messages = GetMessages(messageEvent.ConversationId);
        switch (messageEvent)
        {
            case ChatMessageReceivedEvent { Message: TextChatMessage textMessage }:
                messages.Add(new MobileMessageItemViewModel(textMessage.ConversationId, isOutgoing: false, messageEvent.TimestampUtc)
                {
                    Text = textMessage.Text
                });
                break;
            case ChatMessageReceivedEvent { Message: TextClipboardMessage clipboardMessage }:
                _ = _clipboardService.SetTextAsync(clipboardMessage.Text);
                messages.Add(new MobileMessageItemViewModel(clipboardMessage.ConversationId, isOutgoing: false, messageEvent.TimestampUtc)
                {
                    Text = $"[Clipboard] {clipboardMessage.Text}"
                });
                break;
            case ChatMessageReceivedEvent { Message: ImageChatMessage }:
                messages.Add(new MobileMessageItemViewModel(messageEvent.ConversationId, isOutgoing: false, messageEvent.TimestampUtc)
                {
                    Text = "[Image]"
                });
                break;
            case FileTransferUpdatedEvent transferEvent:
                HandleTransferEvent(messages, transferEvent);
                break;
        }

        if (string.Equals(SelectedConversationId, messageEvent.ConversationId, StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(SubheaderText));
        }
    }

    private static void HandleTransferEvent(ObservableCollection<MobileMessageItemViewModel> messages, FileTransferUpdatedEvent transferEvent)
    {
        var item = transferEvent.TransferId == Guid.Empty
            ? null
            : messages.LastOrDefault(message => message.TransferId == transferEvent.TransferId);

        if (item is null)
        {
            item = new MobileMessageItemViewModel(
                transferEvent.ConversationId,
                isOutgoing: transferEvent.Direction == FileTransferDirection.Upload,
                transferEvent.TimestampUtc)
            {
                Text = string.IsNullOrWhiteSpace(transferEvent.FileName) ? "[File Transfer]" : $"[File] {transferEvent.FileName}",
                FileName = transferEvent.FileName ?? string.Empty,
                TransferId = transferEvent.TransferId
            };
            messages.Add(item);
        }

        item.TransferId = transferEvent.TransferId;
        item.ApplyTransferUpdate(transferEvent);
    }

    private ObservableCollection<MobileMessageItemViewModel> GetMessages(string conversationId)
    {
        if (!_messageMap.TryGetValue(conversationId, out var messages))
        {
            messages = [];
            _messageMap[conversationId] = messages;
        }

        return messages;
    }

    private async Task SendPickedFileAsync(MobilePickedFileKind kind)
    {
        var conversationId = SelectedConversationId;
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return;
        }

        var pickedFile = await _filePickerService.PickFileToSendAsync(kind);
        if (pickedFile is null)
        {
            return;
        }

        await using var disposable = pickedFile;
        await using var stream = await pickedFile.OpenReadAsync();

        var transferId = Guid.NewGuid();
        var item = new MobileMessageItemViewModel(conversationId, isOutgoing: true, DateTimeOffset.UtcNow)
        {
            Text = kind == MobilePickedFileKind.Image ? $"[Image] {pickedFile.DisplayName}" : $"[File] {pickedFile.DisplayName}",
            FileName = pickedFile.DisplayName,
            TransferId = transferId,
            IsPending = true
        };
        GetMessages(conversationId).Add(item);
        OnPropertyChanged(nameof(SubheaderText));

        try
        {
            if (kind == MobilePickedFileKind.Image)
            {
                await _messageAppService.SendImageChatAsync(
                    conversationId,
                    new ImageChatMessage(
                        conversationId,
                        transferId,
                        ResolvePayloadSize(pickedFile.SizeBytes, stream),
                        pickedFile.ContentType,
                        false),
                    stream);
            }
            else
            {
                await _messageAppService.SendFileChatAsync(
                    conversationId,
                    new FileChatMessage(
                        conversationId,
                        transferId,
                        pickedFile.DisplayName,
                        ResolvePayloadSize(pickedFile.SizeBytes, stream)),
                    stream);
            }

            item.IsPending = false;
        }
        catch (Exception ex)
        {
            item.IsPending = false;
            item.IsFailed = true;
            item.Reason = ex.Message;
        }
    }

    private static long ResolvePayloadSize(long? declaredSize, Stream stream)
    {
        if (declaredSize.HasValue && declaredSize.Value > 0)
        {
            return declaredSize.Value;
        }

        return stream.CanSeek ? Math.Max(0L, stream.Length - stream.Position) : 0L;
    }
}
