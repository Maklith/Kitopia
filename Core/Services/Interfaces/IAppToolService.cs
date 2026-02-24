using System.Collections.Concurrent;
using Pinyin.NET;
using PluginCore;

namespace Core.Services.Interfaces;

/// <summary>
/// 应用程序工具服务接口 / Application tool service interface for app-related operations
/// </summary>
public interface IAppToolService
{
    /// <summary>
    /// 解析并添加指定路径的应用或文件到搜索集合 / Parse and add the specified path (app/file) to the search collection
    /// </summary>
    /// <param name="collection">搜索项集合 / Collection of search items</param>
    /// <param name="filePath">文件或目录路径 / File or directory path</param>
    /// <param name="isStarred">是否标星(收藏) / Whether the item is starred</param>
    public void IndexItem(ConcurrentDictionary<string, SearchViewItem> collection, string filePath,
        bool isStarred = false);

    /// <summary>
    /// 清理集合中无效的文件和应用 / Remove invalid files and directories from the collection
    /// </summary>
    /// <param name="collection">搜索项集合 / Collection of search items</param>
    public void CleanupInvalidItems(ConcurrentDictionary<string, SearchViewItem> collection);

    /// <summary>
    /// 索引所有应用程序 / Index all applications
    /// </summary>
    /// <param name="collection">搜索项集合 / Collection of search items</param>
    /// <param name="logging">是否记录日志 / Whether to enable logging</param>
    /// <param name="useEverything">是否使用Everything / Whether to use Everything search</param>
    public void IndexAllApps(ConcurrentDictionary<string, SearchViewItem> collection, bool logging,
        bool useEverything = false);

    /// <summary>
    /// 自动启动Everything / Auto start Everything search engine via scheduled task
    /// </summary>
    /// <param name="collection">搜索项集合 / Collection of search items</param>
    /// <param name="onSuccess">启动成功的回调 / Callback action on success</param>
    public void AutoStartEverything(ConcurrentDictionary<string, SearchViewItem> collection, Action onSuccess);
    
    /// <summary>
    /// 使用Everything搜索 / Use Everything search
    /// </summary>
    /// <param name="keyword">搜索关键字 / Search keyword</param>
    /// <param name="limit">结果限制 / Result limit</param>
    /// <returns>搜索结果 / Search results</returns>
    public IEnumerable<SearchViewItem> SearchWithEverything(string keyword, int limit = 50);
    
    /// <summary>
    /// 获取SearchViewItem图标 / Get icon by search item
    /// </summary>
    /// <param name="item">搜索项 / Search item</param>
    public void LoadIcon(SearchViewItem item);
    
    /// <summary>
    /// 获取自定义情景图标 / Get icon by custom scenario
    /// </summary>
    /// <param name="item">自定义情景 / Custom scenario item</param>
    public void LoadIcon(CustomScenario.CustomScenario item);
    
    /// <summary>
    /// 获取文本拼音 / Get pinyin for input text
    /// </summary>
    /// <param name="input">输入文本 / Input text</param>
    /// <returns>拼音项 / Pinyin item</returns>
    public PinyinItem GetPinyin(string input);
}