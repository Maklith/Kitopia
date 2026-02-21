#region

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Core.Services.Interfaces;
using PluginCore;

#endregion

namespace Core.ViewModel.Pages;

public partial class HomePageViewModel : ObservableRecipient
{
    [ObservableProperty] private List<FileType> _fileTypes = new()
    {
        FileType.文件, FileType.剪贴板图像, FileType.PDF文档, FileType.便签, FileType.命令
    };

    [RelayCommand]
    public void Click()
    {
        ((INavigationPageService)ServiceManager.Services!.GetService(typeof(INavigationPageService)))
            .Navigate("设置");
    }
}