using Kitopia.Feature.Localization;
using Avalonia.Controls.Notifications;
using Kitopia.Desktop.Features.Services;
using Kitopia.Desktop.Features.Services.Config;
using Kitopia.Desktop.Features.Services.Interfaces;
using Kitopia.Desktop.Features.Utils;
using Kitopia.Desktop.Abstractions.Shell;
using Kitopia.Feature.DeviceCommunication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using PluginCore;
using Serilog;

namespace Kitopia.Desktop.Platform.Windows;

public class ApplicationService : IApplicationService {
    private static readonly ILogger Logger = LogManager.Logger.ForContext<ApplicationService>();
    private static readonly HttpClient HttpClient = new();

    public void Init() {
        InitUrlProtocol();
    }

    public async Task RestartAsync() {
        ConfigManger.Save();
        ServiceManager.Services.GetService<IDesktopShell>()!.Open(
            ResolveExecutablePath(), "", AppDomain.CurrentDomain.BaseDirectory);
        await ExitAsync().ConfigureAwait(false);
    }

    public async Task StopAsync() {
        await ExitAsync().ConfigureAwait(false);
    }

    public async Task ExitAsync(int exitCode = 0) {
        ConfigManger.Save();
        ServiceManager.Services.GetService<ISelectionTranslationService>()?.Stop();
        var startupMessageBroker = ServiceManager.Services.GetService<IStartupMessageBroker>();
        if (startupMessageBroker is not null) {
            await startupMessageBroker.StopAsync().ConfigureAwait(false);
        }
        var deviceCommunication = ServiceManager.Services.GetService<IDeviceCommunicationRuntime>();
        if (deviceCommunication is not null) {
            await deviceCommunication.StopAsync().ConfigureAwait(false);
        }

        Logger.Information("程序退出");
        LogManager.Logger.Dispose();
        ServiceManager.Services.GetService<IToastService>()!.Unregister();
        Environment.Exit(exitCode);
    }

    public void InitUrlProtocol() {
        var protocolName = "kitopiaurl";

        try {
            // 创建或打开HKEY_CLASSES_ROOT下的URL Protocol键
            using (var key = Registry.CurrentUser.CreateSubKey("Software\\Classes\\" + protocolName)) {
                // 设置默认值为描述你的协议的字符串
                key.SetValue(null, "URL: Kitopia");
                key.SetValue("URL Protocol", "");

                // 创建一个子键用于处理打开协议的操作
                using (var commandKey = key.CreateSubKey("shell\\open\\command")) {
                    // 设置默认值为你的应用程序可执行文件的路径，包括 "%1" 用于参数
                    var appPath = $"\"{ResolveExecutablePath()}\" \"%1\"";
                    commandKey.SetValue(null, appPath);
                    commandKey.Flush();
                }

                key.Flush();
            }

            Logger.Debug("定义URL Protocol成功");
        }
        catch (Exception ex) {
            Logger.Error(ex, "定义URL Protocol失败");
        }
    }

    public bool ChangeAutoStart(bool autoStart) {
        try {
            if (autoStart) {
                var strName = ResolveExecutablePath(); //获取要自动运行的应用程序名
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
                else {
                    if (Equals(registry.GetValue("Kitopia"), $"\"{strName}\"")) {
                        Logger.Information("开机自启配置已存在");
                        return true;
                    }
                }

                Logger.Information("用户确认启用开机自启");
                try {
                    registry.SetValue("Kitopia", $"\"{strName}\""); //设置该子项的新的“键值对”

                    ServiceManager.Services.GetService<IToastService>()!.Show(Lang.Get("lang.kitopia.start_at_login"),
                        Lang.Get("lang.kitopia.startup_setting_updated"));
                }
                catch (Exception exception) {
                    Logger.Error(exception, "开机自启设置失败");
                    ServiceManager.Services.GetService<IToastService>()!.Show(Lang.Get("lang.kitopia.start_at_login"),
                        Lang.Get("lang.kitopia.startup_setting_failed"));
                    return false;
                }
            }
            else {
                try {
                    var registry =
                        Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run",
                            true); //检索指定的子项
                    registry?.DeleteValue("Kitopia");
                }
                catch (Exception) {
                    return false;
                }
            }
        }
        catch (Exception e) {
            Logger.Error(e, "开机自启设置失败");
            ServiceManager.Services.GetService<IToastService>()!.Show(Lang.Get("lang.kitopia.start_at_login"), Lang.Get("lang.kitopia.startup_setting_failed"));
            return false;
        }

