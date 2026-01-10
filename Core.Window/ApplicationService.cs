using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.Notifications;
using Core.SDKs.Services;
using Core.Services;
using Core.Services.Config;
using Core.Utils;
using KitopiaAvalonia.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using PluginCore;
using Serilog;

namespace Core.Window;

public class ApplicationService : IApplicationService
{
    
    private static ILogger Logger = LogManager.Logger.ForContext<ApplicationService>();
    public void Init()
    {
        InitUrlProtocol();
    }

    public void Restart()
    {
        ServiceManager.Services.GetService<IShellUtils>()!.Open(
            AppDomain.CurrentDomain.FriendlyName + ".exe", "",
            AppDomain.CurrentDomain.BaseDirectory);
        Environment.Exit(0);
    }

    public void Stop()
    {
        ConfigManger.Save();
        Environment.Exit(0);
    }

    public void InitUrlProtocol()
    {
        var protocolName = "kitopiaurl";

        try
        {
            // 创建或打开HKEY_CLASSES_ROOT下的URL Protocol键
            using (var key = Registry.CurrentUser.CreateSubKey("Software\\Classes\\" + protocolName))
            {
                // 设置默认值为描述你的协议的字符串
                key.SetValue(null, "URL: Kitopia");
                key.SetValue("URL Protocol", "");

                // 创建一个子键用于处理打开协议的操作
                using (var commandKey = key.CreateSubKey("shell\\open\\command"))
                {
                    // 设置默认值为你的应用程序可执行文件的路径，包括 "%1" 用于参数
                    var appPath = $"{AppDomain.CurrentDomain.BaseDirectory}KitopiaAvalonia.exe \"%1\"";
                    commandKey.SetValue(null, appPath);
                    commandKey.Flush();
                }

                key.Flush();
            }

            Logger.Debug("定义URL Protocol成功");
        }
        catch (Exception ex)
        {
            Logger.Error("定义URL Protocol失败", ex);
        }
    }

    public bool ChangeAutoStart(bool autoStart)
    {
        try
        {
            if (autoStart)
            {
                var strName = AppDomain.CurrentDomain.BaseDirectory + "KitopiaAvalonia.exe"; //获取要自动运行的应用程序名
                if (!File.Exists(strName)) //判断要自动运行的应用程序文件是否存在
                    return false;

                var registry =
                    Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run",
                        true); //检索指定的子项
                if (registry == null) //若指定的子项不存在
                {
                    registry = Registry.CurrentUser.CreateSubKey(
                        "Software\\Microsoft\\Windows\\CurrentVersion\\Run"); //则创建指定的子项
                }
                else
                {
                    if (Equals(registry.GetValue("Kitopia"), $"\"{strName}\""))
                    {
                        Logger.Information("开机自启配置已存在");
                        return true;
                    }
                }

                Logger.Information("用户确认启用开机自启");
                try
                {
                    registry.SetValue("Kitopia", $"\"{strName}\""); //设置该子项的新的“键值对”
                    ((IToastService)ServiceManager.Services.GetService(typeof(IToastService))).Show("开机自启",
                        "开机自启设置成功");
                }
                catch (Exception exception)
                {
                    Logger.Error("开机自启设置失败");
                    Logger.Error(exception.StackTrace);
                    ((IToastService)ServiceManager.Services.GetService(typeof(IToastService))).Show("开机自启",
                        "开机自启设置失败");
                    return false;
                }
            }
            else
            {
                try
                {
                    var registry =
                        Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run",
                            true); //检索指定的子项
                    registry?.DeleteValue("Kitopia");
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }
        catch (Exception e)
        {
            Logger.Error(e,"开机自启设置失败");
            ((IToastService)ServiceManager.Services.GetService(typeof(IToastService))).Show("开机自启",
                "开机自启设置失败");
            return false;
        }

        return true;
    }

    public async Task CheckUpdate()
    {
        var gitHubUpdateService = ServiceManager.Services.GetService<GitHubUpdateService>();
        var (hasUpdate, latestVersion, downloadUrl, releaseNotes) = await gitHubUpdateService!.CheckForUpdatesAsync();
        if (hasUpdate && !string.IsNullOrEmpty(downloadUrl))
        {
            Logger.Information($"发现新版本:{latestVersion}");
            var dialog = new DialogContent()
            {
                Title = $"Kitopia更新 - 发现新版本 {latestVersion}",
                Content = $"发现新版本 {latestVersion}，是否前往下载？\n\n更新内容:\n{releaseNotes ?? "无更新说明"}",
                PrimaryButtonText = "下载并更新",
                SecondaryButtonText = "取消",
                PrimaryAction = async () =>
                {
                    try
                    {
                        var toastService = ServiceManager.Services.GetService<IToastService>();
                        var tempPath = Path.Combine(Path.GetTempPath(), $"Kitopia_{latestVersion}_Installer.exe");
                        
                        using var client = new HttpClient();
                        using var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
                        response.EnsureSuccessStatusCode();

                        var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                        var canReportProgress = totalBytes != -1;

                        await using var contentStream = await response.Content.ReadAsStreamAsync();
                        var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

                        var buffer = new byte[8192];
                        long totalRead = 0;
                        int bytesRead;
                        var lastProgress = -1;

                        toastService!.Show("更新", "开始下载更新...");

                        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, bytesRead);
                            totalRead += bytesRead;

                            if (canReportProgress)
                            {
                                var progress = (int)((double)totalRead / totalBytes * 100);
                                if (progress > lastProgress && progress % 10 == 0) // Report every 10%
                                {
                                    lastProgress = progress;
                                    toastService.Show("更新", $"下载进度: {progress}%");
                                }
                            }
                        }
                        await fileStream.DisposeAsync();
                        toastService.Show("更新", "下载完成，正在启动安装程序...");
                        await Task.Delay(1000);
                        // Close application and start installer
                        ServiceManager.Services.GetService<ISearchItemTool>()!.OpenFile(tempPath);
                        await Task.Delay(2000);
                        Environment.Exit(0);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, "更新失败");
                        ServiceManager.Services.GetService<IToastService>()!.Show("更新失败", $"下载出错: {ex.Message}",  NotificationType.Error);
                    }
                }
            };
            await ((IContentDialog)ServiceManager.Services!.GetService(typeof(IContentDialog))!).ShowDialogAsync(null,
                dialog);
        }
        else
        {
            var toastService = ServiceManager.Services.GetService<IToastService>();
            toastService.Show("更新", "无更新", NotificationType.Information);
        }
    }
}