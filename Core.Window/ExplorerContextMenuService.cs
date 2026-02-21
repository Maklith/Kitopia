using PluginCore;
using Windows.Management.Deployment;
using Core.Services;
using Core.Services.Interfaces;
using Core.Utils;
using Serilog;

namespace Core.Window;

public class ExplorerContextMenuService : IExplorerContextMenuService
{
    private readonly IContentDialog _contentDialog;
    private static readonly ILogger Logger = LogManager.Logger.ForContext<IExplorerContextMenuService>();

    public ExplorerContextMenuService(IContentDialog contentDialog)
    {
        _contentDialog = contentDialog;
    }

    public async Task<bool> RegisterAsync()
    {
        var packageManager = new PackageManager();
        var packages = packageManager.FindPackagesForUser(string.Empty);
        if (packages.Any(x => x.Id.Name == "Maklith.KitopiaCompanion")) return true;
        Logger.Warning("Kitopia伴侣程序未安装，无法注册右键菜单");
        var dialog = new DialogContent
        {
            Title = "提示",
            Content = "未检测到Kitopia伴侣程序，请安装以使用右键菜单功能。",
            PrimaryButtonText = "前往安装",
            PrimaryAction = () =>
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-windows-store://pdp/?productid=9MV77XCQ37FP") { UseShellExecute = true });
            }
        };
        await _contentDialog.ShowDialogAsync(null, dialog);
        return false;
    }

    public async Task<bool> UnregisterAsync()
    {
        return false;
    }
}
