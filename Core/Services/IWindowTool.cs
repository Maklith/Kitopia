using Avalonia.Controls;

namespace Core.Services;

public interface IWindowTool
{
    void SetForegroundWindow(IntPtr hWnd);
    void MoveWindowToMouseScreenCenter(Window window);
}