using System.Buffers;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Kitopia.Desktop.Features.Services.Interfaces;
using Kitopia.Desktop.Features.Services.Plugin;
using Kitopia.Desktop.Features.Utils;
using Kitopia.Desktop.Abstractions.FileSystem;
using Microsoft.Extensions.DependencyInjection;
using MQTTnet;
using MQTTnet.Protocol;
using MQTTnet.Server;
using Newtonsoft.Json.Linq;
using PluginCore;
using Serilog;

namespace Kitopia.Desktop.Features.Services.MQTT;


/// <summary>
/// MQTT管理器，负责MQTT服务器的初始化和消息处理
/// MQTT manager responsible for MQTT server initialization and message handling
/// </summary>
public class MqttManager
{
    private static ILogger Logger = LogManager.Logger.ForContext<MqttManager>();
        
    /// <summary>
    /// MQTT服务器实例 / MQTT server instance
    /// </summary>
    public static MqttServer Server;
        
    private static FileStream fileStream;
    
    /// <summary>
    /// 初始化MQTT服务器
    /// Initialize MQTT server
    /// </summary>
    /// <returns>异步任务 / Asynchronous task. Returns true if startup args were sent to another instance.</returns>
    public static async Task<bool> Init(string[] args)
    {
        var mqttClientFactoryFactory = new MqttClientFactory();
        var portFilePath = KitopiaPaths.PortFilePath;
        if (File.Exists(portFilePath))
            try
            {
                File.Delete(portFilePath);
            }
            catch (Exception e)
            {
                using (var fs = new FileStream(portFilePath, FileMode.Open,
                           FileAccess.Read, FileShare.ReadWrite))
                {
                    var bt = new byte[fs.Length];
                    fs.Read(bt, 0, bt.Length);
                    fs.Close();
                    var i = int.Parse(Encoding.UTF8.GetString(bt));
                    var options = new MqttClientOptionsBuilder()
                        .WithTcpServer("localhost", i) // 指定MQTT代理服务器的地址和端口
                        .Build();
                    var mqttClient = mqttClientFactoryFactory.CreateMqttClient();
                    var mqttClientConnectResult = await mqttClient.ConnectAsync(options);
                    if (mqttClientConnectResult.ResultCode == MqttClientConnectResultCode.Success)
                    {
                        Logger.Debug("MQTT连接成功");
                        var result = StartupArgumentManager.Parse(args);
                        var jObject = BuildActionPayload(result);
                        jObject["type"] = (int)result.Action;

                        await mqttClient.PublishAsync(new MqttApplicationMessage
                        {
                            Topic = "test", Payload = new ReadOnlySequence<byte>(Encoding.UTF8.GetBytes(jObject.ToString())),
                            QualityOfServiceLevel = MqttQualityOfServiceLevel.ExactlyOnce
                        });
                        
                        // We handled the startup by sending to another instance
                        return true;
                    }
                }
            }

        var nowPort = 6600;
        restart:
        MqttServerFactory mqttServerFactory = new MqttServerFactory();
        var mqttServerOptions = mqttServerFactory.CreateServerOptionsBuilder()
            .WithDefaultEndpoint().WithDefaultEndpointPort(nowPort).Build();
        Server = mqttServerFactory.CreateMqttServer(mqttServerOptions);
        Server.ClientConnectedAsync += Server_ClientConnectedAsync;
        Server.ClientDisconnectedAsync += Server_ClientDisconnectedAsync;
        Server.InterceptingPublishAsync += Server_InterceptingPublishAsync;


        try
        {
            await Server.StartAsync();
        }
        catch (Exception e)
        {
            Server.ClientConnectedAsync -= Server_ClientConnectedAsync;
            Server.ClientDisconnectedAsync -= Server_ClientDisconnectedAsync;
            Server.InterceptingPublishAsync -= Server_InterceptingPublishAsync;
            nowPort++;
            Logger.Debug($"MQTT启动失败,尝试启动端口{nowPort}");
            goto restart;
        }


        fileStream = new FileStream(portFilePath, FileMode.CreateNew);
        fileStream.Write(Encoding.UTF8.GetBytes(nowPort.ToString()));
        fileStream.Flush();
        
        return false;
    }
    
    // Static method to handle local args if we are the server
    public static async Task ProcessLocalArgs(string[] args)
    {
        var result = StartupArgumentManager.Parse(args);
        if (result.Action == StartupAction.None || result.Action == StartupAction.RepeatStartup) return;

        await HandleAction(result.Action, BuildActionPayload(result));
    }

