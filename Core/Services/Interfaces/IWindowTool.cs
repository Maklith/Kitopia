using Avalonia.Controls;

namespace Core.Services.Interfaces;

public interface IWindowTool
{
    void SetForegroundWindow(IntPtr hWnd);
    void MoveWindowToMouseScreenCenter(Window window);
}