using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Serilog;
using Vanara.PInvoke;
using Kitopia.Desktop.Abstractions.Shell;
using Kitopia.Desktop.Features.Services;
using static Vanara.PInvoke.Shell32;

namespace Kitopia.Desktop.Platform.Windows.AppTools;

public sealed class ShellUtils : IDesktopShell
{
    private static readonly ILogger Logger = LogManager.Logger.ForContext<ShellUtils>();

    // Both menus and keyboard verbs use the file's Shell handler, including registered extensions.
    public static void InvokeFileContextMenu(Window owner, string path, PixelPoint? position = null, string? verb = null)
    {
        IShellItem? item = null;
        IContextMenu? context = null;
        try
        {
            var hwnd = owner.TryGetPlatformHandle()!.Handle;
            item = SHCreateItemFromParsingName<IShellItem>(path);
            context = item.BindToHandler<IContextMenu>(null, BHID.BHID_SFUIObject);
            using var menu = User32.CreatePopupMenu();
            var flags = CMF.CMF_NORMAL;
            if ((User32.GetKeyState((int)User32.VK.VK_SHIFT) & 0x8000) != 0) flags |= CMF.CMF_EXTENDEDVERBS;
            context.QueryContextMenu(menu, 0, 1, 0x7FFF, flags).ThrowIfFailed();

            nint MenuMessage(nint window, uint message, nint wparam, nint lparam, ref bool handled)
            {
                if (message is not ((uint)User32.WindowMessage.WM_INITMENUPOPUP or
                    (uint)User32.WindowMessage.WM_MENUCHAR or (uint)User32.WindowMessage.WM_DRAWITEM or
                    (uint)User32.WindowMessage.WM_MEASUREITEM)) return 0;
                if (context is IContextMenu3 menu3)
                {
                    if (menu3.HandleMenuMsg2(message, wparam, lparam, out var result).Succeeded)
                    {
                        handled = true;
                        return result;
                    }
                }
                else if (context is IContextMenu2 menu2 && menu2.HandleMenuMsg(message, wparam, lparam).Succeeded)
                    handled = true;
                return 0;
            }

            if (verb is null)
            {
                if (position is not { } point) return;
                Win32Properties.AddWndProcHookCallback(owner, MenuMessage);
                uint command;
                try
                {
                    command = (uint)User32.TrackPopupMenuEx(menu,
                        User32.TrackPopupMenuFlags.TPM_RETURNCMD | User32.TrackPopupMenuFlags.TPM_RIGHTBUTTON,
                        point.X, point.Y, hwnd);
                }
                finally { Win32Properties.RemoveWndProcHookCallback(owner, MenuMessage); }
                if (command == 0) return;
                var nativeInfo = new CMINVOKECOMMANDINFOEX
                {
                    hwnd = hwnd,
                    nShow = ShowWindowCommand.SW_SHOWNORMAL,
                    fMask = CMIC.CMIC_MASK_NOASYNC,
                    lpVerb = (nint)(command - 1)
                };
                context.InvokeCommand(nativeInfo).ThrowIfFailed();
                return;
            }
            var info = new CMINVOKECOMMANDINFO(verb)
            {
                hwnd = hwnd,
                nShow = ShowWindowCommand.SW_SHOWNORMAL,
                fMask = CMIC.CMIC_MASK_NOASYNC
            };
            context.InvokeCommand(info).ThrowIfFailed();
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Failed to invoke Shell operation {Verb} for {Path}", verb ?? "context menu", path);
        }
        finally
        {
            if (context is not null) Marshal.ReleaseComObject(context);
            if (item is not null) Marshal.ReleaseComObject(item);
        }
    }

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
