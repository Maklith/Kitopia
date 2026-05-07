#region

using Avalonia.Controls.Notifications;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Core.Services.Interfaces;
using Core.ViewModel.Main;
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
        WeakReferenceMessenger.Default.Send<PageChangeEventArgs>(new PageChangeEventArgs("Setting"));
    }

    [RelayCommand]
    private void ShowBasicToast()
    {
        GetToastService()?.Show("Toast测试", "这是一个基础信息通知。", NotificationType.Information);
    }

    [RelayCommand]
    private void ShowActionToast()
    {
        var toastService = GetToastService();
        if (toastService is null)
        {
            return;
        }

        toastService.Show(new ToastRequest
        {
            Header = "按钮通知测试",
            Text = "该通知包含自定义按钮和不同触发逻辑。",
            NotificationType = NotificationType.Success,
            AutoCloseDelay = null,
            Actions =
            [
                new ToastAction
                {
                    Text = "打开设置",
                    IsPrimary = true,
                    Callback = () => WeakReferenceMessenger.Default.Send<PageChangeEventArgs>(new PageChangeEventArgs("Setting"))
                },
                new ToastAction
                {
                    Text = "再来一条",
                    Callback = () => toastService.Show("按钮回调", "你点击了“再来一条”。")
                }
            ]
        });
    }

    [RelayCommand]
    private void ShowLongTimeoutToast()
    {
        GetToastService()?.Show(new ToastRequest
        {
            Header = "时长测试",
            Text = "该通知会在 8 秒后自动消失。",
            NotificationType = NotificationType.Warning,
            AutoCloseDelay = TimeSpan.FromSeconds(8)
        });
    }

    [RelayCommand]
    private async Task ShowProgressToast()
    {
        var toastService = GetToastService();
        if (toastService is null)
        {
            return;
        }

        var progressHandle = toastService.ShowProgress("进度通知测试", "开始处理...", NotificationType.Information,
            initialProgress: 0, isIndeterminate: false);
        for (var i = 0; i <= 100; i += 20)
        {
            await Task.Delay(300);
            progressHandle.Update(i, $"当前进度：{i}%");
        }

        progressHandle.Complete("处理完成。");
    }

    private static IToastService? GetToastService()
    {
        return ServiceManager.Services?.GetService(typeof(IToastService)) as IToastService;
    }
    
}
