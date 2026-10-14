using System.Runtime.InteropServices;
using System.Text;
using Avalonia;
using Kitopia.Desktop.Features.Services;
using Vanara.PInvoke;
using static Vanara.PInvoke.Shell32;

namespace Kitopia.Desktop.Platform.Windows;

public sealed class ExplorerFileSelection
{
    public (IReadOnlyList<string> Paths, PixelRect? Bounds) GetSelection()
    {
        var foreground = User32.GetForegroundWindow();
        var name = new StringBuilder(128);
        User32.GetClassName(foreground, name, name.Capacity);
        if (name.ToString() is not ("CabinetWClass" or "ExploreWClass" or "Progman" or "WorkerW"))
            return (Array.Empty<string>(), null);
        var windows = (IShellWindows)new ShellWindows();
        try
        {
            if (name.ToString() is "Progman" or "WorkerW")
            {
                object location = (int)CSIDL.CSIDL_DESKTOP;
                object root = 0;
                windows.FindWindowSW(ref location, ref root, ShellWindowTypeConstants.SWC_DESKTOP,
                    out _, ShellWindowFindWindowOptions.SWFO_NEEDDISPATCH, out var desktop).ThrowIfFailed();
                try { return ReadSelection(desktop); }
                finally { if (desktop is not null) Marshal.ReleaseComObject(desktop); }
            }

            for (var index = 0; index < windows.Count; index++)
            {
                var window = windows.Item(index);
                if (window is null) continue;
                try
                {
                    if ((nint)(long)((dynamic)window).HWND != (nint)foreground) continue;
                    var selection = ReadSelection(window);
                    if (selection.Paths.Count > 0) return selection;
                }
                catch (COMException) { }
                finally { Marshal.ReleaseComObject(window); }
            }
            return (Array.Empty<string>(), null);
        }
        catch (COMException exception)
        {
            LogManager.Logger.Warning(exception, "无法读取资源管理器选中的文件");
            return (Array.Empty<string>(), null);
        }
        finally { Marshal.ReleaseComObject(windows); }
    }

    private static (IReadOnlyList<string> Paths, PixelRect? Bounds) ReadSelection(object window)
    {
        var browserService = new Guid("4C96BE40-915C-11CF-99D3-00AA004AE837");
        var browserId = typeof(IShellBrowser).GUID;
        ((Shell32.IServiceProvider)window).QueryService(browserService, browserId, out var pointer).ThrowIfFailed();
        var browser = (IShellBrowser)Marshal.GetObjectForIUnknown(pointer);
        Marshal.Release(pointer);
        IShellView? view = null;
        IShellItemArray? selection = null;
        try
        {
            browser.QueryActiveShellView(out view).ThrowIfFailed();
            view.GetWindow(out var viewWindow).ThrowIfFailed();
            if (!User32.IsWindowVisible(viewWindow)) return (Array.Empty<string>(), null);
            ((IFolderView2)view).GetSelection(false, out selection).ThrowIfFailed();
            var files = new List<string>();
            for (uint index = 0; index < selection.GetCount(); index++)
            {
                var item = selection.GetItemAt(index);
                try
                {
                    var path = item.GetDisplayName(SIGDN.SIGDN_FILESYSPATH);
                    if (File.Exists(path) || Directory.Exists(path)) files.Add(path);
                }
                catch (COMException) { }
                finally { Marshal.ReleaseComObject(item); }
            }
            return (files, files.Count > 0 ? GetSelectedBounds((IFolderView2)view, viewWindow) : null);
        }
        finally
        {
            if (selection is not null) Marshal.ReleaseComObject(selection);
            if (view is not null) Marshal.ReleaseComObject(view);
            Marshal.ReleaseComObject(browser);
        }
    }

    private static PixelRect? GetSelectedBounds(IFolderView2 view, HWND viewWindow)
    {
        var threadInfo = new User32.GUITHREADINFO { cbSize = (uint)Marshal.SizeOf<User32.GUITHREADINFO>() };
        if (User32.GetGUIThreadInfo(0, ref threadInfo) && User32.IsChild(viewWindow, threadInfo.hwndFocus))
        {
            var accessibleId = new Guid("618736E0-3C3D-11CF-810C-00AA00389B71");
            if (AccessibleObjectFromWindow(threadInfo.hwndFocus, 0xFFFFFFFC, ref accessibleId, out var accessible) >= 0)
            {
                object? focused = null;
                try
                {
                    dynamic root = accessible;
                    focused = root.accFocus;
                    dynamic target = focused is int ? root : focused ?? root;
                    object child = focused is int index ? index : 0;
                    target.accLocation(out int x, out int y, out int width, out int height, child);
                    if (width > 0 && height > 0) return new PixelRect(x, y, width, height);
                }
                catch (Exception exception) when (exception is COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException) { }
                finally
                {
                    if (focused is not null && !ReferenceEquals(focused, accessible) && Marshal.IsComObject(focused))
                        Marshal.ReleaseComObject(focused);
                    Marshal.ReleaseComObject(accessible);
                }
            }
        }

        try
        {
            var index = view.GetFocusedItem();
            if (index < 0) return null;
            using var item = view.Item(index);
            var position = view.GetItemPosition(item);
            var spacing = view.GetSpacing();
            User32.ClientToScreen(viewWindow, ref position);
            return new PixelRect(position.X, position.Y, Math.Max(32, spacing.X), Math.Max(24, spacing.Y));
        }
        catch (COMException) { return null; }
    }

    [DllImport("oleacc.dll")]
    private static extern int AccessibleObjectFromWindow(HWND hwnd, uint objectId, ref Guid interfaceId,
        [MarshalAs(UnmanagedType.IDispatch)] out object accessible);
}
