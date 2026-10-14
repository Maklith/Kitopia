using PluginCore;

namespace Kitopia.Desktop.Features.Services.Interfaces;

/// <summary>
/// Narrow Core-owned port used by startup, configuration, and plugin hosting to
/// notify the desktop search feature without depending on its implementation.
/// </summary>
public interface ISearchFeatureService
{
    void SetEverythingAvailability(bool? isAvailable);

    void AddToIndex(string path);

    void RemoveFromIndex(string path);

    bool IsIndexed(string path);

    bool IsPinned(string path);

    void SetPinned(string path, bool pinned);

    void AddPluginItems(IEnumerable<SearchViewItem> items);

    void RemovePluginItems(IEnumerable<SearchViewItem> items);

    void RemoveAnalyzerIndex(object analyzer);
}
