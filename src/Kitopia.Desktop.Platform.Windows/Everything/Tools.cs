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
        foreach (var path in EnumerateIndexedFiles())
        {
            visitor(path);
        }
    }

    public static IEnumerable<string> EnumerateIndexedFiles()
    {
        for (var offset = 0; ;)
        {
            var page = IntPtr.Size == 8
                ? ReadIndexedPageAmd64(offset)
                : ReadIndexedPageAmd32(offset);
            foreach (var path in page)
            {
                yield return path;
            }

            if (page.Count < DiscoveryPageSize || offset > int.MaxValue - page.Count)
            {
                yield break;
            }

            offset += page.Count;
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

    private const int DiscoveryPageSize = 2048;

    private static List<string> ReadIndexedPageAmd32(int offset)
    {
        lock (QueryGate)
        {
            Everything32.Everything_Reset();
            Everything32.Everything_SetSearchW(string.Join("|", ConfigManger.Config.everythingSearchExtensions));
            Everything32.Everything_SetMatchCase(false);
            Everything32.Everything_SetOffset(offset);
            Everything32.Everything_SetMax(DiscoveryPageSize);
            EnsureQuerySucceeded(Everything32.Everything_QueryW(true), Everything32.Everything_GetLastError());

            var count = Everything32.Everything_GetNumResults();
            return ReadPage(count, Everything32.Everything_GetResultFullPathNameW);
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

    private static List<string> ReadIndexedPageAmd64(int offset)
    {
        lock (QueryGate)
        {
            Everything64.Everything_Reset();
            Everything64.Everything_SetSearchW(string.Join("|", ConfigManger.Config.everythingSearchExtensions));
            Everything64.Everything_SetMatchCase(false);
            Everything64.Everything_SetOffset(offset);
            Everything64.Everything_SetMax(DiscoveryPageSize);
            EnsureQuerySucceeded(Everything64.Everything_QueryW(true), Everything64.Everything_GetLastError());

            var count = Everything64.Everything_GetNumResults();
            return ReadPage(count, Everything64.Everything_GetResultFullPathNameW);
        }
    }

    private static List<string> ReadPage(
        int count,
        Action<int, StringBuilder, int> getPath,
        List<string>? paths = null)
    {
        // Keep the unmanaged result set bounded to one page. A large result query otherwise
        // makes Everything retain every matching path before indexing even starts.
        const int bufferSize = 32 * 1024;
        paths ??= new List<string>(Math.Min(count, DiscoveryPageSize));
        var buffer = new StringBuilder(bufferSize);
        for (var index = 0; index < count; index++)
        {
            buffer.Clear();
            getPath(index, buffer, buffer.Capacity);
            if (buffer.Length > 0)
            {
                paths.Add(buffer.ToString());
            }
        }

        return paths;
    }

    private static void EnsureQuerySucceeded(bool succeeded, int error)
    {
        if (!succeeded)
        {
            throw new InvalidOperationException($"Everything query failed (error {error}).");
        }
    }
}
