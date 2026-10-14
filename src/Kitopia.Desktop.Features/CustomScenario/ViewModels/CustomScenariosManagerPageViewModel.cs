using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kitopia.Feature.Localization;
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
                Header = Lang.Get("lang.kitopia.upload_scenario"), Text = Lang.Get("lang.kitopia.sign_in_to_your_kitopia_account"), AutoCloseDelay = null,
                Actions = [new ToastAction { Text = Lang.Get("lang.kitopia.sign_in"), IsPrimary = true, Callback = account.OpenBrowserLogin }]
            }, dialogWindow);
            return;
        }
        await ServiceManager.Services.GetRequiredService<IScenarioUploadService>().ShowAsync(scenario, dialogWindow);
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
            Title = Lang.Format("lang.kitopia.delete_value", scenario.Name),
            Content = Lang.Get("lang.kitopia.delete_this_scenario_this_cannot_be_undone"),
            PrimaryButtonText = Lang.Get("lang.kitopia.ok"),
            SecondaryButtonText = Lang.Get("lang.kitopia.cancel"),
            PrimaryAction = () => { Dispatcher.UIThread.InvokeAsync(() => { CustomScenarioManger.Remove(scenario); }); }
        };
        ((IToastService)ServiceManager.Services.GetService(typeof(IToastService))!).Show(
            dialog.ToToastRequest(), ServiceManager.Services.GetService<IWindowTool>()?.GetForegroundWindow());
    }
}
