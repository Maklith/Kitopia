using System;
using Avalonia.Threading;
using Core.Services.Interfaces;

namespace KitopiaAvalonia.Services;

public class PluginToolService : IPluginToolService
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