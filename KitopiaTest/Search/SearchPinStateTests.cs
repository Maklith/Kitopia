using Kitopia.Desktop.Features.Search.ViewModels;

namespace KitopiaTest.Search;

[TestClass]
public sealed class SearchPinStateTests
{
    [TestMethod]
    public void SetPinned_UnpinnedPath_AddsItAtTheFront()
    {
        var pinnedPaths = new List<string> { "existing" };

        var changed = SearchPinState.SetPinned(pinnedPaths, "new", true);

        Assert.IsTrue(changed);
        CollectionAssert.AreEqual(new[] { "new", "existing" }, pinnedPaths);
        Assert.IsTrue(SearchPinState.IsPinned(pinnedPaths, "new"));
    }

    [TestMethod]
    public void SetPinned_AlreadyAtRequestedState_IsIdempotent()
    {
        var pinnedPaths = new List<string> { "existing" };

        var changed = SearchPinState.SetPinned(pinnedPaths, "existing", true);

        Assert.IsFalse(changed);
        CollectionAssert.AreEqual(new[] { "existing" }, pinnedPaths);
    }

    [TestMethod]
    public void SetPinned_PinnedPath_RemovesIt()
    {
        var pinnedPaths = new List<string> { "first", "remove", "last" };

        var changed = SearchPinState.SetPinned(pinnedPaths, "remove", false);

        Assert.IsTrue(changed);
        CollectionAssert.AreEqual(new[] { "first", "last" }, pinnedPaths);
        Assert.IsFalse(SearchPinState.IsPinned(pinnedPaths, "remove"));
    }
}
