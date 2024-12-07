using Core.SDKs.Services;
using log4net;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;

namespace Core.Window.Everything;

public class EverythingService : IEverythingService
{
    private static readonly ILog Log = LogManager.GetLogger(nameof(EverythingService));

    public bool isRun()
    {
        if (IntPtr.Size == 8)
        {
            // 64-bit
            var task = Task.Run<bool>(() =>
            {
                Everything64.Everything_SetMax(1);
                return Everything64.Everything_QueryW(true);
            });
            if (!task.Wait(TimeSpan.FromSeconds(1)))
            {
                Log.Error("Everything调用超时");
                ServiceManager.Services.GetService<IToastService>()!.Show("Everything", "Everything调用超时");
                return false;
            }

            return task.Result;
        }
        else
        {
            // 32-bit

            var task = Task.Run<bool>(() =>
            {
                Everything32.Everything_SetMax(1);
                return Everything32.Everything_QueryW(true);
            });
            if (!task.Wait(TimeSpan.FromSeconds(1)))
            {
                Log.Error("Everything调用超时");
                ServiceManager.Services.GetService<IToastService>()!.Show("Everything", "Everything调用超时");
                return false;
            }

            return task.Result;
        }
    }
}