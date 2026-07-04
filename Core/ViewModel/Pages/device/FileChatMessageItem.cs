using System;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Core.ViewModel.Pages.device;

public partial class FileChatMessageItem : ObservableObject
{
    public FileChatMessageItem(
        string fileName,
        long fileSizeBytes,
        bool isOutgoing,
        DateTimeOffset timestamp,
        string? localFilePath = null)
    {
        _fileName = fileName;
        _fileSizeBytes = fileSizeBytes;
        _isOutgoing = isOutgoing;
        _isPending = true;
        _timestamp = timestamp;
        _localFilePath = localFilePath;
    }

    [ObservableProperty] private string _fileName = string.Empty;
    [ObservableProperty] private long _fileSizeBytes;
    [ObservableProperty] private string? _localFilePath;
    [ObservableProperty] private Bitmap? _fileIcon;
    [ObservableProperty] private bool _isOutgoing;
    [ObservableProperty] private DateTimeOffset _timestamp;
    [ObservableProperty] private string _conversationId = string.Empty;
    [ObservableProperty] private Guid? _trackingTransferId;
    [ObservableProperty] private bool _isIncomingFileOffer;
    [ObservableProperty] private bool _isHandled;
    [ObservableProperty] private bool _isPending;
    [ObservableProperty] private bool _isFailed;
    [ObservableProperty] private bool _isReceiving;
    [ObservableProperty] private bool _isWaitingForAccept;
    [ObservableProperty] private bool _isOfferDelivered;
    [ObservableProperty] private double _receiveProgress;
    [ObservableProperty] private double _transferSpeedBytesPerSecond;

    private long _transferStartBytes = -1;
    private DateTimeOffset? _transferStartTimestampUtc;
    private DateTimeOffset _lastProgressUpdate = DateTimeOffset.MinValue;

    private const int ProgressUpdateIntervalMs = 200;

    public bool CanUpdateProgress(DateTimeOffset timestampUtc)
    {
        if ((timestampUtc - _lastProgressUpdate).TotalMilliseconds < ProgressUpdateIntervalMs)
            return false;
        _lastProgressUpdate = timestampUtc;
        return true;
    }

    public bool CanHandleIncomingOffer => IsIncomingFileOffer && !IsHandled && TrackingTransferId.HasValue;
    public bool IsTransferActive => (IsPending || IsReceiving || IsWaitingForAccept) && !IsFailed && !IsHandled;
    public bool HasLocalFile => !string.IsNullOrWhiteSpace(LocalFilePath) && System.IO.File.Exists(LocalFilePath);
    public string TimeText => Timestamp.ToLocalTime().ToString("HH:mm");
    public string ProgressPercentText => $"{ReceiveProgress * 100:0}";

    public string StateText
    {
        get
        {
            if (IsFailed && !IsReceiving) return "失败";
            if (IsIncomingFileOffer && !IsHandled) return "等待接收";
            if (IsReceiving)
            {
                var speed = BuildSpeedText();
                var pct = ProgressPercentText;
                var direction = IsOutgoing ? "发送" : "接收";
                return $"{direction}中 {speed} ({pct}%)";
            }
            if (IsOutgoing && IsPending && !IsOfferDelivered) return "正在发送请求...";
            if (IsOutgoing && IsPending && IsWaitingForAccept) return "请求已送达，等待对方接受...";
            if (IsHandled && IsIncomingFileOffer) return "已保存";
            if (!IsPending && !IsFailed && !IsReceiving) return "已完成";
            return string.Empty;
        }
    }

    public bool HasState => !string.IsNullOrWhiteSpace(StateText);

    public string FileSizeText => FormatFileSizeLabel(FileSizeBytes);

    public void UpdateTransferSpeed(long transferredBytes, DateTimeOffset timestampUtc)
    {
        if (!_transferStartTimestampUtc.HasValue || transferredBytes < _transferStartBytes)
        {
            _transferStartBytes = Math.Max(0L, transferredBytes);
            _transferStartTimestampUtc = timestampUtc;
            TransferSpeedBytesPerSecond = 0d;
            return;
        }

        var elapsedSeconds = (timestampUtc - _transferStartTimestampUtc.Value).TotalSeconds;
        if (elapsedSeconds <= 0.0001d) return;

        var elapsedBytes = Math.Max(0L, transferredBytes - _transferStartBytes);
        TransferSpeedBytesPerSecond = Math.Max(0d, elapsedBytes / elapsedSeconds);
        _transferStartBytes = transferredBytes;
        _transferStartTimestampUtc = timestampUtc;
    }

    public void ResetTransferSpeed()
    {
        _transferStartBytes = -1;
        _transferStartTimestampUtc = null;
        TransferSpeedBytesPerSecond = 0d;
    }

    private string BuildSpeedText()
    {
        var speed = TransferSpeedBytesPerSecond;
        if (speed <= 0) return string.Empty;
        if (speed >= 1024 * 1024) return $"{speed / (1024 * 1024):0.0} MB/s";
        if (speed >= 1024) return $"{speed / 1024:0.0} KB/s";
        return $"{speed:0} B/s";
    }

    public static string FormatFileSizeLabel(long sizeBytes)
    {
        var bytes = Math.Max(0L, sizeBytes);
        const long oneKb = 1024;
        const long oneMb = 1024L * 1024L;
        const long oneGb = 1024L * 1024L * 1024L;

        if (bytes >= oneGb) return $"{bytes / (double)oneGb:0.00} GB";
        if (bytes >= oneMb) return $"{bytes / (double)oneMb:0.00} MB";
        if (bytes >= oneKb) return $"{bytes / (double)oneKb:0.00} KB";
        return $"{bytes} 字节";
    }

    partial void OnIsOutgoingChanged(bool value) => OnPropertyChanged(nameof(StateText));
    partial void OnTimestampChanged(DateTimeOffset value) => OnPropertyChanged(nameof(TimeText));
    partial void OnIsPendingChanged(bool value)
    {
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(HasState));
        OnPropertyChanged(nameof(IsTransferActive));
    }
    partial void OnIsFailedChanged(bool value)
    {
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(HasState));
        OnPropertyChanged(nameof(IsTransferActive));
    }
    partial void OnIsIncomingFileOfferChanged(bool value)
    {
        OnPropertyChanged(nameof(CanHandleIncomingOffer));
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(HasState));
    }
    partial void OnIsHandledChanged(bool value)
    {
        OnPropertyChanged(nameof(CanHandleIncomingOffer));
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(HasState));
        OnPropertyChanged(nameof(IsTransferActive));
    }
    partial void OnTrackingTransferIdChanged(Guid? value) => OnPropertyChanged(nameof(CanHandleIncomingOffer));
    partial void OnReceiveProgressChanged(double value)
    {
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(HasState));
        OnPropertyChanged(nameof(ProgressPercentText));
    }
    partial void OnIsReceivingChanged(bool value)
    {
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(HasState));
        OnPropertyChanged(nameof(IsTransferActive));
    }
    partial void OnTransferSpeedBytesPerSecondChanged(double value)
    {
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(HasState));
    }
    partial void OnIsWaitingForAcceptChanged(bool value)
    {
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(HasState));
        OnPropertyChanged(nameof(IsTransferActive));
    }
    partial void OnIsOfferDeliveredChanged(bool value)
    {
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(HasState));
    }
    partial void OnFileSizeBytesChanged(long value) => OnPropertyChanged(nameof(FileSizeText));
    partial void OnLocalFilePathChanged(string? value) => OnPropertyChanged(nameof(HasLocalFile));
}
