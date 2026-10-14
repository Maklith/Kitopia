using Kitopia.Desktop.Features.Services.Config;

namespace Kitopia.Desktop.Features.Search;

internal static class SearchResultRanker
{
    private const double MaxHistoryBoost = 1;
    private const double RecencyHalfLifeDays = 14;
    private const int HistoryAccessSaturation = 6;

    public static List<SearchIndexResult> Rank(
        IReadOnlyList<SearchIndexResult> results,
        IReadOnlyDictionary<string, HistoryItem> history,
        DateTime now)
    {
        return results
            .Select((result, index) => new
            {
                Result = result,
                Index = index,
                Score = result.Weight * (1 + GetHistoryBoost(result.Source.OnlyKey, history, now))
            })
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Index)
            .Select(candidate => candidate.Result)
            .ToList();
    }

    private static double GetHistoryBoost(
        string onlyKey,
        IReadOnlyDictionary<string, HistoryItem> history,
        DateTime now)
    {
        if (!history.TryGetValue(onlyKey, out var item)) return 0;

        var accessTimes = item.AccessTimes.Where(accessTime => accessTime <= now).ToList();
        if (accessTimes.Count == 0) return 0;

        var weightedAccesses = accessTimes.Sum(accessTime =>
            Math.Pow(0.5, Math.Max(0, (now - accessTime).TotalDays) / RecencyHalfLifeDays));
        var preference = Math.Min(
            1,
            weightedAccesses / HistoryAccessSaturation);
        return MaxHistoryBoost * preference;
    }
}
