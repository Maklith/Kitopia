using Avalonia.Threading;
using Kitopia.Desktop.Features.Services.Interfaces;
using Kitopia.Desktop.Features.Indexing;
using Kitopia.Desktop.Windows;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
#if WINDOWS
using Kitopia.Desktop.Platform.Windows;
#endif

namespace Kitopia.Desktop.Services;

public class SearchWindowService : ISearchWindowService
{
    public void ShowExplorerSearch(HotKeyModel hotKey)
    {
#if WINDOWS
        Dispatcher.UIThread.Invoke(() =>
        {
            var context = ServiceManager.Services.GetRequiredService<ExplorerFileSelection>().GetSelection(directoryOnly: true);
            if (context.Paths.Count == 0 || context.Bounds is not { } bounds)
            {
                if (hotKey.SelectKey == EKey.任意键 && !string.IsNullOrEmpty(hotKey.TriggerText))
                    ServiceManager.Services.GetRequiredService<SharpHook.IEventSimulator>().SimulateTextEntry(hotKey.TriggerText);
                return;
            }
            ServiceManager.Services.GetRequiredService<SearchWindow>().ShowDirectorySearch(context.Paths[0], hotKey.TriggerText ?? string.Empty, bounds);
        });
#endif
    }

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
