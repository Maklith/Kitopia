namespace Kitopia.Desktop.Features.Search.ViewModels;

internal static class SearchPinState
{
    public static bool IsPinned(IReadOnlyCollection<string> pinnedPaths, string path)
    {
        return pinnedPaths.Contains(path, StringComparer.Ordinal);
    }

    public static bool SetPinned(IList<string> pinnedPaths, string path, bool pinned)
    {
        var isPinned = pinnedPaths.Any(candidate => string.Equals(candidate, path, StringComparison.Ordinal));
        if (isPinned == pinned) return false;

        if (pinned)
        {
            pinnedPaths.Insert(0, path);
        }
        else
        {
            pinnedPaths.Remove(path);
        }

        return true;
    }
}