    private static async Task HandleAction(StartupAction action, JObject jObject)
    {
        var searchFeature = ServiceManager.Services.GetService<ISearchFeatureService>()!;
        var toast = ServiceManager.Services.GetService<IToastService>();
        var value = jObject["value"]?.ToString() ?? string.Empty;
        var values = ExtractActionValues(jObject, value);

        switch (action)
        {
            // ... same switch case as before ...
            case StartupAction.RepeatStartup:
            {
                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (Application.Current!.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                    {
                        if (desktop.MainWindow != null)
                        {
                            desktop.MainWindow.Show();
                            desktop.MainWindow.WindowState = WindowState.Normal;
                            ServiceManager.Services.GetService<IWindowTool>()
                                .SetForegroundWindow(desktop.MainWindow.TryGetPlatformHandle().Handle);
                        }
                    }
                });
                break;
            }
            // ... copy cases ...
            case StartupAction.DownloadPlugin:
            {
                var pluginSign = jObject["pluginSign"]?.ToString();
                var version = jObject["pluginVersion"]?.ToString();
                if (string.IsNullOrWhiteSpace(pluginSign))
                {
                    pluginSign = values.FirstOrDefault();
                    version ??= values.Skip(1).FirstOrDefault();
                }

                if (!string.IsNullOrWhiteSpace(pluginSign))
                {
                    var onlinePluginInfo = await PluginNetworkService.GetOnlinePluginInfo(pluginSign);
                    if (onlinePluginInfo == null)
                    {
                        toast.Show("来自URL的操作失败",
                            $"下载安装插件{pluginSign}不存在");
                        break;
                    }

                    version ??= onlinePluginInfo.LastVersion;
                    if (string.IsNullOrWhiteSpace(version))
                    {
                        toast.Show("来自URL的操作失败", $"插件 {onlinePluginInfo.Name} 没有可安装的版本");
                        break;
                    }

                    var authorNameTask = PluginNetworkService.GetAuthorNameAsync(onlinePluginInfo.AuthorId);
                    var supportSystems = onlinePluginInfo.SupportSystems.Count > 0
                        ? string.Join("、", onlinePluginInfo.SupportSystems.Select(system => system.ToLowerInvariant() switch
                        {
                            "windows" => "Windows",
                            "macos" => "macOS",
                            "linux" => "Linux",
                            _ => system
                        }))
                        : "未知";
                    var authorName = await authorNameTask ?? $"用户 {onlinePluginInfo.AuthorId}";
                    var request = new ToastRequest
                    {
                        Header = $"安装插件 · {onlinePluginInfo.Name}",
                        Text = $"{onlinePluginInfo.NameSign}\n\n" +
                               $"{onlinePluginInfo.DescriptionShort ?? onlinePluginInfo.Description ?? "暂无简介"}\n\n" +
                               $"版本：v{version}\n" +
                               $"支持系统：{supportSystems}\n" +
                               $"作者：{authorName}\n" +
                               $"{onlinePluginInfo.DownloadCounts} 下载" ,
                        NotificationType = Avalonia.Controls.Notifications.NotificationType.Information,
                        AutoCloseDelay = null,
                        Actions =
                        [
                            new ToastAction
                            {
                                Text = "安装",
                                IsPrimary = true,
                                Callback = () => _ = InstallPluginFromUrlAsync(onlinePluginInfo, version)
                            },
                            new ToastAction { Text = "取消" }
                        ]
                    };

                    await ShowPluginInstallDialogAsync(request, toast);
                }
                break;
            }
            case StartupAction.IndexAdd:
                if (!string.IsNullOrEmpty(value))
                {
                    searchFeature.AddToIndex(value);
                    toast.Show("索引操作", $"已添加到索引: {value}");
                }
                break;
            case StartupAction.IndexRemove:
                if (!string.IsNullOrEmpty(value))
                {
                    searchFeature.RemoveFromIndex(value);
                    toast.Show("索引操作", $"已从索引移除: {value}");
                }
                break;
            case StartupAction.IndexCheck:
                if (!string.IsNullOrEmpty(value))
                {
                    var exists = searchFeature.IsIndexed(value);
                    toast.Show("索引状态", exists ? $"已索引: {value}" : $"未索引: {value}");
                }
                break;
            case StartupAction.PinAdd:
                if (!string.IsNullOrEmpty(value))
                {
                    searchFeature.SetPinned(value, true);
                    toast.Show("收藏操作", $"已收藏: {value}");
                }
                break;
            case StartupAction.PinRemove:
                if (!string.IsNullOrEmpty(value))
                {
                    searchFeature.SetPinned(value, false);
                    toast.Show("收藏操作", $"已取消收藏: {value}");
                }
                break;
            case StartupAction.PinCheck:
                if (!string.IsNullOrEmpty(value))
                {
                    var pinned = searchFeature.IsPinned(value);
                    toast.Show("收藏状态", pinned ? $"已收藏: {value}" : $"未收藏: {value}");
                }
                break;
            case StartupAction.PluginCheck:
                if (!string.IsNullOrEmpty(value))
                {
                    var info = PluginManager.GetPluginLocalInfoByPlgStr(value);
                    var installed = info != null;
                    toast.Show("插件状态", installed ? $"已安装插件: {value}" : $"未安装插件: {value}");
                }
                break;
            case StartupAction.PluginAdd:
                if (!string.IsNullOrEmpty(value))
                {
                    var onlineInfo = await PluginNetworkService.GetOnlinePluginInfo(value);
                    if (onlineInfo != null)
                    {
                        await PluginManager.DownloadPluginAndEnable(onlineInfo.NameSign);
                        toast.Show("插件操作", $"插件安装/启用成功: {value}");
                    }
                    else
                    {
                        toast.Show("插件操作", $"找不到插件: {value}");
                    }
                }
                break;
            case StartupAction.PluginRemove:
                if (!string.IsNullOrEmpty(value))
                {
                    PluginManager.DeletePlugin(value);
                }
                break;
            case StartupAction.LanFileShare:
            {
                var filePaths = values
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Select(path => path.Trim().Trim('"'))
                    .Where(File.Exists)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var lanFileShareWindow = ServiceManager.Services.GetService<ILanFileShareWindow>();
                if (lanFileShareWindow == null)
                {
                    toast.Show("局域网分享", "分享窗口不可用。");
                    break;
                }

                await Dispatcher.UIThread.InvokeAsync(() => lanFileShareWindow.Show(filePaths));

                if (filePaths.Count == 0)
                {
                    toast.Show("局域网分享", "未识别到可发送文件。");
                }
            }
                break;
            case StartupAction.FileLocksmith:
                if (!string.IsNullOrEmpty(value))
                {
                    // Basic implementation: Show toast with locking processes?
                    // Or ideally open a window. Since we don't have a FileLocksmith Window yet,
                    // we will list processes in a toast or dialog for now as a proof of concept.
                    var service = ServiceManager.Services.GetService<IFileLockService>();
                    var windowService = ServiceManager.Services.GetService<IFileLocksmithWindow>();
                    if (service != null && windowService != null)
                    {
                        var lockingProcesses = await service.CheckFileLocksAsync(new[] { value });
                        if (lockingProcesses.Any())
                        {
                            await Dispatcher.UIThread.InvokeAsync(() => windowService.Show(lockingProcesses));
                        }
                        else
                        {
                            toast.Show("File Locksmith", $"未发现占用文件: {value}");
                        }
                    }
                }
                break;
        }
    }

    private static async Task ShowPluginInstallDialogAsync(ToastRequest request, IToastService toast)
    {
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop ||
                desktop.MainWindow is not { } mainWindow)
            {
                await toast.Show(request);
                return;
            }

            mainWindow.Show();
            mainWindow.WindowState = WindowState.Normal;
            mainWindow.Activate();
            var platformHandle = mainWindow.TryGetPlatformHandle();
            if (platformHandle is not null)
            {
                ServiceManager.Services.GetService<IWindowTool>()?.SetForegroundWindow(platformHandle.Handle);
            }

            await toast.Show(request, mainWindow);
        });
    }

    private static async Task InstallPluginFromUrlAsync(OnlinePluginInfo plugin, string version)
    {
        var toast = ServiceManager.Services.GetRequiredService<IToastService>();
        var installed = await PluginManager.DownloadPluginAndEnable(plugin.NameSign, version);
        await toast.Show("来自URL的操作", installed
            ? $"下载安装插件{plugin.Name}成功"
            : $"下载安装插件{plugin.Name}失败");
    }

    private static JObject BuildActionPayload(StartupResult result)
    {
        var payload = new JObject
        {
            ["value"] = result.Value ?? string.Empty
        };

        if (result.Values.Count > 0)
        {
            payload["values"] = JArray.FromObject(result.Values);
        }

        foreach (var kv in result.Extras)
        {
            payload[kv.Key] = kv.Value;
        }

        return payload;
    }

    private static IReadOnlyList<string> ExtractActionValues(JObject payload, string fallbackValue)
    {
        if (payload["values"] is JArray array)
        {
            var values = array
                .Values<string>()
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => path.Trim().Trim('"'))
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (values.Count > 0)
            {
                return values;
            }
        }

        return StartupArgumentManager.UnpackValues(fallbackValue)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path.Trim().Trim('"'))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static async Task Server_InterceptingPublishAsync(InterceptingPublishEventArgs arg)
    {
        var s = Encoding.UTF8.GetString(arg.ApplicationMessage.Payload);
        Logger.Debug($"Publish {arg.ApplicationMessage.Topic} {s}");
        try
        {
            var jObject = JObject.Parse(s);
            var jToken = jObject["type"];
            var action = jToken != null ? (StartupAction)jToken.ToObject<int>() : StartupAction.None;

            await HandleAction(action, jObject);
        }
        catch (Exception e)
        {
            Logger.Error( e,"来自URL的操作出现错误");
        }
    }

    private static async Task Server_ClientDisconnectedAsync(ClientDisconnectedEventArgs arg)
    {
        Logger.Debug($"Client {arg.ClientId} disconnected.");
    }

    private static async Task Server_ClientConnectedAsync(ClientConnectedEventArgs arg)
    {
        Logger.Debug($"Client {arg.ClientId} connected.");
    }
}
