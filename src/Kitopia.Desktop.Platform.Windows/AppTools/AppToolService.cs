using System.Drawing.Imaging;
using Avalonia.Threading;
using Kitopia.Desktop.Features.Services.Interfaces;
using Kitopia.Desktop.Features.Utils;
using Kitopia.Desktop.Platform.Windows.Everything;
using Kitopia.Desktop.Features.Search;
using Kitopia.Desktop.Features.Indexing;
using Pinyin.NET;
using PluginCore;
using Vanara.Extensions;
using Vanara.PInvoke;

namespace Kitopia.Desktop.Platform.Windows.AppTools;

public class AppToolService : IAppToolService
{
    public static (long StartTimeTicks, TimeSpan ProcessorTime)? ReadProcessTimes(System.Diagnostics.Process process)
    {
        using var handle = Kernel32.OpenProcess(
            (uint)Kernel32.ProcessAccess.PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)process.Id);
        if (handle.IsInvalid || !Kernel32.GetProcessTimes(handle, out var creation, out _, out var kernel, out var user))
        {
            return null;
        }

        return (DateTime.FromFileTimeUtc((long)creation.ToUInt64()).Ticks,
            TimeSpan.FromTicks((long)(kernel.ToUInt64() + user.ToUInt64())));
    }

    public void IndexItem(ISearchEntryIndex index, string filePath,
        bool isStarred = false)
    {
        AppIndexer.IndexItem(index, filePath, isStarred);
    }

    public void CleanupInvalidItems(IIndexService index)
    {
        AppIndexer.CleanupInvalidItems(index);
    }

    public void IndexAllApps(IIndexService index, bool logging,
        bool useEverythingForIndexing = false)
    {
        AppIndexer.IndexAllApps(index, logging, useEverythingForIndexing);
    }

    public void AutoStartEverything(IIndexService index, Action onSuccess)
    {
        AppIndexer.AutoStartEverything(index, onSuccess);
    }

    public void VisitEverythingIndexedFiles(Action<string> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        EverythingClient.VisitIndexedFiles(visitor);
    }

    public IEnumerable<string> EnumerateEverythingIndexedFiles() => EverythingClient.EnumerateIndexedFiles();

    public IReadOnlyList<SearchViewItem> SearchWithEverything(string query, int count = 100)
    {
        return EverythingClient.Search(query, 0, count);
    }

    public IReadOnlyList<SearchViewItem> SearchWithEverything(string query, int offset, int count,
        EverythingSortOrder sortOrder = EverythingSortOrder.NameAscending,
        CancellationToken cancellationToken = default)
    {
        return EverythingClient.Search(query, offset, count, sortOrder, cancellationToken);
    }

    public void LoadIcon(SearchViewItem item)
    {
        IconTools.GetIconByItem(item);
    }

    public void LoadIcon(Kitopia.Desktop.Features.CustomScenario.CustomScenario item)
    {
        IconTools.GetIconByItem(item);
    }

    public void LoadIcon(string filePath, Action<Avalonia.Media.Imaging.Bitmap?> callback)
    {
        Task.Run(() =>
        {
            try
            {
                using var icon = IconTools.GetIconFromImageList(filePath);
                if (icon is null)
                {
                    Dispatcher.UIThread.InvokeAsync(() => callback(null));
                    return;
                }

                using var bm = icon.ToBitmap();
                var resized = new System.Drawing.Bitmap(bm, new System.Drawing.Size(64, 64));
                var avaloniaBitmap = resized.ToAvaloniaBitmap();
                Dispatcher.UIThread.InvokeAsync(() => callback(avaloniaBitmap));
            }
            catch
            {
                Dispatcher.UIThread.InvokeAsync(() => callback(null));
            }
        });
    }

    public byte[]? GetFileIconPng(string filePath)
    {
        try
        {
            using var icon = IconTools.GetIconFromImageList(filePath);
            if (icon is null) return null;
            using var bm = icon.ToBitmap();
            using var resized = new System.Drawing.Bitmap(bm, new System.Drawing.Size(64, 64));
            using var ms = new MemoryStream();
            resized.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            return ms.ToArray();
        }
        catch
        {
            return null;
        }
    }

    public PinyinItem GetPinyin(string input)
    {
        return AppIndexer.PinyinProcessor.GetPinyin(input);
    }
}
