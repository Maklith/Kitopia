#region

using System.Text;
using Kitopia.Desktop.Features.Services.Config;
using Kitopia.Desktop.Platform.Windows.AppTools;
using Kitopia.Desktop.Features.Search;
using PluginCore;

#endregion

namespace Kitopia.Desktop.Platform.Windows.Everything;

public class EverythingTools
{
    // Everything's SDK has one process-global query state. Concurrent Reset/Query calls corrupt
    // result sets, so discovery, availability checks, and interactive searches share this gate.
    private static readonly object QueryGate = new();
    public static bool IsRun()
    {
        lock (QueryGate)
        {
            if (IntPtr.Size == 8)
            {
                Everything64.Everything_SetMax(1);
                return Everything64.Everything_QueryW(true);
            }

            Everything32.Everything_SetMax(1);
            return Everything32.Everything_QueryW(true);
        }
    }
    public static void VisitIndexedFiles(Action<string> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        lock (QueryGate)
        {
            if (IntPtr.Size == 8)
                VisitIndexedFilesAmd64(visitor);
            else
                VisitIndexedFilesAmd32(visitor);
        }
    }

    public static IEnumerable<SearchViewItem> Search(string s,int limit=50)
    {
        lock (QueryGate)
        {
            if (IntPtr.Size == 8)
                return SearchAmd64(s,limit);
            return SearchAmd32(s,limit);
        }
    }

    private static void VisitIndexedFilesAmd32(Action<string> visitor)
    {
        
        Everything32.Everything_Reset();
        Everything32.Everything_SetSearchW(string.Join("|", ConfigManger.Config.everythingSearchExtensions));
        Everything32.Everything_SetMatchCase(false);
        Everything32.Everything_QueryW(true);
        const int bufsize = 260;
        var buf = new StringBuilder(bufsize);
        for (var i = 0; i < Everything32.Everything_GetNumResults(); i++)
        {
            // get the result's full path and file name.
            Everything32.Everything_GetResultFullPathNameW(i, buf, bufsize);
            var filePath = buf.ToString();
            visitor(filePath);
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
        Everything64.Everything_SetMatchCase(false);
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

    private static void VisitIndexedFilesAmd64(Action<string> visitor)
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
            visitor(filePath);
        }
    }
}
