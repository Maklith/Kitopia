using CommunityToolkit.Mvvm.ComponentModel;
using Kitopia.DeviceCommunication.Application;

namespace Kitopia.Mobile.ViewModels;

public sealed partial class MobileMessageItemViewModel : ObservableObject
{
    public MobileMessageItemViewModel(string conversationId, bool isOutgoing, DateTimeOffset timestamp)
    {
        ConversationId = conversationId;
        IsOutgoing = isOutgoing;
        Timestamp = timestamp;
    }

    [ObservableProperty]
    private string _conversationId;

    [ObservableProperty]
    private string _text = string.Empty;

    [ObservableProperty]
    private bool _isOutgoing;

    [ObservableProperty]
    private DateTimeOffset _timestamp;

    [ObservableProperty]
    private bool _isPending;

    [ObservableProperty]
    private bool _isFailed;

    [ObservableProperty]
    private string _fileName = string.Empty;

    [ObservableProperty]
    private Guid? _transferId;

    [ObservableProperty]
    private bool _isIncomingFileOffer;

    [ObservableProperty]
    private bool _isHandled;

    [ObservableProperty]
    private bool _isReceiving;

    [ObservableProperty]
    private long? _bytesTransferred;

    [ObservableProperty]
    private long? _totalBytes;

    [ObservableProperty]
    private string _reason = string.Empty;

    public bool CanHandleIncomingOffer => IsIncomingFileOffer && !IsHandled && TransferId.HasValue;
    public bool CanCancelTransfer => TransferId.HasValue && !IsHandled && (IsPending || IsReceiving);

    public double ProgressPercent => !BytesTransferred.HasValue || !TotalBytes.HasValue || TotalBytes.Value <= 0
        ? 0
        : Math.Clamp((double)BytesTransferred.Value / TotalBytes.Value * 100d, 0d, 100d);

    public string TimestampText => Timestamp.ToLocalTime().ToString("HH:mm");

    public string StatusText
    {
        get
        {
            if (IsFailed)
            {
                return string.IsNullOrWhiteSpace(Reason) ? "Failed" : $"Failed: {Reason}";
            }

            if (IsReceiving)
            {
                return TotalBytes.HasValue && TotalBytes.Value > 0
                    ? $"Receiving {ProgressPercent:0}%"
                    : "Receiving";
            }

            if (IsPending)
            {
                return "Sending...";
            }

            if (IsHandled && IsIncomingFileOffer)
            {
                return "Handled";
            }

            return string.Empty;
        }
    }

    partial void OnIsIncomingFileOfferChanged(bool value) => OnPropertyChanged(nameof(CanHandleIncomingOffer));

    partial void OnTransferIdChanged(Guid? value)
    {
        _ = value;
        OnPropertyChanged(nameof(CanHandleIncomingOffer));
        OnPropertyChanged(nameof(CanCancelTransfer));
    }

    partial void OnIsHandledChanged(bool value)
    {
        _ = value;
        OnPropertyChanged(nameof(CanHandleIncomingOffer));
        OnPropertyChanged(nameof(CanCancelTransfer));
        OnPropertyChanged(nameof(StatusText));
    }

    partial void OnBytesTransferredChanged(long? value)
    {
        _ = value;
        OnPropertyChanged(nameof(ProgressPercent));
        OnPropertyChanged(nameof(StatusText));
    }

    partial void OnTotalBytesChanged(long? value)
    {
        _ = value;
        OnPropertyChanged(nameof(ProgressPercent));
        OnPropertyChanged(nameof(StatusText));
    }

    partial void OnIsReceivingChanged(bool value)
    {
        _ = value;
        OnPropertyChanged(nameof(CanCancelTransfer));
        OnPropertyChanged(nameof(StatusText));
    }

    partial void OnIsPendingChanged(bool value)
    {
        _ = value;
        OnPropertyChanged(nameof(CanCancelTransfer));
        OnPropertyChanged(nameof(StatusText));
    }

    partial void OnIsFailedChanged(bool value)
    {
        _ = value;
        OnPropertyChanged(nameof(StatusText));
    }

    partial void OnReasonChanged(string value)
    {
        _ = value;
        OnPropertyChanged(nameof(StatusText));
    }

    partial void OnTimestampChanged(DateTimeOffset value)
    {
        _ = value;
        OnPropertyChanged(nameof(TimestampText));
    }

    public void ApplyTransferUpdate(FileTransferUpdatedEvent transferEvent)
    {
        FileName = string.IsNullOrWhiteSpace(transferEvent.FileName) ? FileName : transferEvent.FileName;
        BytesTransferred = transferEvent.BytesTransferred;
        TotalBytes = transferEvent.TotalBytes;
        Reason = transferEvent.Reason ?? string.Empty;

        switch (transferEvent.Status)
        {
            case FileTransferStatus.WaitingForAccept:
                IsIncomingFileOffer = true;
                IsHandled = false;
                IsReceiving = false;
                IsPending = false;
                IsFailed = false;
                break;
            case FileTransferStatus.Accepted:
            case FileTransferStatus.InProgress:
                IsReceiving = true;
                IsPending = false;
                IsFailed = false;
                break;
            case FileTransferStatus.Completed:
                IsReceiving = false;
                IsPending = false;
                IsFailed = false;
                IsHandled = true;
                break;
            case FileTransferStatus.Rejected:
            case FileTransferStatus.Cancelled:
            case FileTransferStatus.Failed:
            case FileTransferStatus.Timeout:
                IsReceiving = false;
                IsPending = false;
                IsFailed = true;
                IsHandled = true;
                break;
        }
    }
}
