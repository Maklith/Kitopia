#region

using System.Text;
using Core.SDKs.Services;
using Core.SDKs.Services.Config;
using log4net;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;

#endregion

namespace Core.Window.Everything;

public class Tools
{
    private static readonly ILog Log = LogManager.GetLogger("EverythingTools");

    public static void main(List<string> Items)
    {
        var task = Task.Run(() =>
        {
            if (IntPtr.Size == 8)
                // 64-bit
                amd64(Items);
            else
                // 32-bit
                amd32(Items);
        });
        if (!task.Wait(TimeSpan.FromSeconds(1)))
        {
            Log.Error("Everything调用超时");
            ServiceManager.Services.GetService<IToastService>()!.Show("Everything", "Everything调用超时");
        }
    }

    public static void amd32(List<string> Items)
    {
        Everything32.Everything_Reset();
        Everything32.Everything_SetSearchW(string.Join("|", ConfigManger.Config!.everythingSearchExtensions));
        Everything32.Everything_SetMatchCase(true);
        Everything32.Everything_QueryW(true);
        const int bufsize = 260;
        var buf = new StringBuilder(bufsize);
        for (var i = 0; i < Everything32.Everything_GetNumResults(); i++)
        {
            // get the result's full path and file name.
            Everything32.Everything_GetResultFullPathNameW(i, buf, bufsize);
            var filePath = buf.ToString();
            Items.Add(filePath);
        }
    }

    public static void amd64(List<string> Items)
    {
        Everything64.Everything_Reset();
        Everything64.Everything_SetSearchW(string.Join("|", ConfigManger.Config!.everythingSearchExtensions));
        Everything64.Everything_SetMatchCase(true);
        Everything64.Everything_QueryW(true);
        const int bufsize = 260;
        var buf = new StringBuilder(bufsize);
        for (var i = 0; i < Everything64.Everything_GetNumResults(); i++)
        {
            // get the result's full path and file name.
            Everything64.Everything_GetResultFullPathNameW(i, buf, bufsize);
            var filePath = buf.ToString();
            Items.Add(filePath);
        }
    }
}