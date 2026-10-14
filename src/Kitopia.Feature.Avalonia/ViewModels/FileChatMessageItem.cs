using System;
using System.IO;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Kitopia.Feature.DeviceCommunication.Application;
using Kitopia.Feature.Localization;

namespace Kitopia.Feature.Avalonia.DeviceCommunication.ViewModels;

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
    [ObservableProperty] private FileTransferStatus _status = FileTransferStatus.WaitingForAccept;
    [ObservableProperty] private double _receiveProgress;
    [ObservableProperty] private double _transferSpeedBytesPerSecond;

    public System.Windows.Input.ICommand? AcceptCommand { get; set; }
    public System.Windows.Input.ICommand? RejectCommand { get; set; }
    public System.Windows.Input.ICommand? OpenCommand { get; set; }
    public System.Windows.Input.ICommand? SaveAsCommand { get; set; }
    public System.Windows.Input.ICommand? CopyFileCommand { get; set; }
    public System.Windows.Input.ICommand? CancelCommand { get; set; }
    public System.Windows.Input.ICommand? ViewDetailsCommand { get; set; }

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

    public bool IsIncomingFileOffer => !IsOutgoing;
    public bool IsPending => Status is FileTransferStatus.WaitingForAccept or FileTransferStatus.Delivered;
    public bool IsReceiving => Status is FileTransferStatus.Accepted or FileTransferStatus.InProgress;
    public bool IsTransferActive => IsPending || IsReceiving;
    public bool CanHandleIncomingOffer => !IsOutgoing && Status == FileTransferStatus.WaitingForAccept && TrackingTransferId.HasValue;
    public bool HasLocalFile => !string.IsNullOrWhiteSpace(LocalFilePath) && File.Exists(LocalFilePath);
    public bool CanUseLocalFile => Status == FileTransferStatus.Completed && HasLocalFile;
    public bool HasFileIcon => FileIcon is not null;
    public string FileTypeText
    {
        get
        {
            var extension = Path.GetExtension(FileName);
            return string.IsNullOrEmpty(extension) ? Lang.Get("lang.kitopia.file") : extension.TrimStart('.').ToUpperInvariant();
        }
    }
    public string TimeText => Timestamp.ToLocalTime().ToString("HH:mm");
    public string ProgressPercentText => $"{ReceiveProgress * 100:0}";

    public string StateText
    {
        get
        {
            if (IsReceiving)
            {
                var speed = BuildSpeedText();
                var pct = ProgressPercentText;
                return Lang.Format(IsOutgoing ? "lang.kitopia.sending_value_value" : "lang.kitopia.receiving_value_value", speed, pct);
            }
            return Status switch
            {
                FileTransferStatus.WaitingForAccept => Lang.Get(IsOutgoing ? "lang.kitopia.sending_request" : "lang.kitopia.waiting_to_receive"),
                FileTransferStatus.Delivered => Lang.Get("lang.kitopia.request_delivered_waiting_for_acceptance"),
                FileTransferStatus.Completed => Lang.Get(IsOutgoing ? "lang.kitopia.completed" : "lang.kitopia.saved"),
                FileTransferStatus.Rejected => Lang.Get("lang.kitopia.rejected"),
                FileTransferStatus.Cancelled => Lang.Get("lang.kitopia.cancelled"),
                FileTransferStatus.Timeout => Lang.Get("lang.kitopia.transfer_timed_out"),
                FileTransferStatus.Failed => Lang.Get("lang.kitopia.failed"),
                _ => string.Empty
            };
        }
    }

    public bool HasState => !string.IsNullOrWhiteSpace(StateText);

    public string FileSizeText => FormatFileSizeLabel(FileSizeBytes);

    internal void RefreshLanguage()
    {
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(HasState));
        OnPropertyChanged(nameof(FileSizeText));
        OnPropertyChanged(nameof(FileTypeText));
    }

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
        return Lang.Format("lang.kitopia.value_bytes", bytes);
    }

    partial void OnIsOutgoingChanged(bool value)
    {
        OnPropertyChanged(nameof(IsIncomingFileOffer));
        OnPropertyChanged(nameof(CanHandleIncomingOffer));
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(HasState));
    }
    partial void OnTimestampChanged(DateTimeOffset value) => OnPropertyChanged(nameof(TimeText));
    partial void OnStatusChanged(FileTransferStatus value)
    {
        if (!IsReceiving) ResetTransferSpeed();
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(HasState));
        OnPropertyChanged(nameof(IsPending));
        OnPropertyChanged(nameof(IsReceiving));
        OnPropertyChanged(nameof(IsTransferActive));
        OnPropertyChanged(nameof(CanHandleIncomingOffer));
        OnPropertyChanged(nameof(CanUseLocalFile));
        OnPropertyChanged(nameof(HasLocalFile));
    }
    partial void OnTrackingTransferIdChanged(Guid? value) => OnPropertyChanged(nameof(CanHandleIncomingOffer));
    partial void OnReceiveProgressChanged(double value)
    {
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(HasState));
        OnPropertyChanged(nameof(ProgressPercentText));
    }
    partial void OnTransferSpeedBytesPerSecondChanged(double value)
    {
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(HasState));
    }
    partial void OnFileSizeBytesChanged(long value) => OnPropertyChanged(nameof(FileSizeText));
    partial void OnFileNameChanged(string value) => OnPropertyChanged(nameof(FileTypeText));
    partial void OnFileIconChanged(Bitmap? value) => OnPropertyChanged(nameof(HasFileIcon));
    partial void OnLocalFilePathChanged(string? value)
    {
        OnPropertyChanged(nameof(HasLocalFile));
        OnPropertyChanged(nameof(CanUseLocalFile));
    }
}
