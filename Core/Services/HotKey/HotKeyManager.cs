using Microsoft.Extensions.DependencyInjection;
using PluginCore;

namespace Core.Services.HotKey;

public class HotKeyManager
{
    public static IHotKetImpl HotKetImpl;

    public static void Init()
    {
        HotKetImpl = ServiceManager.Services.GetService<IHotKetImpl>()!;
        HotKetImpl.Init();
    }
}