using System.Runtime.InteropServices;
using Kitopia.Desktop.Features.Services;
using Kitopia.Desktop.Abstractions.Shell;
using Serilog;
using Vanara.PInvoke;

namespace Kitopia.Desktop.Platform.Windows.AppTools;

public sealed class ShellUtils : IDesktopShell
{
    private static readonly ILogger Logger = LogManager.Logger.ForContext<ShellUtils>();

    public void Open(string path, string? arguments = "", string? workingDirectory = "")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        try 
        {
            var info = new Shell32.SHELLEXECUTEINFO(path, arguments)
            {
                lpVerb =  null,
                lpDirectory = workingDirectory,
                nShellExecuteShow = ShowWindowCommand.SW_NORMAL,
                fMask = Shell32.ShellExecuteMaskFlags.SEE_MASK_NOASYNC
            };
            if (!Shell32.ShellExecuteEx(ref info))
                Logger.Error("Failed to open {Path}: Win32 error {ErrorCode}", path, Marshal.GetLastWin32Error());
        }
        catch (Exception e)
        {
            Logger.Error(e, $"Failed to open: {path} args: {arguments} dir: {workingDirectory}");
        }
    }

    public void RunAsAdmin(string path, string arguments = "")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        try
        {
            var info = new Shell32.SHELLEXECUTEINFO(path, arguments)
            {
                lpVerb = "runas",
                lpDirectory = "",
                nShellExecuteShow = ShowWindowCommand.SW_NORMAL,
                fMask = Shell32.ShellExecuteMaskFlags.SEE_MASK_NOASYNC
            };
            if (!Shell32.ShellExecuteEx(ref info))
                Logger.Error("Failed to run {Path} as admin: Win32 error {ErrorCode}", path, Marshal.GetLastWin32Error());
        }
        catch (Exception e)
        {
            Logger.Error(e, $"Failed to run as admin: {path} args: {arguments}");
        }
    }

    public void OpenFolderAndSelect(string filepath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filepath);

        var parentDir = Path.GetDirectoryName(filepath);
        if (string.IsNullOrEmpty(parentDir))
        {
             var info = new Shell32.SHELLEXECUTEINFO("explorer.exe", "/select," + filepath)
             {
                 lpVerb = "open",
                 lpDirectory = "",
                 nShellExecuteShow = ShowWindowCommand.SW_SHOW,
                 fMask = Shell32.ShellExecuteMaskFlags.SEE_MASK_NOASYNC
             };
             if (!Shell32.ShellExecuteEx(ref info))
                 Logger.Error("Failed to select {Path} in Explorer: Win32 error {ErrorCode}", filepath, Marshal.GetLastWin32Error());
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
            Logger.Error(e, "Vanara SHOpenFolderAndSelectItems failed, falling back to ShellExecuteEx.");
            var info = new Shell32.SHELLEXECUTEINFO("explorer.exe", "/select," + filepath)
            {
                lpVerb = "open",
                lpDirectory = "",
                nShellExecuteShow = ShowWindowCommand.SW_SHOW,
                fMask = Shell32.ShellExecuteMaskFlags.SEE_MASK_NOASYNC
            };
            if (!Shell32.ShellExecuteEx(ref info))
                Logger.Error("Failed to select {Path} in Explorer: Win32 error {ErrorCode}", filepath, Marshal.GetLastWin32Error());
        }
    }
}
