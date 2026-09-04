using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Kitopia.Desktop.Abstractions.FileSystem;

namespace Kitopia.Desktop.Features.ViewModel.Windows;

public enum LockTreeNodeType
{
    Directory,
    File,
    Process
}

public partial class FileLockTreeNodeViewModel : ObservableObject
{
    public LockTreeNodeType NodeType { get; init; }
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public int ProcessId { get; set; }
    public string PidText { get; set; } = string.Empty;
    public string ProcessName { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public bool IsLocked { get; set; }
    public bool IsDriverModule { get; set; }
    public FileLockInfo? SourceRecord { get; set; }

    [ObservableProperty]
    private bool _isSelfLocked;

    [ObservableProperty]
    private int _selfLockCount;

    [ObservableProperty]
    private int _childLockCount;

    [ObservableProperty]
    private bool _isExpanded = true;

    [ObservableProperty]
    private bool _isSelected;

    public ObservableCollection<FileLockTreeNodeViewModel> Children { get; } = [];

    // Helper properties for UI binding
    public bool IsDirectory => NodeType == LockTreeNodeType.Directory;
    public bool IsFile => NodeType == LockTreeNodeType.File;
    public bool IsProcess => NodeType == LockTreeNodeType.Process;

    public string NodeKey => NodeType switch
    {
        LockTreeNodeType.Directory => $"dir:{FilePath.TrimEnd('\\', '/')}",
        LockTreeNodeType.File => $"file:{FilePath}",
        LockTreeNodeType.Process => $"proc:{FilePath}:{ProcessId}:{ProcessName}",
        _ => FilePath
    };

    public bool HasChildren => Children.Count > 0;
    public bool HasChildLocks => ChildLockCount > 0;
    public int TotalLockCount => (IsSelfLocked || (IsFile && IsLocked) ? 1 : 0) + ChildLockCount;
    public bool HasAnyLock => TotalLockCount > 0;

    // Badges text
    public string SelfLockBadgeText => IsDirectory
        ? (IsSelfLocked ? (SelfLockCount > 1 ? $"自身被锁定 ({SelfLockCount}个进程)" : "自身被锁定") : "自身未锁定")
        : State;

    public string ChildLockBadgeText => HasChildLocks
        ? $"子项 {ChildLockCount} 处锁定"
        : "子项无锁定";

    public bool CanUnlock => (IsDirectory && HasAnyLock) || (IsFile && IsLocked) || IsProcess;
    public bool CanOpenLocation => !string.IsNullOrEmpty(FilePath);

    public void ExpandAll()
    {
        IsExpanded = true;
        foreach (var child in Children)
        {
            child.ExpandAll();
        }
    }

    public void CollapseAll()
    {
        IsExpanded = false;
        foreach (var child in Children)
        {
            child.CollapseAll();
        }
    }
}
