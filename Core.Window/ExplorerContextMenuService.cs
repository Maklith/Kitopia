using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using PluginCore;
using Windows.Management.Deployment;
using Core.Services;
using Serilog;
using Serilog.Core;

namespace Core.Window;

public class ExplorerContextMenuService : IExplorerContextMenuService
{
    private ILogger Logger = LogManager.Logger.ForContext<IExplorerContextMenuService>();
    public async Task<bool> RegisterAsync()
    {
        return false;
    }

    public async Task<bool> UnregisterAsync()
    {
        return false;
    }
}
