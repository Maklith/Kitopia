using System.Drawing.Imaging;
using Avalonia.Threading;
using Kitopia.Desktop.Features.Services.Interfaces;
using Kitopia.Desktop.Features.Utils;
using Kitopia.Desktop.Platform.Windows.Everything;
using Kitopia.Desktop.Features.Search;
using Kitopia.Desktop.Features.Indexing;
using Pinyin.NET;
using PluginCore;

namespace Kitopia.Desktop.Platform.Windows.AppTools;

public class AppToolService : IAppToolService
{
    public void IndexItem(ISearchEntryIndex index, string filePath,
        bool isStarred = false)
    {
        AppSolver.IndexItem(index, filePath, isStarred);
    }

    public void CleanupInvalidItems(IIndexService index)
    {
        AppSolver.CleanupInvalidItems(index);
    }

    public void IndexAllApps(IIndexService index, bool logging,
        bool useEverything = false)
    {
        AppSolver.IndexAllApps(index, logging, useEverything);
    }

    public void AutoStartEverything(IIndexService index, Action onSuccess)
    {
        AppSolver.AutoStartEverything(index, onSuccess);
    }

    public void VisitEverythingIndexedFiles(Action<string> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        EverythingTools.VisitIndexedFiles(visitor);
    }

    public IEnumerable<SearchViewItem> SearchWithEverything(string keyword, int limit = 50)
    {
        return EverythingTools.Search(keyword, limit);
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
        return AppSolver.PinyinProcessor.GetPinyin(input);
    }
}
