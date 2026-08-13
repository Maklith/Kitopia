using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using Kitopia.Desktop.Abstractions.FileSystem;
using Kitopia.Desktop.Features.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;

namespace Kitopia.Desktop.Features.Services.Plugin;

public static class KitopiaFeatures
{
    [Feature("search", "快速搜索", "搜索并启动应用、文件和已配置的快捷功能。", "搜索与窗口", 0xf4b8, 10)]
    private static Task OpenSearchWindowAsync()
    {
        var searchWindowService = ServiceManager.Services?.GetService<ISearchWindowService>();
        if (searchWindowService is null)
        {
            ShowUnavailable("搜索功能");
            return Task.CompletedTask;
        }

        searchWindowService.ShowOrHiddenSearchWindow();
        return Task.CompletedTask;
    }

    [Feature("index", "文件与应用索引", "管理本地应用和文件索引，并支持 Everything 扩展搜索。", "搜索与窗口", 0xf3ae, 20)]
    private static Task OpenIndexAsync()
    {
        return OpenSearchWindowAsync();
    }

    [Feature("window-switcher", "窗口切换", "在搜索窗口中按标题定位并切换已打开的窗口。", "搜索与窗口", 0xf60a, 30)]
    private static Task OpenWindowSwitcherAsync()
    {
        return OpenSearchWindowAsync();
    }

    [Feature("window-topmost", "窗口置顶", "选择一个窗口并切换其始终置顶状态。", "搜索与窗口", 0xf602, 40)]
    private static Task ExecuteWindowTopmostAsync()
    {
        var windowTool = ServiceManager.Services?.GetService<IWindowTool>();
        if (windowTool is null)
        {
            ShowUnavailable("窗口置顶");
            return Task.CompletedTask;
        }

        windowTool.SelectAndSetWindowTopMost();
        return Task.CompletedTask;
    }

    [Feature("mouse-quick", "鼠标快捷菜单", "打开由常用搜索项目组成的鼠标快捷菜单。", "搜索与窗口", 0xf4b8, 50)]
    private static Task OpenMouseQuickMenuAsync()
    {
        var mouseQuickWindowService = ServiceManager.Services?.GetService<IMouseQuickWindowService>();
        if (mouseQuickWindowService is null)
        {
            ShowUnavailable("鼠标快捷菜单");
            return Task.CompletedTask;
        }

        mouseQuickWindowService.Open();
        return Task.CompletedTask;
    }

    [Feature("screen-capture", "截图", "截取屏幕区域并使用内置标注、长截图和剪贴板功能。", "截图与图像", 0xf4b8, 100)]
    private static Task CaptureScreenAsync()
    {
        var screenCaptureWindow = ServiceManager.Services?.GetService<IScreenCaptureWindow>();
        if (screenCaptureWindow is null)
        {
            ShowUnavailable("截图功能");
            return Task.CompletedTask;
        }

        screenCaptureWindow.CaptureScreen();
        return Task.CompletedTask;
    }

    [Feature("ocr", "文字识别", "选择屏幕区域，使用主程序的本地 PaddleOCR 识别文字并复制结果。", "截图与图像", 0xea72, 110,
        Activation = FeatureActivationMode.ScreenCapture)]
    private static async Task RecognizeScreenTextAsync(ScreenCaptureResult captureResult, CancellationToken cancellationToken)
    {
        if (captureResult.Source is null)
        {
            ShowToast("文字识别", "截图中没有可识别的图像数据。", NotificationType.Warning);
            return;
        }

        var ocr = ServiceManager.Services?.GetService<PluginCore.IOcrService>();
        if (ocr is null || !ocr.IsAvailable)
        {
            ShowToast("文字识别", "本地 OCR 模型不可用。", NotificationType.Warning);
            return;
        }

        var regions = await ocr.RecognizeAsync(captureResult.Source, cancellationToken);
        var text = string.Join(Environment.NewLine, regions.Select(region => region.Text));
        if (string.IsNullOrWhiteSpace(text))
        {
            ShowToast("文字识别", "未识别到文字。", NotificationType.Information);
            return;
        }

        ServiceManager.Services?.GetService<IClipboardService>()?.SetText(text);
        ShowToast("文字识别", text, NotificationType.Information);
    }

