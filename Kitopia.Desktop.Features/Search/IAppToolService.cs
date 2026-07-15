using Pinyin.NET;
using PluginCore;

namespace Kitopia.Desktop.Features.Search;

/// <summary>
/// 应用程序工具服务接口 / Application tool service interface for app-related operations
/// </summary>
public interface IAppToolService
{
    /// <summary>
    /// 解析并添加指定路径的应用或文件到搜索集合 / Parse and add the specified path (app/file) to the search collection
    /// </summary>
    /// <param name="index">搜索索引 / Search index</param>
    /// <param name="filePath">文件或目录路径 / File or directory path</param>
    /// <param name="isStarred">是否标星(收藏) / Whether the item is starred</param>
    public void IndexItem(SearchIndex index, string filePath,
        bool isStarred = false);

    /// <summary>
    /// 清理集合中无效的文件和应用 / Remove invalid files and directories from the collection
    /// </summary>
    /// <param name="index">搜索索引 / Search index</param>
    public void CleanupInvalidItems(SearchIndex index);

    /// <summary>
    /// 索引所有应用程序 / Index all applications
    /// </summary>
    /// <param name="index">搜索索引 / Search index</param>
    /// <param name="logging">是否记录日志 / Whether to enable logging</param>
    /// <param name="useEverything">是否使用Everything / Whether to use Everything search</param>
    public void IndexAllApps(SearchIndex index, bool logging,
        bool useEverything = false);

    /// <summary>
    /// 自动启动Everything / Auto start Everything search engine via scheduled task
    /// </summary>
    /// <param name="index">搜索索引 / Search index</param>
    /// <param name="onSuccess">启动成功的回调 / Callback action on success</param>
    public void AutoStartEverything(SearchIndex index, Action onSuccess);
    
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
    public void LoadIcon(Kitopia.Desktop.Features.CustomScenario.CustomScenario item);

    /// <summary>
    /// 获取文件路径对应的 Shell 图标 / Get shell icon for a file path
    /// </summary>
    /// <param name="filePath">文件路径（不需要存在，按扩展名匹配）</param>
    /// <param name="callback">图标加载完成的回调（UI 线程执行）</param>
    public void LoadIcon(string filePath, Action<Avalonia.Media.Imaging.Bitmap?> callback);

    /// <summary>
    /// 获取文件路径的图标 PNG 字节 / Get file icon as PNG bytes
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>PNG 字节数组，失败返回 null</returns>
    public byte[]? GetFileIconPng(string filePath);

    /// <summary>
    /// 获取文本拼音 / Get pinyin for input text
    /// </summary>
    /// <param name="input">输入文本 / Input text</param>
    /// <returns>拼音项 / Pinyin item</returns>
    public PinyinItem GetPinyin(string input);
}
