using Core.Services;
using Core.Services.Interfaces;
using Serilog;
using Vanara.PInvoke;

namespace Core.Window.AppTools;

public class ShellUtils : IShellUtils
{
    private static readonly ILogger Logger = LogManager.Logger.ForContext<ShellUtils>();

    public void Open(string path, string? arguments = "", string? workingDirectory = "")
    {
        try 
        {
            Shell32.ShellExecute(IntPtr.Zero, "open", path, arguments, workingDirectory, ShowWindowCommand.SW_NORMAL);
        }
        catch (Exception e)
        {
            Logger.Error(e, $"Failed to open: {path} args: {arguments} dir: {workingDirectory}");
        }
    }

    public void RunAsAdmin(string path, string arguments = "")
    {
        try
        {
            Shell32.ShellExecute(IntPtr.Zero, "runas", path, arguments, "", ShowWindowCommand.SW_NORMAL);
        }
        catch (Exception e)
        {
            Logger.Error(e, $"Failed to run as admin: {path} args: {arguments}");
        }
    }

    public void OpenFolderAndSelect(string filepath)
    {
        var parentDir = Path.GetDirectoryName(filepath);
        if (string.IsNullOrEmpty(parentDir))
        {
             Shell32.ShellExecute(IntPtr.Zero, "open", "explorer.exe", "/select," + filepath, "", ShowWindowCommand.SW_SHOW);
             return;
        }
        
        try
        {
            using var pidlFolder = Shell32.ILCreateFromPath(parentDir);
            using var pidlItem = Shell32.ILCreateFromPath(filepath);
            
            if (pidlFolder.IsNull || pidlItem.IsNull)
            {
                throw new ArgumentException("Could not create PIDL for path.");
            }

            var itemsToSelect = new[] { pidlItem.DangerousGetHandle() };
            Shell32.SHOpenFolderAndSelectItems(pidlFolder, (uint)itemsToSelect.Length, itemsToSelect);
        }
        catch (Exception e)
        {
            Logger.Error(e, "Vanara SHOpenFolderAndSelectItems failed, falling back to ShellExecute.");
            Shell32.ShellExecute(IntPtr.Zero, "open", "explorer.exe", "/select," + filepath, "", ShowWindowCommand.SW_SHOW);
        }
    }
}
