using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Core.Services.Interfaces;
using Core.ViewModel.Windows;
using KitopiaAvalonia.Windows;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;

namespace KitopiaAvalonia.Services;

public class SearchWindowService : ISearchWindowService
{
    public void ShowOrHiddenSearchWindow()
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            var searchWindow = ServiceManager.Services.GetService<SearchWindow>();
            {
                var viewModel = ServiceManager.Services.GetService<SearchWindowViewModel>()!;
                viewModel.UpdateIndexOnWindowOpen();
                viewModel.LoadLast();

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
