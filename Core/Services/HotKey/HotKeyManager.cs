using Microsoft.Extensions.DependencyInjection;
using PluginCore;

namespace Core.Services.HotKey;

/// <summary>
/// 热键管理器 / Hot key manager for global hotkey registration and handling
/// </summary>
public class HotKeyManager
{
    /// <summary>热键实现接口 / Hot key implementation interface</summary>
    public static IHotKetImpl HotKetImpl;

    /// <summary>
    /// 初始化热键管理器 / Initialize the hot key manager
    /// </summary>
    public static void Init()
    {
        HotKetImpl = ServiceManager.Services.GetService<IHotKetImpl>()!;
        HotKetImpl.Init();
    }
}