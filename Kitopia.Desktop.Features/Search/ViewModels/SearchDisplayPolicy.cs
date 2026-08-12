using PluginCore;

namespace Kitopia.Desktop.Features.Search.ViewModels;

internal static class SearchDisplayPolicy
{
    private const int MinimumPreviewResultCount = 3;
    private const double PreviewResultRatio = 0.6;
    private const int TopResultCount = 5;

    public static bool ShouldUsePreview(
        bool isInSelectMode,
        string? query,
        IReadOnlyCollection<SearchViewItem> items,
        IReadOnlyDictionary<string, SearchResultContext> resultContexts)
    {
        if (isInSelectMode || string.IsNullOrWhiteSpace(query) || items.Count == 0)
        {
            return false;
        }

        var topItems = items.Take(TopResultCount).ToList();
        if (!IsPreviewCandidate(topItems[0]))
        {
            return false;
        }

        var previewableItems = topItems.Where(IsPreviewCandidate).ToList();
        if (previewableItems.Count == 0)
        {
            return false;
        }

        if (previewableItems.Any(item => resultContexts.TryGetValue(item.OnlyKey, out var context)
                                         && context.SemanticContentChunkIndex is not null))
        {
            return true;
        }

        return previewableItems.Count >= MinimumPreviewResultCount
               && (double)previewableItems.Count / topItems.Count >= PreviewResultRatio;
    }

    public static bool IsPreviewCandidate(SearchViewItem item)
    {
        return item.FileType is FileType.Word文档
            or FileType.PPT文档
            or FileType.Excel文档
            or FileType.PDF文档
            or FileType.图像
            or FileType.文件;
    }
}

internal sealed record SearchResultContext(int? SemanticContentChunkIndex);
