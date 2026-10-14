using Kitopia.Desktop.Features.Search.ViewModels;
using PluginCore;

namespace KitopiaTest.Search;

[TestClass]
public sealed class SearchDisplayPolicyTests
{
    [TestMethod]
    public void ShouldUsePreview_EmptyQuery_KeepsLauncherList()
    {
        Assert.IsFalse(SearchDisplayPolicy.ShouldUsePreview(
            false,
            string.Empty,
            CreateItems(Pdf, Pdf, Pdf),
            new Dictionary<string, SearchResultContext>()));
    }

    [TestMethod]
    public void ShouldUsePreview_SelectMode_KeepsLauncherList()
    {
        Assert.IsFalse(SearchDisplayPolicy.ShouldUsePreview(
            true,
            "query",
            CreateItems(Word, Word, Word),
            new Dictionary<string, SearchResultContext>()));
    }

    [TestMethod]
    public void ShouldUsePreview_PreviewableDocumentsDominate_UsesSplitView()
    {
        Assert.IsTrue(SearchDisplayPolicy.ShouldUsePreview(
            false,
            "query",
            CreateItems(Pdf, Word, Excel, App, Command),
            new Dictionary<string, SearchResultContext>()));
    }

    [TestMethod]
    public void ShouldUsePreview_DocumentsDoNotDominate_KeepsLauncherList()
    {
        Assert.IsFalse(SearchDisplayPolicy.ShouldUsePreview(
            false,
            "query",
            CreateItems(Pdf, Word, App, Uwp),
            new Dictionary<string, SearchResultContext>()));
    }

    [TestMethod]
    public void ShouldUsePreview_ApplicationsRankedFirst_KeepsLauncherList()
    {
        Assert.IsFalse(SearchDisplayPolicy.ShouldUsePreview(
            false,
            "query",
            CreateItems(App, Uwp, Command, Pdf, Word, Excel, Pdf, Word),
            new Dictionary<string, SearchResultContext>()));
    }

    [TestMethod]
    public void ShouldUsePreview_SemanticContentMatch_UsesSplitViewForSingleDocument()
    {
        var items = CreateItems(Pdf);
        var contexts = new Dictionary<string, SearchResultContext>
        {
            [items[0].OnlyKey] = new(4)
        };

        Assert.IsTrue(SearchDisplayPolicy.ShouldUsePreview(false, "query", items, contexts));
    }

    private static List<SearchViewItem> CreateItems(params FileType[] fileTypes) =>
        fileTypes.Select((fileType, index) => new SearchViewItem
        {
            OnlyKey = $"item-{index}",
            ItemDisplayName = $"Item {index}",
            FileType = fileType
        }).ToList();

    private const FileType App = (FileType)0;
    private const FileType Word = (FileType)2;
    private const FileType Excel = (FileType)4;
    private const FileType Pdf = (FileType)5;
    private const FileType Command = (FileType)10;
    private const FileType Uwp = (FileType)12;
}
