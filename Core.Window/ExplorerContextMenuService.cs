using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using PluginCore;
using Windows.Management.Deployment;

namespace Core.Window;

public class ExplorerContextMenuService : IExplorerContextMenuService
{
    public async Task<bool> RegisterAsync()
    {
        try
        {
            var exePath = AppDomain.CurrentDomain.BaseDirectory;
            var manifestPath = Path.Combine(exePath, "ContextMenuDll", "AppxManifest.xml");

            if (!File.Exists(manifestPath))
            {
                manifestPath = Path.Combine(exePath, "AppxManifest.xml");
            }

            if (!File.Exists(manifestPath))
            {
                return false;
            }

            var packageManager = new PackageManager();
            var options = new RegisterPackageOptions
            {
                ExternalLocationUri = new Uri(exePath),
                AllowUnsigned = true,
                DeveloperMode = true
            };

            var result = await packageManager.RegisterPackageByUriAsync(new Uri(manifestPath), options);
            return result.ExtendedErrorCode == null;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public async Task<bool> UnregisterAsync()
    {
        try
        {
            var packageManager = new PackageManager();
            var packages = packageManager.FindPackagesForUser(string.Empty);
            var package = packages.FirstOrDefault(p => p.Id.Name == "Kitopia" && p.Id.Publisher == "CN=Kitopia");

            if (package != null)
            {
                var result = await packageManager.RemovePackageAsync(package.Id.FullName);
                return result.ExtendedErrorCode == null;
            }

            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