    [Feature("file-locksmith", "文件占用解锁", "选择文件后检查占用它的进程，并在需要时结束相关进程。", "文件与设备", 0xe61c, 200)]
    private static async Task CheckFileLocksAsync(CancellationToken cancellationToken)
    {
        var services = ServiceManager.Services;
        var filePicker = services?.GetService<IFeatureFilePicker>();
        var fileLockService = services?.GetService<IFileLockService>();
        var fileLocksmithWindow = services?.GetService<IFileLocksmithWindow>();
        if (filePicker is null || fileLockService is null || fileLocksmithWindow is null)
        {
            ShowUnavailable("文件占用解锁");
            return;
        }

        var filePaths = await filePicker.PickFilesAsync("选择要检查占用的文件", true, cancellationToken);
        if (filePaths.Count == 0)
        {
            return;
        }

        var lockingProcesses = await fileLockService.CheckFileLocksAsync(filePaths.ToArray(), cancellationToken);
        if (lockingProcesses.Count == 0)
        {
            ShowToast("文件占用解锁", "未发现占用所选文件的进程。", NotificationType.Information);
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(() => fileLocksmithWindow.Show(lockingProcesses));
    }

    [Feature("lan-file-share", "局域网分享", "选择文件并发送到局域网内已发现的设备。", "文件与设备", 0xe974, 210)]
    private static async Task ShareFilesAsync(CancellationToken cancellationToken)
    {
        var services = ServiceManager.Services;
        var filePicker = services?.GetService<IFeatureFilePicker>();
        var lanFileShareWindow = services?.GetService<ILanFileShareWindow>();
        if (filePicker is null || lanFileShareWindow is null)
        {
            ShowUnavailable("局域网分享");
            return;
        }

        var filePaths = await filePicker.PickFilesAsync("选择要分享的文件", true, cancellationToken);
        if (filePaths.Count == 0)
        {
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(() => lanFileShareWindow.Show(filePaths));
    }

    [Feature("device-chat", "设备聊天与文件发送", "向已发现设备发送文本、图片、文件和剪贴板内容。", "文件与设备", 0xe975, 220)]
    private static Task OpenDeviceChatAsync()
    {
        return NavigateAsync("device/chat");
    }

    [Feature("scenario", "自定义情景", "组合触发器和功能节点，创建可自动运行的情景。", "自动化与管理", 0xe065, 300)]
    private static Task OpenScenarioManagerAsync()
    {
        return NavigateAsync("scenario");
    }

    [Feature("hotkey", "快捷键管理", "查看、修改和启用 Kitopia 的全局快捷键。", "自动化与管理", 0xf4b9, 310)]
    private static Task OpenHotKeyManagerAsync()
    {
        return NavigateAsync("hotkey");
    }

    [Feature("market", "插件市场", "浏览并安装可扩展 Kitopia 功能的插件。", "自动化与管理", 0xf151, 320)]
    private static Task OpenPluginMarketAsync()
    {
        return NavigateAsync("market");
    }

    [Feature("plugin", "插件管理", "管理本地插件及其配置和启用状态。", "自动化与管理", 0xf60a, 330)]
    private static Task OpenPluginManagerAsync()
    {
        return NavigateAsync("plugin");
    }

    [Feature("onnx", "ONNX 模型管理", "查看和管理 OCR 等功能使用的 ONNX 模型。", "自动化与管理", 0xf83b, 340)]
    private static Task OpenOnnxModelManagerAsync()
    {
        return NavigateAsync("onnx/model-manager");
    }

    [Feature("index-status", "\u7d22\u5f15\u72b6\u6001", "\u67e5\u770b\u5e76\u91cd\u5efa\u62fc\u97f3\u3001\u6587\u672c\u4e0e\u56fe\u7247 sqlite-vec \u7d22\u5f15\u3002", "\u81ea\u52a8\u5316\u4e0e\u7ba1\u7406", 0xf105, 345)]
    private static Task OpenIndexStatusAsync()
    {
        return NavigateAsync("index/status");
    }

    [Feature("settings", "设置与更新", "调整主题、截图、搜索和系统集成设置，并检查更新。", "自动化与管理", 0xf6aa, 350)]
    private static Task OpenSettingsAsync()
    {
        return NavigateAsync("settings");
    }

    private static Task NavigateAsync(string route)
    {
        var navigationService = ServiceManager.Services?.GetService<INavigationService>();
        if (navigationService is null)
        {
            ShowUnavailable("页面导航");
            return Task.CompletedTask;
        }

        navigationService.Navigate(route);
        return Task.CompletedTask;
    }

    private static void ShowUnavailable(string featureName)
    {
        ShowToast(featureName, "当前平台暂不支持此功能。", NotificationType.Warning);
    }

    private static void ShowToast(string title, string message, NotificationType notificationType)
    {
        _ = ServiceManager.Services?.GetService<IToastService>()?.Show(title, message, notificationType);
    }
}
