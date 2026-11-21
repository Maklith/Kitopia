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
        if (results.Count <= 1)
        {
            return [.. results];
        }

        var scored = new (SearchIndexResult Result, int Index, double Score)[results.Count];
        for (var i = 0; i < results.Count; i++)
        {
            var result = results[i];
            scored[i] = (
                result,
                i,
                result.Weight * (1 + GetHistoryBoost(result.Source.OnlyKey, history, now)));
        }

        Array.Sort(scored, static (left, right) =>
        {
            var scoreComparison = right.Score.CompareTo(left.Score);
            return scoreComparison != 0
                ? scoreComparison
                : left.Index.CompareTo(right.Index);
        });

        var ranked = new List<SearchIndexResult>(scored.Length);
        for (var i = 0; i < scored.Length; i++)
        {
            ranked.Add(scored[i].Result);
        }

        return ranked;
    }

    private static double GetHistoryBoost(
        string onlyKey,
        IReadOnlyDictionary<string, HistoryItem> history,
        DateTime now)
    {
        if (!history.TryGetValue(onlyKey, out var item)) return 0;

        var accessTimes = item.AccessTimes;
        var hasValidAccess = false;
        var weightedAccesses = 0d;
        for (var i = 0; i < accessTimes.Count; i++)
        {
            var accessTime = accessTimes[i];
            if (accessTime > now) continue;
            hasValidAccess = true;
            weightedAccesses += Math.Pow(0.5, Math.Max(0, (now - accessTime).TotalDays) / RecencyHalfLifeDays);
        }

        if (!hasValidAccess) return 0;

        var preference = Math.Min(
            1,
            weightedAccesses / HistoryAccessSaturation);
        return MaxHistoryBoost * preference;
    }
}
