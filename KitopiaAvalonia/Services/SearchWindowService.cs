using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Core.Services;
using Core.ViewModel.Windows;
using KitopiaAvalonia.Windows;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using PluginCore.SearchWindow.InputDataAnalyzer;

namespace KitopiaAvalonia.Services;

public class SearchWindowService : ISearchWindowService
{
    public void ShowOrHiddenSearchWindow()
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            var searchWindow = ServiceManager.Services.GetService<SearchWindow>();
            {
                ServiceManager.Services.GetService<SearchWindowViewModel>()!.LoadLast();
                ServiceManager.Services.GetService<SearchWindowViewModel>()!.ProcessInputData(null,
                    IInputDataAnalyzeTimeFlags.仅有搜索内容打开时);

                searchWindow.Show();
                Task.Run(() =>
                {
                    Thread.CurrentThread.Priority = ThreadPriority.Lowest;
                    ServiceManager.Services.GetService<SearchWindowViewModel>()!.ReloadApps();
                });
            }
        });
    }
}