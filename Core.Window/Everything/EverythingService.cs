using Core.SDKs.Services;

using Microsoft.Extensions.DependencyInjection;
using PluginCore;

namespace Core.Window.Everything;

public class EverythingService : IEverythingService
{
    

    public bool IsRun()
    {
        return EverythingTools.IsRun();
    }
}