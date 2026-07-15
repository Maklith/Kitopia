using System;
using Avalonia.Threading;
using Kitopia.Desktop.Features.Services.Interfaces;

namespace Kitopia.Desktop.Features.PluginHost.Services;

public sealed class PluginToolService : IPluginToolService
{
    public void RequestUninstallPlugin(string pluginId)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            try
            {
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        });
    }
}
