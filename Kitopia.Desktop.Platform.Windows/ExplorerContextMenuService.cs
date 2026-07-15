using System.Diagnostics;
using Windows.Management.Deployment;
using Kitopia.Desktop.Features.Services;
using Kitopia.Desktop.Features.Utils;
using PluginCore;
using Serilog;

namespace Kitopia.Desktop.Platform.Windows;

public class ExplorerContextMenuService : IExplorerContextMenuService
{
    private readonly IToastService _toastService;
    private static readonly ILogger Logger = LogManager.Logger.ForContext<IExplorerContextMenuService>();

    public ExplorerContextMenuService(IToastService toastService)
    {
        _toastService = toastService;
    }

    public Task<bool> RegisterAsync()
    {
        var packageManager = new PackageManager();
        var packages = packageManager.FindPackagesForUser(string.Empty);
        if (packages.Any(x => x.Id.Name == "Maklith.KitopiaCompanion")) return Task.FromResult(true);
        Logger.Warning("Kitopia伴侣程序未安装，无法注册右键菜单");
        var dialog = new DialogContent
        {
            Title = "提示",
            Content = "未检测到Kitopia伴侣程序，请安装以使用右键菜单功能。",
            PrimaryButtonText = "前往安装",
            PrimaryAction = () =>
            {
                Process.Start(new ProcessStartInfo("ms-windows-store://pdp/?productid=9MV77XCQ37FP") { UseShellExecute = true });
            }
        };
        _toastService.Show(dialog.ToToastRequest());
        return Task.FromResult(false);
    }

    public Task<bool> UnregisterAsync()
    {
        return Task.FromResult(false);
    }
}
