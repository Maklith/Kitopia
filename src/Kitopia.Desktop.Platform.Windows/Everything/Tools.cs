#region

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

    public static IEnumerable<SearchViewItem> Search(string s, int count = 100) => Search(s, 0, count);

    public static IEnumerable<SearchViewItem> Search(string s, int offset, int count,
        EverythingSortOrder sortOrder = EverythingSortOrder.NameAscending)
    {
        lock (QueryGate)
        {
            if (IntPtr.Size == 8)
                return SearchAmd64(s, offset, count, sortOrder);
            return SearchAmd32(s, offset, count, sortOrder);
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

    public static IEnumerable<SearchViewItem> SearchAmd32(string s, int offset = 0, int count = 100,
        EverythingSortOrder sortOrder = EverythingSortOrder.NameAscending)
    {
        Everything32.Everything_Reset();
        Everything32.Everything_SetSearchW(s);
        Everything32.Everything_SetSort((uint)sortOrder);
        Everything32.Everything_SetMatchCase(!ConfigManger.Config.everythingSearchIgnoreCase);
        Everything32.Everything_SetOffset(Math.Max(0, offset));
        Everything32.Everything_SetMax(count <= 0 ? unchecked((int)0xFFFFFFFF) : count);
        Everything32.Everything_SetRequestFlags(
            Everything32.EVERYTHING_REQUEST_FULL_PATH_AND_FILE_NAME |
            Everything32.EVERYTHING_REQUEST_SIZE |
            Everything32.EVERYTHING_REQUEST_DATE_MODIFIED);
        Everything32.Everything_QueryW(true);

        const int bufsize = 4096;
        var buf = new StringBuilder(bufsize);
        var numResults = Everything32.Everything_GetNumResults();
        var results = new List<SearchViewItem>(numResults);

        for (var i = 0; i < numResults; i++)
        {
            buf.Clear();
            Everything32.Everything_GetResultFullPathNameW(i, buf, bufsize);
            var filePath = buf.ToString();
            if (string.IsNullOrWhiteSpace(filePath)) continue;

            var isFolder = Everything32.Everything_IsFolderResult(i);
            long? fileSize = null;
            if (!isFolder && Everything32.Everything_GetResultSize(i, out var size) && size >= 0)
            {
                fileSize = size;
            }

            DateTime? dateModified = null;
            if (Everything32.Everything_GetResultDateModified(i, out var fileTime) && fileTime > 0)
            {
                try
                {
                    dateModified = DateTime.FromFileTime(fileTime);
                }
                catch
                {
                    // Ignore date conversion failure
                }
            }

            if (!isFolder && fileSize == null && count <= 200)
            {
                try
                {
                    if (File.Exists(filePath))
                    {
                        var fi = new FileInfo(filePath);
                        fileSize = fi.Length;
                        dateModified ??= fi.LastWriteTime;
                    }
                }
                catch
                {
                    // Ignore disk lookup failure
                }
            }

            var fileName = Path.GetFileName(filePath);
            if (string.IsNullOrEmpty(fileName))
            {
                fileName = filePath;
            }

            var dir = Path.GetDirectoryName(filePath);
            if (string.IsNullOrEmpty(dir))
            {
                dir = filePath;
            }

            var fileType = isFolder ? FileType.文件夹 : ResolveFileType(filePath);

            results.Add(new SearchViewItem
            {
                ItemDisplayName = fileName,
                OnlyKey = filePath,
                StartDirectory = dir,
                FileType = fileType,
                FileSize = fileSize,
                DateModified = dateModified,
                IsFolder = isFolder,
                IsVisible = true
            });
        }

        return results;
    }

    public static IEnumerable<SearchViewItem> SearchAmd64(string s, int offset = 0, int count = 100,
        EverythingSortOrder sortOrder = EverythingSortOrder.NameAscending)
    {
        Everything64.Everything_Reset();
        Everything64.Everything_SetSearchW(s);
        Everything64.Everything_SetSort((uint)sortOrder);
        Everything64.Everything_SetOffset(Math.Max(0, offset));
        Everything64.Everything_SetMax(count <= 0 ? unchecked((int)0xFFFFFFFF) : count);
        Everything64.Everything_SetMatchCase(!ConfigManger.Config.everythingSearchIgnoreCase);
        Everything64.Everything_SetRequestFlags(
            Everything64.EVERYTHING_REQUEST_FULL_PATH_AND_FILE_NAME |
            Everything64.EVERYTHING_REQUEST_SIZE |
            Everything64.EVERYTHING_REQUEST_DATE_MODIFIED);
        Everything64.Everything_QueryW(true);

        const int bufsize = 4096;
        var buf = new StringBuilder(bufsize);
        var numResults = Everything64.Everything_GetNumResults();
        var results = new List<SearchViewItem>(numResults);

        for (var i = 0; i < numResults; i++)
        {
            buf.Clear();
            Everything64.Everything_GetResultFullPathNameW(i, buf, bufsize);
            var filePath = buf.ToString();
            if (string.IsNullOrWhiteSpace(filePath)) continue;

            var isFolder = Everything64.Everything_IsFolderResult(i);
            long? fileSize = null;
            if (!isFolder && Everything64.Everything_GetResultSize(i, out var size) && size >= 0)
            {
                fileSize = size;
            }

            DateTime? dateModified = null;
            if (Everything64.Everything_GetResultDateModified(i, out var fileTime) && fileTime > 0)
            {
                try
                {
                    dateModified = DateTime.FromFileTime(fileTime);
                }
                catch
                {
                    // Ignore date conversion failure
                }
            }

            if (!isFolder && fileSize == null && count <= 200)
            {
                try
                {
                    if (File.Exists(filePath))
                    {
                        var fi = new FileInfo(filePath);
                        fileSize = fi.Length;
                        dateModified ??= fi.LastWriteTime;
                    }
                }
                catch
                {
                    // Ignore disk lookup failure
                }
            }

            var fileName = Path.GetFileName(filePath);
            if (string.IsNullOrEmpty(fileName))
            {
                fileName = filePath;
            }

            var dir = Path.GetDirectoryName(filePath);
            if (string.IsNullOrEmpty(dir))
            {
                dir = filePath;
            }

            var fileType = isFolder ? FileType.文件夹 : ResolveFileType(filePath);

            results.Add(new SearchViewItem
            {
                ItemDisplayName = fileName,
                OnlyKey = filePath,
                StartDirectory = dir,
                FileType = fileType,
                FileSize = fileSize,
                DateModified = dateModified,
                IsFolder = isFolder,
                IsVisible = true
            });
        }

        return results;
    }

    private static FileType ResolveFileType(string path)
    {
        var ext = Path.GetExtension(path);
        if (string.IsNullOrEmpty(ext)) return FileType.文件;
        return ext.ToLowerInvariant() switch
        {
            ".exe" => FileType.应用程序,
            ".pdf" => FileType.PDF文档,
            ".doc" or ".docx" => FileType.Word文档,
            ".xls" or ".xlsx" => FileType.Excel文档,
            ".ppt" or ".pptx" => FileType.PPT文档,
            ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".webp" or ".svg" or ".ico" => FileType.图像,
            _ => FileType.文件
        };
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
