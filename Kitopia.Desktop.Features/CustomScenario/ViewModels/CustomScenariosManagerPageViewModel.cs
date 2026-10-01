using System.Collections.ObjectModel;
using System.Net;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kitopia.Desktop.Abstractions.Shell;
using Kitopia.Desktop.Features.Utils;
using Kitopia.Desktop.Features.CustomScenario.Services;
using Kitopia.Desktop.Features.Services.Interfaces;
using Kitopia.Desktop.Features.Services.Config;
using Kitopia.Desktop.Features.Services;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using Scenario = Kitopia.Desktop.Features.CustomScenario.CustomScenario;

namespace Kitopia.Desktop.Features.CustomScenario.ViewModels;

public partial class CustomScenariosManagerPageViewModel : ObservableRecipient
{
    public ObservableCollection<Scenario> CustomScenarios => CustomScenarioManger.CustomScenarios;

    [RelayCommand]
    public void NewCustomScenarios()
    {
        ((ITaskEditorOpenService)ServiceManager.Services.GetService(typeof(ITaskEditorOpenService))!).Open();
    }

    [RelayCommand]
    private void ToTaskEditPage(Scenario scenario)
    {
        ((ITaskEditorOpenService)ServiceManager.Services.GetService(typeof(ITaskEditorOpenService))!).Open(
            scenario);
    }

    [RelayCommand]
    private void StopCustomScenario(Scenario scenario)
    {
        scenario.Stop();
    }

    [RelayCommand]
    private void RunCustomScenario(Scenario scenario)
    {
        scenario.Run();
    }

    [RelayCommand]
    private void RetryScenario(Scenario scenario)
    {
        CustomScenarioManger.Reload(scenario);
    }

    [RelayCommand]
    private void OpenScenarioFile(Scenario scenario)
    {
        ServiceManager.Services.GetRequiredService<IDesktopShell>()
            .Open(KitopiaPaths.GetCustomScenarioFilePath(scenario.Uuid));
    }

    [RelayCommand]
    private async Task UploadScenario(Scenario scenario)
    {
        var account = ServiceManager.Services.GetRequiredService<IAccountService>();
        var toast = ServiceManager.Services.GetRequiredService<IToastService>();
        var dialogWindow = ServiceManager.Services.GetService<IWindowTool>()?.GetForegroundWindow();
        if (!account.IsLoggedIn)
        {
            await toast.Show(new ToastRequest
            {
                Header = "上传情景", Text = "请登录 Kitopia 账户。", AutoCloseDelay = null,
                Actions = [new ToastAction { Text = "登录", IsPrimary = true, Callback = account.OpenBrowserLogin }]
            }, dialogWindow);
            return;
        }
        await toast.Show(new ToastRequest
        {
            Header = $"上传情景 · {scenario.Name}", Text = scenario.Description,
            AutoCloseDelay = null, ShowCloseButton = true,
            SelectionOptions = ["私有", "公开"], SelectedOption = "私有",
            SelectionConfirmText = "上传",
            SelectionConfirmed = visibility => _ = UploadSelectedAsync(scenario, visibility == "公开", dialogWindow)
        }, dialogWindow);
    }

    private static async Task UploadSelectedAsync(Scenario scenario, bool isPublic, Window? dialogWindow)
    {
        var toast = ServiceManager.Services.GetRequiredService<IToastService>();
        try
        {
            await ScenarioMarketService.UploadAsync(scenario, isPublic);
            await toast.Show("情景上传成功", isPublic ? $"{scenario.Name} 已提交审核。" : $"{scenario.Name} 已保存为私有情景。");
        }
        catch (HttpRequestException exception) when (exception.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            LogManager.Logger.Warning("上传情景需要重新授权: {Scenario}, HTTP {StatusCode}", scenario.Name, (int)exception.StatusCode.Value);
            var account = ServiceManager.Services.GetRequiredService<IAccountService>();
            await toast.Show(new ToastRequest
            {
                Header = "情景上传需要授权", Text = exception.Message,
                NotificationType = NotificationType.Warning, AutoCloseDelay = null,
                Actions = [new ToastAction
                {
                    Text = exception.StatusCode == HttpStatusCode.Unauthorized ? "重新登录" : "重新授权",
                    IsPrimary = true, Callback = account.OpenBrowserLogin
                }]
            }, dialogWindow);
        }
        catch (Exception exception)
        {
            LogManager.Logger.Error(exception, "上传情景失败: {Scenario}", scenario.Name);
            await toast.Show("情景上传失败", exception.Message);
        }
    }

    [RelayCommand]
    private void OpenMyScenarios()
    {
        ServiceManager.Services.GetRequiredService<IDesktopShell>().Open($"{ConfigManger.WebUrl}/developer/scenarios");
    }

    [RelayCommand]
    private void RemoveCustomScenario(Scenario scenario)
    {
        var dialog = new DialogContent
        {
            Title = $"删除{scenario.Name}?",
            Content = "是否确定删除?\n他真的会丢失很久很久(不可恢复)",
            PrimaryButtonText = "确定",
            SecondaryButtonText = "取消",
            PrimaryAction = () => { Dispatcher.UIThread.InvokeAsync(() => { CustomScenarioManger.Remove(scenario); }); }
        };
        ((IToastService)ServiceManager.Services.GetService(typeof(IToastService))!).Show(
            dialog.ToToastRequest(), ServiceManager.Services.GetService<IWindowTool>()?.GetForegroundWindow());
    }
}
