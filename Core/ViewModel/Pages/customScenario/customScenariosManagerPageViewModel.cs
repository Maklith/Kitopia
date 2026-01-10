using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Core.CustomScenario;
using Core.Services;
using Core.Services.Interfaces;
using Core.Utils;
using PluginCore;

namespace Core.ViewModel.Pages.customScenario;

public partial class CustomScenariosManagerPageViewModel : ObservableRecipient
{
    public ObservableCollection<CustomScenario.CustomScenario> CustomScenarios => CustomScenarioManger.CustomScenarios;

    [RelayCommand]
    public void NewCustomScenarios()
    {
        ((ITaskEditorOpenService)ServiceManager.Services!.GetService(typeof(ITaskEditorOpenService))!).Open();
    }

    [RelayCommand]
    private void ToTaskEditPage(CustomScenario.CustomScenario scenario)
    {
        ((ITaskEditorOpenService)ServiceManager.Services!.GetService(typeof(ITaskEditorOpenService))!).Open(
            scenario);
    }

    [RelayCommand]
    private void StopCustomScenario(CustomScenario.CustomScenario scenario)
    {
        scenario.Stop();
    }

    [RelayCommand]
    private void RunCustomScenario(CustomScenario.CustomScenario scenario)
    {
        scenario.Run();
    }

    [RelayCommand]
    private void RemoveCustomScenario(CustomScenario.CustomScenario scenario)
    {
        var dialog = new DialogContent
        {
            Title = $"删除{scenario.Name}?",
            Content = "是否确定删除?\n他真的会丢失很久很久(不可恢复)",
            PrimaryButtonText = "确定",
            SecondaryButtonText = "取消",
            PrimaryAction = () => { Dispatcher.UIThread.InvokeAsync(() => { CustomScenarioManger.Remove(scenario); }); }
        };
        ((IContentDialog)ServiceManager.Services!.GetService(typeof(IContentDialog))!).ShowDialogAsync(null,
            dialog);
    }
}