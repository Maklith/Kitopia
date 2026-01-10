using Core.SDKs.Services;
using Core.Services;
using Core.Services.Interfaces;
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