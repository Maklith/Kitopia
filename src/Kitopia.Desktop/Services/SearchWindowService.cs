using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Kitopia.Desktop.Features.Services.Interfaces;
using Kitopia.Desktop.Features.Search.ViewModels;
using Kitopia.Desktop.Features.Indexing;
using Kitopia.Desktop.Windows;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;

namespace Kitopia.Desktop.Services;

public class SearchWindowService : ISearchWindowService
{
    public void ShowOrHiddenSearchWindow()
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            var searchWindow = ServiceManager.Services.GetService<SearchWindow>();
            {
                ServiceManager.Services.GetRequiredService<IIndexMaintenanceService>().RefreshWindowOpenEntries();
                searchWindow!.Show();
            }
        });
    }
}
