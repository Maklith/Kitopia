using Core.SDKs.Services;
using log4net;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;

namespace Core.Window.Everything;

public class EverythingService : IEverythingService
{
    private static readonly ILog Log = LogManager.GetLogger(nameof(EverythingService));

    public bool IsRun()
    {
        return EverythingTools.IsRun();
    }
}