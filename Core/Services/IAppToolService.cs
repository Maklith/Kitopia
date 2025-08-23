using System.Collections.Concurrent;
using Pinyin.NET;
using PluginCore;

namespace Core.Services;

/// <summary>
/// 应用程序工具服务接口 / Application tool service interface for app-related operations
/// </summary>
public interface IAppToolService
{
    /// <summary>
    /// 应用程序求解器A / Application solver A for processing search results
    /// </summary>
    /// <param name="_collection">搜索项集合 / Collection of search items</param>
    /// <param name="search">搜索文本 / Search text</param>
    /// <param name="isSearch">是否为搜索 / Whether this is a search operation</param>
    public void AppSolverA(ConcurrentDictionary<string, SearchViewItem> _collection, string search,
        bool isSearch = false);

    /// <summary>
    /// 删除无效文件 / Remove null/invalid files from collection
    /// </summary>
    /// <param name="_collection">搜索项集合 / Collection of search items</param>
    public void DelNullFile(ConcurrentDictionary<string, SearchViewItem> _collection);

    /// <summary>
    /// 获取所有应用程序 / Get all applications
    /// </summary>
    /// <param name="_collection">搜索项集合 / Collection of search items</param>
    /// <param name="logging">是否记录日志 / Whether to enable logging</param>
    /// <param name="useEverything">是否使用Everything / Whether to use Everything search</param>
    public void GetAllApps(ConcurrentDictionary<string, SearchViewItem> _collection, bool logging,
        bool useEverything = false);

    /// <summary>
    /// 自动启动Everything / Auto start Everything search engine
    /// </summary>
    /// <param name="_collection">搜索项集合 / Collection of search items</param>
    /// <param name="action">回调动作 / Callback action</param>
    public void AutoStartEverything(ConcurrentDictionary<string, SearchViewItem> _collection, Action action);
    
    /// <summary>
    /// 使用Everything搜索 / Use Everything search
    /// </summary>
    /// <param name="s">搜索字符串 / Search string</param>
    /// <param name="limit">结果限制 / Result limit</param>
    /// <returns>搜索结果 / Search results</returns>
    public IEnumerable<SearchViewItem> UseEverythingSearch(string s, int limit = 50);
    
    /// <summary>
    /// 根据项目获取图标 / Get icon by search item
    /// </summary>
    /// <param name="item">搜索项 / Search item</param>
    public void GetIconByItem(SearchViewItem item);
    
    /// <summary>
    /// 根据自定义情景获取图标 / Get icon by custom scenario
    /// </summary>
    /// <param name="item">自定义情景 / Custom scenario item</param>
    public void GetIconByItem(CustomScenario.CustomScenario item);
    
    /// <summary>
    /// 获取拼音 / Get pinyin for input text
    /// </summary>
    /// <param name="input">输入文本 / Input text</param>
    /// <returns>拼音项 / Pinyin item</returns>
    public PinyinItem GetPinyin(string input);
}