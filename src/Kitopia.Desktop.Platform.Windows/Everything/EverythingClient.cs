#region

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Kitopia.Desktop.Features.Services.Config;
using Kitopia.Desktop.Features.Search;
using PluginCore;

#endregion

namespace Kitopia.Desktop.Platform.Windows.Everything;

public static class EverythingClient
{
    // Everything's SDK has one process-global query state. Concurrent Reset/Query calls corrupt
    // result sets, so discovery, availability checks, and interactive searches share this gate.
    private static readonly SemaphoreSlim QueryGate = new(1, 1);
    public static bool IsAvailable(CancellationToken cancellationToken = default)
    {
        QueryGate.Wait(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool available;
            if (IntPtr.Size == 8)
            {
                EverythingNative64.Everything_Reset();
                EverythingNative64.Everything_SetMax(1);
                available = EverythingNative64.Everything_QueryW(true);
            }
            else
            {
                EverythingNative32.Everything_Reset();
                EverythingNative32.Everything_SetMax(1);
                available = EverythingNative32.Everything_QueryW(true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            return available;
        }
        finally
        {
            QueryGate.Release();
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
                ? ReadIndexedPage64(offset)
                : ReadIndexedPage32(offset);
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

    public static IReadOnlyList<SearchViewItem> Search(string query, int count = 100) => Search(query, 0, count);

    public static IReadOnlyList<SearchViewItem> Search(string query, int offset, int count,
        EverythingSortOrder sortOrder = EverythingSortOrder.NameAscending,
        CancellationToken cancellationToken = default)
    {
        var is64Bit = IntPtr.Size == 8;
        List<SearchViewItem> results;
        QueryGate.Wait(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (is64Bit)
            {
                EverythingNative64.Everything_Reset();
                EverythingNative64.Everything_SetSearchW(query);
                EverythingNative64.Everything_SetSort((uint)sortOrder);
                EverythingNative64.Everything_SetMatchCase(!ConfigManger.Config.everythingSearchIgnoreCase);
                EverythingNative64.Everything_SetOffset(Math.Max(0, offset));
                EverythingNative64.Everything_SetMax(count <= 0 ? unchecked((int)0xFFFFFFFF) : count);
                EverythingNative64.Everything_SetRequestFlags(
                    EverythingNative64.EVERYTHING_REQUEST_FULL_PATH_AND_FILE_NAME |
                    EverythingNative64.EVERYTHING_REQUEST_SIZE |
                    EverythingNative64.EVERYTHING_REQUEST_DATE_MODIFIED);
                EnsureQuerySucceeded(EverythingNative64.Everything_QueryW(true), EverythingNative64.Everything_GetLastError());
            }
            else
            {
                EverythingNative32.Everything_Reset();
                EverythingNative32.Everything_SetSearchW(query);
                EverythingNative32.Everything_SetSort((uint)sortOrder);
                EverythingNative32.Everything_SetMatchCase(!ConfigManger.Config.everythingSearchIgnoreCase);
                EverythingNative32.Everything_SetOffset(Math.Max(0, offset));
                EverythingNative32.Everything_SetMax(count <= 0 ? unchecked((int)0xFFFFFFFF) : count);
                EverythingNative32.Everything_SetRequestFlags(
                    EverythingNative32.EVERYTHING_REQUEST_FULL_PATH_AND_FILE_NAME |
                    EverythingNative32.EVERYTHING_REQUEST_SIZE |
                    EverythingNative32.EVERYTHING_REQUEST_DATE_MODIFIED);
                EnsureQuerySucceeded(EverythingNative32.Everything_QueryW(true), EverythingNative32.Everything_GetLastError());
            }

            var numResults = is64Bit ? EverythingNative64.Everything_GetNumResults() : EverythingNative32.Everything_GetNumResults();
            results = new List<SearchViewItem>(numResults);
            const int bufferSize = 32 * 1024;
            var buffer = new StringBuilder(bufferSize);
            for (var i = 0; i < numResults; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                buffer.Clear();
                if (is64Bit) EverythingNative64.Everything_GetResultFullPathNameW(i, buffer, bufferSize);
                else EverythingNative32.Everything_GetResultFullPathNameW(i, buffer, bufferSize);
                var path = buffer.ToString();
                if (string.IsNullOrWhiteSpace(path)) continue;

                var isFolder = is64Bit ? EverythingNative64.Everything_IsFolderResult(i) : EverythingNative32.Everything_IsFolderResult(i);
                long? fileSize = null;
                long size;
                if (!isFolder && (is64Bit
                        ? EverythingNative64.Everything_GetResultSize(i, out size)
                        : EverythingNative32.Everything_GetResultSize(i, out size)) && size >= 0)
                {
                    fileSize = size;
                }

                DateTime? dateModified = null;
                long fileTime;
                if ((is64Bit
                        ? EverythingNative64.Everything_GetResultDateModified(i, out fileTime)
                        : EverythingNative32.Everything_GetResultDateModified(i, out fileTime)) && fileTime > 0)
                {
                    try
                    {
                        dateModified = DateTime.FromFileTime(fileTime);
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                        // Some indexed entries do not have a representable timestamp.
                    }
                }

                var name = Path.GetFileName(path);
                results.Add(new SearchViewItem
                {
                    OnlyKey = path,
                    ItemDisplayName = string.IsNullOrEmpty(name) ? path : name,
                    FileSize = fileSize,
                    DateModified = dateModified,
                    IsFolder = isFolder,
                    IsVisible = true
                });
            }
        }
        finally
        {
            QueryGate.Release();
        }

        // SDK data is now detached; filesystem metadata must not hold up other queries.
        foreach (var item in results)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!item.IsFolder && item.FileSize is null && count is > 0 and <= 200)
            {
                try
                {
                    var file = new FileInfo(item.OnlyKey);
                    if (file.Exists)
                    {
                        item.FileSize = file.Length;
                        item.DateModified ??= file.LastWriteTime;
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // Files can disappear or become inaccessible after the indexed query.
                }
            }

            var directory = Path.GetDirectoryName(item.OnlyKey);
            item.StartDirectory = string.IsNullOrEmpty(directory) ? item.OnlyKey : directory;
            item.FileType = item.IsFolder ? FileType.文件夹 : ResolveFileType(item.OnlyKey);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return results;
    }

    private const int DiscoveryPageSize = 2048;

    private static List<string> ReadIndexedPage32(int offset)
    {
        QueryGate.Wait();
        try
        {
            EverythingNative32.Everything_Reset();
            EverythingNative32.Everything_SetSearchW(string.Join("|", ConfigManger.Config.everythingIndexIncludePatterns));
            EverythingNative32.Everything_SetMatchCase(false);
            EverythingNative32.Everything_SetOffset(offset);
            EverythingNative32.Everything_SetMax(DiscoveryPageSize);
            EnsureQuerySucceeded(EverythingNative32.Everything_QueryW(true), EverythingNative32.Everything_GetLastError());

            var count = EverythingNative32.Everything_GetNumResults();
            return ReadPage(count, EverythingNative32.Everything_GetResultFullPathNameW);
        }
        finally
        {
            QueryGate.Release();
        }
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

    private static List<string> ReadIndexedPage64(int offset)
    {
        QueryGate.Wait();
        try
        {
            EverythingNative64.Everything_Reset();
            EverythingNative64.Everything_SetSearchW(string.Join("|", ConfigManger.Config.everythingIndexIncludePatterns));
            EverythingNative64.Everything_SetMatchCase(false);
            EverythingNative64.Everything_SetOffset(offset);
            EverythingNative64.Everything_SetMax(DiscoveryPageSize);
            EnsureQuerySucceeded(EverythingNative64.Everything_QueryW(true), EverythingNative64.Everything_GetLastError());

            var count = EverythingNative64.Everything_GetNumResults();
            return ReadPage(count, EverythingNative64.Everything_GetResultFullPathNameW);
        }
        finally
        {
            QueryGate.Release();
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
