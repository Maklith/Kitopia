#region

using System.Text;
using Core.Services;
using Core.Services.Config;
using Core.Window.AppTools;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using Serilog;

#endregion

namespace Core.Window.Everything;

public class EverythingTools
{
    private static readonly ILogger Logger =  LogManager.Logger.ForContext<EverythingTools>();
    public static bool IsRun()
    {
        if (IntPtr.Size == 8)
        {
            // 64-bit
            var task = Task.Run(() =>
            {
                Everything64.Everything_SetMax(1);
                return Everything64.Everything_QueryW(true);
            });
            if (!task.Wait(TimeSpan.FromSeconds(1)))
            {
                Logger.Error("Everything调用超时");
                ServiceManager.Services.GetService<IToastService>()!.Show("Everything", "Everything调用超时");
                return false;
            }

            return task.Result;
        }
        else
        {
            // 32-bit

            var task = Task.Run(() =>
            {
                Everything32.Everything_SetMax(1);
                return Everything32.Everything_QueryW(true);
            });
            if (!task.Wait(TimeSpan.FromSeconds(1)))
            {
                Logger.Error("Everything调用超时");
                ServiceManager.Services.GetService<IToastService>()!.Show("Everything", "Everything调用超时");
                return false;
            }

            return task.Result;
        }
    }
    public static void Index(List<string> items)
    {
        
        var task = Task.Run(() =>
        {
            if (IntPtr.Size == 8)
                // 64-bit
                IndexAmd64(items);
            else
                // 32-bit
                IndexAmd32(items);
        });
        if (!task.Wait(TimeSpan.FromSeconds(1)))
        {
            Logger.Error("Everything调用超时");
            ServiceManager.Services.GetService<IToastService>()!.Show("Everything", "Everything调用超时");
        }
    }

    public static IEnumerable<SearchViewItem> Search(string s,int limit=50)
    {
        var task = Task.Run(() => {
            if (IntPtr.Size == 8)
                // 64-bit
                return SearchAmd64(s,limit);
            // 32-bit
            return SearchAmd32(s,limit);
        });
        if (!task.Wait(TimeSpan.FromSeconds(1)))
        {
            Logger.Error("Everything调用超时");
            ServiceManager.Services.GetService<IToastService>()!.Show("Everything", "Everything调用超时");
        }

        return task.Result;
    }

    private static void IndexAmd32(List<string> items)
    {
        
        Everything32.Everything_Reset();
        Everything32.Everything_SetSearchW(string.Join("|", ConfigManger.Config.everythingSearchExtensions));
        Everything32.Everything_SetMatchCase(true);
        Everything32.Everything_QueryW(true);
        const int bufsize = 260;
        var buf = new StringBuilder(bufsize);
        for (var i = 0; i < Everything32.Everything_GetNumResults(); i++)
        {
            // get the result's full path and file name.
            Everything32.Everything_GetResultFullPathNameW(i, buf, bufsize);
            var filePath = buf.ToString();
            items.Add(filePath);
        }
    }
    public static IEnumerable<SearchViewItem> SearchAmd32(string s,int limit=50)
    {
        Everything32.Everything_Reset();
        Everything32.Everything_SetSearchW(s);
        Everything32.Everything_SetMatchCase(true);
        Everything32.Everything_SetMax(limit);
        Everything32.Everything_QueryW(true);
        const int bufsize = 260;
        var buf = new StringBuilder(bufsize);
        var index = new SearchIndex();
        for (var i = 0; i < Everything32.Everything_GetNumResults(); i++)
        {
            Everything32.Everything_GetResultFullPathNameW(i, buf, bufsize);
            var filePath = buf.ToString();
            AppSolver.IndexItem(index,filePath);
        }

        return index.GetEntriesSnapshot().Select(e => e.Value.ToSearchViewItem());
    }
    public static IEnumerable<SearchViewItem> SearchAmd64(string s,int limit=50)
    {
        Everything64.Everything_Reset();
        Everything64.Everything_SetSearchW(s);
        Everything64.Everything_SetMax(limit);
        Everything64.Everything_SetMatchCase(true);
        Everything64.Everything_QueryW(true);
        const int bufsize = 260;
        var buf = new StringBuilder(bufsize);
        var index = new SearchIndex();
        for (var i = 0; i < Everything64.Everything_GetNumResults(); i++)
        {
            Everything64.Everything_GetResultFullPathNameW(i, buf, bufsize);
            var filePath = buf.ToString();
            AppSolver.IndexItem(index,filePath);
        }

        return index.GetEntriesSnapshot().Select(e => e.Value.ToSearchViewItem());
    }

    private static void IndexAmd64(List<string> items)
    {
        Everything64.Everything_Reset();
        Everything64.Everything_SetSearchW(string.Join("|", ConfigManger.Config.everythingSearchExtensions));
        Everything64.Everything_SetMatchCase(true);
        Everything64.Everything_QueryW(true);
        const int bufsize = 260;
        var buf = new StringBuilder(bufsize);
        for (var i = 0; i < Everything64.Everything_GetNumResults(); i++)
        {
            // get the result's full path and file name.
            Everything64.Everything_GetResultFullPathNameW(i, buf, bufsize);
            var filePath = buf.ToString();
            items.Add(filePath);
        }
    }
}