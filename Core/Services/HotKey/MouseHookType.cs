namespace Core.Services.HotKey;

/// <summary>
/// 鼠标钩子类型 / Mouse hook type enumeration for different mouse buttons
/// </summary>
public enum MouseHookType
{
    /// <summary>鼠标左键 / Left mouse button</summary>
    LeftButton = 1,
    /// <summary>鼠标右键 / Right mouse button</summary>
    RightButton = 2,
    /// <summary>鼠标中键 / Middle mouse button</summary>
    MiddleButton = 3,
    /// <summary>鼠标侧键1 / Mouse side button 1</summary>
    XButton1 = 4,
    /// <summary>鼠标侧键2 / Mouse side button 2</summary>
    XButton2 = 5
}