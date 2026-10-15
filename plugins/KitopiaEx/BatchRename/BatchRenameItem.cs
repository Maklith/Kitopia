using System.ComponentModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;

namespace KitopiaEx.BatchRename;

public enum BatchRenameStatus
{
    [Description("lang.kitopiaex.compression.waiting")] Waiting,
    [Description("lang.kitopiaex.video_compression.running")] Running,
    [Description("lang.kitopiaex.compression.completed")] Completed,
    [Description("lang.kitopiaex.batch_rename.unchanged")] Unchanged,
    [Description("lang.kitopiaex.compression.failed")] Failed,
    [Description("lang.kitopiaex.compression.cancelled")] Cancelled
}

public sealed partial class BatchRenameItem(string sourcePath) : ObservableObject
{
    public string SourcePath { get; } = sourcePath;
    public string FileName => Path.GetFileName(SourcePath);
    [ObservableProperty] private string _newName = "";
    [ObservableProperty] private string _targetPath = "";
    [ObservableProperty] private string? _outputPath;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string _error = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCompleted))]
    [NotifyPropertyChangedFor(nameof(IsRunning))]
    private BatchRenameStatus _status;

    public bool HasError => Error.Length > 0;
    public bool IsCompleted => Status == BatchRenameStatus.Completed;
    public bool IsRunning => Status == BatchRenameStatus.Running;
}
