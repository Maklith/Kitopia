using Kitopia.Desktop.Features.Search;
using PluginCore;

namespace KitopiaTest.Search;

[TestClass]
public sealed class SearchIndexTests
{
    [TestMethod]
    public void TryAddAndRemove_Entry_UpdatesIndex()
    {
        var index = new SearchIndex();
        var entry = CreateEntry("c:\\apps\\kitopia.exe", "Kitopia");

        Assert.IsTrue(index.TryAdd(entry));
        Assert.IsFalse(index.TryAdd(entry));
        Assert.AreEqual(1, index.Count);
        Assert.IsTrue(index.TryGetValue(entry.OnlyKey, out var stored));
        Assert.AreEqual(entry, stored);

        Assert.IsTrue(index.TryRemove(entry.OnlyKey));
        Assert.AreEqual(0, index.Count);
        Assert.IsFalse(index.ContainsKey(entry.OnlyKey));
    }

    [TestMethod]
    public void RemoveWhere_MatchingEntries_RemovesOnlyMatches()
    {
        var index = new SearchIndex();
        index.TryAdd(CreateEntry("keep", "Keep"));
        index.TryAdd(CreateEntry("remove-1", "Remove one"));
        index.TryAdd(CreateEntry("remove-2", "Remove two"));

        var removed = index.RemoveWhere((key, _) => key.StartsWith("remove", StringComparison.Ordinal));

        Assert.AreEqual(2, removed);
        Assert.AreEqual(1, index.Count);
        Assert.IsTrue(index.ContainsKey("keep"));
    }

    [TestMethod]
    public void ToSearchViewItem_Entry_PreservesSearchMetadata()
    {
        var entry = new SearchEntry
        {
            DisplayName = "Kitopia",
            OnlyKey = "kitopia",
            FileType = FileType.应用程序,
            Arguments = "--open",
            StartDirectory = "c:\\apps",
            IconPath = "c:\\apps\\kitopia.ico",
            IconSymbol = 42
        };

        var item = entry.ToSearchViewItem();

        Assert.AreEqual(entry.DisplayName, item.ItemDisplayName);
        Assert.AreEqual(entry.OnlyKey, item.OnlyKey);
        Assert.AreEqual(entry.FileType, item.FileType);
        Assert.AreEqual(entry.Arguments, item.Arguments);
        Assert.AreEqual(entry.StartDirectory, item.StartDirectory);
        Assert.AreEqual(entry.IconPath, item.IconPath);
        Assert.AreEqual(entry.IconSymbol, item.IconSymbol);
        Assert.IsTrue(item.IsVisible);
    }

    [TestMethod]
    public async Task TryAddAndRefreshSearcher_ChineseDisplayName_IsSearchableByPinyin()
    {
        var index = new SearchIndex();
        var entry = CreateEntry("calculator", "测试工具");

        Assert.IsTrue(index.TryAddAndRefreshSearcher(entry));

        await WaitUntilAsync(() => index.Search("ceshi").Any(result => result.Source == entry));
    }

    [TestMethod]
    public async Task TryRemoveAndRefreshSearcher_ExistingEntry_RemovesItFromPinyinSearch()
    {
        var index = new SearchIndex();
        var entry = CreateEntry("calculator", "测试工具");
        index.TryAddAndRefreshSearcher(entry);
        await WaitUntilAsync(() => index.Search("ceshi").Any(result => result.Source == entry));

        Assert.IsTrue(index.TryRemoveAndRefreshSearcher(entry.OnlyKey));

        await WaitUntilAsync(() => index.Search("ceshi").All(result => result.Source != entry));
    }

    [TestMethod]
    public async Task SearchAsync_WhenSemanticSearchIsUnavailable_ReturnsPinyinMatches()
    {
        var index = new SearchIndex();
        var entry = CreateEntry("calculator", "测试工具");
        index.TryAddAndRefreshSearcher(entry);
        await WaitUntilAsync(() => index.Search("ceshi").Any(result => result.Source == entry));

        var results = await index.SearchAsync("ceshi", CancellationToken.None);

        var result = results.Single(result => result.Source == entry);
        Assert.IsNotNull(result.CharMatchResults);
        Assert.IsTrue(result.Weight > 0);
    }

    [TestMethod]
    public async Task SearchAsync_WhenCanceled_StopsBeforeSemanticRetrieval()
    {
        var index = new SearchIndex();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            () => index.SearchAsync("query", cancellation.Token));
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                Assert.Fail("The search index did not publish its refreshed Pinyin snapshot in time.");
            }

            await Task.Delay(20);
        }
    }

    private static SearchEntry CreateEntry(string key, string displayName)
    {
        return new SearchEntry
        {
            OnlyKey = key,
            DisplayName = displayName,
            FileType = FileType.文件
        };
    }
}
