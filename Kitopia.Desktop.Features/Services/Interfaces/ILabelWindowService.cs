namespace Kitopia.Desktop.Features.Services.Interfaces;

/// <summary>
/// 标签窗口服务接口 / Label window service interface for displaying notifications
/// </summary>
public interface ILabelWindowService
{
    /// <summary>
    /// 显示标签窗口 / Show the label window
    /// </summary>
    public void Show();
    
    /// <summary>
    /// 显示带内容的标签窗口 / Show the label window with content
    /// </summary>
    /// <param name="content">要显示的内容 / Content to display</param>
    public void Show(string content);
}