using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Core.SDKs.Services;
using Core.ViewModel;
using KitopiaAvalonia.Windows;
using Microsoft.Extensions.DependencyInjection;

namespace KitopiaAvalonia.Services;

public class SearchWindowService : ISearchWindowService
{
    public void ShowOrHiddenSearchWindow()
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            var searchWindow = ServiceManager.Services.GetService<SearchWindow>();
            {
                ServiceManager.Services.GetService<SearchWindowViewModel>()!.CheckClipboard();
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