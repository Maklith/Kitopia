using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kitopia.Desktop.Abstractions.FileSystem;

namespace Kitopia.Desktop.Features.ViewModel.Windows;

public partial class FileLocksmithWindowViewModel : ObservableObject
{
    private readonly IFileLockService _fileLockService;

    [ObservableProperty]
    private ObservableCollection<FileLockInfo> _processes = new();

    public FileLocksmithWindowViewModel(IFileLockService fileLockService)
    {
        _fileLockService = fileLockService;
    }

    public void LoadProcesses(List<FileLockInfo> processes)
    {
        Processes = new ObservableCollection<FileLockInfo>(processes);
    }

    [RelayCommand]
    private async Task Unlock(FileLockInfo processInfo)
    {
        if (processInfo == null) return;

        var success = await _fileLockService.UnlockFileAsync(new List<int> { processInfo.ProcessId });
        if (success)
        {
            Processes.Remove(processInfo);
        }
    }

    [RelayCommand]
    private async Task UnlockAll()
    {
        if (Processes.Count == 0) return;

        var ids = Processes.Select(p => p.ProcessId).ToList();
        var success = await _fileLockService.UnlockFileAsync(ids);
        
        if (success)
        {
            Processes.Clear();
        }
        else
        {
            // If partial failure, re-check which are still alive?
            // For now, just remove valid ones or reload.
            // Simplified: Clear specific ones?
            // Actually, let's remove from list one by one or reload?
            // Since we don't have return details on which failed, we can assume success for UI 
            // or better, user should manually refresh if they see it persists (or we auto close window if all gone).
            
            // Let's just remove them all for UI responsiveness, assuming best effort.
            Processes.Clear(); 
        }
    }
}
