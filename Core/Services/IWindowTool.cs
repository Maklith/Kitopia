using Avalonia.Controls;

namespace Core.SDKs.Services;

public interface IWindowTool
{
    void SetForegroundWindow(IntPtr hWnd);
    void MoveWindowToMouseScreenCenter(Window window);
}