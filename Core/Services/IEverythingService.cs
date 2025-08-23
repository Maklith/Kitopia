namespace Core.Services;

/// <summary>
/// Everything 搜索服务接口 / Everything search service interface for file indexing
/// </summary>
public interface IEverythingService
{
    /// <summary>
    /// 检查 Everything 是否正在运行 / Check if Everything search engine is running
    /// </summary>
    /// <returns>是否运行中 / Whether Everything is running</returns>
    public bool IsRun();
}