        return true;
    }

    public async Task<bool> CheckUpdate(bool toastIfNoUpdate) {
        bool continueUpdate = true;
        var gitHubUpdateService = ServiceManager.Services.GetService<GitHubUpdateService>();
        var (hasUpdate, latestVersion, downloadUrl, releaseNotes) = await gitHubUpdateService!.CheckForUpdatesAsync();
        if (hasUpdate && !string.IsNullOrEmpty(downloadUrl)) {
            Logger.Information($"发现新版本:{latestVersion}");
            var dialog = new DialogContent {
                Title = Lang.Format("lang.kitopia.messages.kitopia_update_new_version_value", latestVersion),
                Content = Lang.Format("lang.kitopia.messages.new_version_value_is_available_download_now_release_notes_value", latestVersion, releaseNotes ?? "无更新说明"),
                PrimaryButtonText = Lang.Get("lang.kitopia.download_and_update"),
                SecondaryButtonText = Lang.Get("lang.kitopia.cancel"),
                PrimaryAction = async void () => {
                    IToastProgressHandle? progressToast = null;
                    try {
                        var toastService = ServiceManager.Services.GetService<IToastService>()!;
                        var tempPath = Path.Combine(Path.GetTempPath(), $"Kitopia_{latestVersion}_Installer.exe");
                        
                        using var response =
                            await HttpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
                        response.EnsureSuccessStatusCode();

                        var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                        var canReportProgress = totalBytes != -1;
                        progressToast = toastService.ShowProgress("更新", "开始下载更新...", NotificationType.Information,
                            initialProgress: 0, isIndeterminate: !canReportProgress);

                        await using var contentStream = await response.Content.ReadAsStreamAsync();
                        var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None,
                            8192, true);

                        var buffer = new byte[8192];
                        long totalRead = 0;
                        int bytesRead;
                        var lastProgress = -1;

                        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0) {
                            await fileStream.WriteAsync(buffer, 0, bytesRead);
                            totalRead += bytesRead;

                            if (canReportProgress) {
                                var progress = (int)((double)totalRead / totalBytes * 100);
                                if (progress > lastProgress) {
                                    lastProgress = progress;
                                    progressToast.Update(progress, Lang.Format("lang.kitopia.messages.download_progress_value", progress));
                                }
                            }
                        }

                        await fileStream.DisposeAsync();
                        progressToast.Complete("下载完成，正在启动安装程序...");
                        await Task.Delay(1000);
                        // Close application and start installer
                        var installerArguments = ConfigManger.Config.createShortcutsOnUpdate
                            ? "--silent"
                            : "--silent --no-shortcuts";
                        ServiceManager.Services.GetService<IDesktopShell>()!.Open(tempPath, installerArguments);
                        await Task.Delay(2000);
                        await ExitAsync().ConfigureAwait(false);
                    }
                    catch (Exception ex) {
                        Logger.Error(ex, "更新失败");
                        if (progressToast is not null) {
                            progressToast.Fail(Lang.Format("lang.kitopia.messages.download_failed_value", ex.Message), "更新失败");
                        }
                        else {
                            _ =ServiceManager.Services.GetService<IToastService>()!
                                .Show(Lang.Get("lang.kitopia.update_failed"), Lang.Format("lang.kitopia.messages.download_failed_value", ex.Message), NotificationType.Error);
                        }
                    }
                },
                CloseAction = () => { continueUpdate = false; },
                SecondaryAction = () => { continueUpdate = false; }
            };
            await ServiceManager.Services.GetService<IToastService>()!.Show(dialog.ToToastRequest());
        }
        else {
            if (toastIfNoUpdate) {
                var toastService = ServiceManager.Services.GetService<IToastService>()!;
                await toastService.Show(Lang.Get("lang.kitopia.update"), Lang.Get("lang.kitopia.no_updates"));
            }
        }

        return continueUpdate;
    }

    private static string ResolveExecutablePath() {
        return string.IsNullOrWhiteSpace(Environment.ProcessPath)
            ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Kitopia.Desktop.exe")
            : Environment.ProcessPath;
    }
}
