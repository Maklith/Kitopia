using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kitopia.Desktop.Features.CustomScenario;
using Kitopia.Desktop.Features.Utils;
using Kitopia.Desktop.Features.CustomScenario.Services;
using PluginCore;
using Scenario = Kitopia.Desktop.Features.CustomScenario.CustomScenario;

namespace Kitopia.Desktop.Features.CustomScenario.ViewModels;

public partial class CustomScenariosManagerPageViewModel : ObservableRecipient
{
    public ObservableCollection<Scenario> CustomScenarios => CustomScenarioManger.CustomScenarios;

    [RelayCommand]
    public void NewCustomScenarios()
    {
        ((ITaskEditorOpenService)ServiceManager.Services!.GetService(typeof(ITaskEditorOpenService))!).Open();
    }

    [RelayCommand]
    private void ToTaskEditPage(Scenario scenario)
    {
        ((ITaskEditorOpenService)ServiceManager.Services!.GetService(typeof(ITaskEditorOpenService))!).Open(
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
        ((IToastService)ServiceManager.Services!.GetService(typeof(IToastService))!).Show(
            dialog.ToToastRequest());
    }
}
