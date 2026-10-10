using Kitopia.Feature.Localization;
using System.Buffers;
using System.Net;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Kitopia.Desktop.Features.Services.Interfaces;
using Kitopia.Desktop.Features.CustomScenario.Services;
using Kitopia.Desktop.Features.Services.Plugin;
using Kitopia.Desktop.Features.Utils;
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
    public static int Port { get; private set; }
    public static TaskCompletionSource PluginsReady { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        
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
            .WithDefaultEndpoint()
            .WithDefaultEndpointBoundIPAddress(IPAddress.Loopback)
            .WithDefaultEndpointPort(nowPort).Build();
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


        Port = nowPort;
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
                        toast.Show(Lang.Get("lang.kitopia.url_operation_failed"),
                            Lang.Format("lang.kitopia.plugins.not_found", pluginSign));
                        break;
                    }

                    version ??= onlinePluginInfo.LastVersion;
                    if (string.IsNullOrWhiteSpace(version))
                    {
                        toast.Show(Lang.Get("lang.kitopia.url_operation_failed"), Lang.Format("lang.kitopia.plugins.no_installable_version", onlinePluginInfo.Name));
                        break;
                    }

                    var authorNameTask = PluginNetworkService.GetAuthorNameAsync(onlinePluginInfo.AuthorId);
                    var supportSystems = onlinePluginInfo.SupportSystems.Count > 0
                        ? string.Join(", ", onlinePluginInfo.SupportSystems.Select(system => system.ToLowerInvariant() switch
                        {
                            "windows" => "Windows",
                            "macos" => "macOS",
                            "linux" => "Linux",
                            _ => system
                        }))
                        : Lang.Get("lang.kitopia.unknown");
                    var authorName = await authorNameTask ?? Lang.Format("lang.kitopia.users.default_name", onlinePluginInfo.AuthorId);
                    var request = new ToastRequest
                    {
                        Header = Lang.Format("lang.kitopia.plugins.install_title", onlinePluginInfo.Name),
                        Text = Lang.Format("lang.kitopia.plugins.install_details", onlinePluginInfo.NameSign,
                            onlinePluginInfo.DescriptionShort ?? onlinePluginInfo.Description ?? Lang.Get("lang.kitopia.no_summary"),
                            version, supportSystems, authorName, onlinePluginInfo.DownloadCounts),
                        NotificationType = Avalonia.Controls.Notifications.NotificationType.Information,
                        AutoCloseDelay = null,
                        Actions =
                        [
                            new ToastAction
                            {
                                Text = Lang.Get("lang.kitopia.install"),
                                IsPrimary = true,
                                Callback = () => _ = InstallPluginFromUrlAsync(onlinePluginInfo, version)
                            },
                            new ToastAction { Text = Lang.Get("lang.kitopia.cancel") }
                        ]
                    };

                    await ShowPluginInstallDialogAsync(request, toast);
                }
                break;
            }
            case StartupAction.DownloadScenario:
            {
                if (!long.TryParse(value, out var scenarioId) || scenarioId <= 0) break;
                var version = jObject["version"]?.ToString();
                try
                {
                    var scenario = await ScenarioMarketService.GetScenarioAsync(scenarioId);
                    await ShowPluginInstallDialogAsync(new ToastRequest
                    {
                        Header = Lang.Format("lang.kitopia.scenarios.import_title", scenario.Name), Text = Lang.Format("lang.kitopia.messages.value_author_value_version_value", scenario.Description, scenario.Author, version ?? scenario.LastVersion ?? Lang.Get("lang.kitopia.latest_version")),
                        AutoCloseDelay = null, ShowCloseButton = true,
                        Actions = [new ToastAction { Text = Lang.Get("lang.kitopia.import"), IsPrimary = true, Callback = () => _ = ImportScenarioFromUrlAsync(scenario.Id, version) },
                            new ToastAction { Text = Lang.Get("lang.kitopia.cancel") }]
                    }, toast!);
                }
                catch (Exception exception)
                {
                    Logger.Error(exception, "无法获取市场情景 {ScenarioId}", scenarioId);
                    await toast!.Show(Lang.Get("lang.kitopia.scenario_import_failed"), exception.Message);
                }
                break;
            }
            case StartupAction.IndexAdd:
                if (!string.IsNullOrEmpty(value))
                {
                    searchFeature.AddToIndex(value);
                    toast.Show(Lang.Get("lang.kitopia.index_operation"), Lang.Format("lang.kitopia.indexing.added", value));
                }
                break;
            case StartupAction.IndexRemove:
                if (!string.IsNullOrEmpty(value))
                {
                    searchFeature.RemoveFromIndex(value);
                    toast.Show(Lang.Get("lang.kitopia.index_operation"), Lang.Format("lang.kitopia.indexing.removed", value));
                }
                break;
            case StartupAction.IndexCheck:
                if (!string.IsNullOrEmpty(value))
                {
                    var exists = searchFeature.IsIndexed(value);
                    toast.Show(Lang.Get("lang.kitopia.index_status"), Lang.Format(exists ? "lang.kitopia.indexing.present" : "lang.kitopia.indexing.missing", value));
                }
                break;
            case StartupAction.PinAdd:
                if (!string.IsNullOrEmpty(value))
                {
                    searchFeature.SetPinned(value, true);
                    toast.Show(Lang.Get("lang.kitopia.favorite_operation"), Lang.Format("lang.kitopia.favorites.added", value));
                }
                break;
            case StartupAction.PinRemove:
                if (!string.IsNullOrEmpty(value))
                {
                    searchFeature.SetPinned(value, false);
                    toast.Show(Lang.Get("lang.kitopia.favorite_operation"), Lang.Format("lang.kitopia.favorites.removed", value));
                }
                break;
            case StartupAction.PinCheck:
                if (!string.IsNullOrEmpty(value))
                {
                    var pinned = searchFeature.IsPinned(value);
                    toast.Show(Lang.Get("lang.kitopia.favorite_status"), Lang.Format(pinned ? "lang.kitopia.favorites.added" : "lang.kitopia.favorites.missing", value));
                }
                break;
            case StartupAction.PluginCheck:
                if (!string.IsNullOrEmpty(value))
                {
                    var info = PluginManager.GetPluginLocalInfoByPlgStr(value);
                    var installed = info != null;
                    toast.Show(Lang.Get("lang.kitopia.plugin_status"), Lang.Format(installed ? "lang.kitopia.plugins.installed" : "lang.kitopia.plugins.not_installed", value));
                }
                break;
            case StartupAction.PluginAdd:
                if (!string.IsNullOrEmpty(value))
                {
                    var onlineInfo = await PluginNetworkService.GetOnlinePluginInfo(value);
                    if (onlineInfo != null)
                    {
                        await PluginManager.DownloadPluginAndEnable(onlineInfo.NameSign);
                        toast.Show(Lang.Get("lang.kitopia.plugin_operation"), Lang.Format("lang.kitopia.plugins.install_succeeded", value));
                    }
                    else
                    {
                        toast.Show(Lang.Get("lang.kitopia.plugin_operation"), Lang.Format("lang.kitopia.plugins.not_found", value));
                    }
                }
                break;
            case StartupAction.PluginRemove:
                if (!string.IsNullOrEmpty(value))
                {
                    var pluginInfo = PluginManager.GetPluginLocalInfoByPlgStr(value);
                    var pluginDisplayName = pluginInfo != null ? pluginInfo.PluginBaseInfo.Name : value;
                    var request = new ToastRequest
                    {
                        Header = Lang.Get("lang.kitopia.plugins.uninstall_title"),
                        Text = Lang.Format("lang.kitopia.plugins.uninstall_request", pluginDisplayName, value),
                        NotificationType = Avalonia.Controls.Notifications.NotificationType.Warning,
                        AutoCloseDelay = null,
                        Actions =
                        [
                            new ToastAction
                            {
                                Text = Lang.Get("lang.kitopia.confirm_uninstall"),
                                IsPrimary = true,
                                Callback = () =>
                                {
                                    PluginManager.DeletePlugin(value);
                                    toast?.Show(Lang.Get("lang.kitopia.plugin_operation"), Lang.Format("lang.kitopia.plugins.uninstalled", pluginDisplayName));
                                }
                            },
                            new ToastAction { Text = Lang.Get("lang.kitopia.cancel") }
                        ]
                    };
                    await ShowPluginInstallDialogAsync(request, toast);
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
                    toast.Show(Lang.Get("lang.kitopia.lan_sharing"), Lang.Get("lang.kitopia.the_sharing_window_is_unavailable"));
                    break;
                }

                await Dispatcher.UIThread.InvokeAsync(() => lanFileShareWindow.Show(filePaths));

                if (filePaths.Count == 0)
                {
                    toast.Show(Lang.Get("lang.kitopia.lan_sharing"), Lang.Get("lang.kitopia.no_files_available_to_send_were_found"));
                }
            }
                break;
            case StartupAction.FileLocksmith:
            {
                var windowService = ServiceManager.Services.GetService<IFileLocksmithWindow>();
                if (windowService != null)
                {
                    var targetList = values.Count > 0
                        ? values.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim().Trim('"')).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                        : string.IsNullOrWhiteSpace(value) ? [] : [value.Trim().Trim('"')];

                    string? rootDir = null;
                    List<string>? targetFiles = null;

                    if (targetList.Count == 1 && Directory.Exists(targetList[0]))
                    {
                        rootDir = targetList[0];
                    }
                    else if (targetList.Count > 0)
                    {
                        targetFiles = targetList;
                    }

                    await Dispatcher.UIThread.InvokeAsync(() => windowService.ShowForScope(rootDir, targetFiles));
                }
                break;
            }
            case StartupAction.Login:
            {
                var code = jObject["code"]?.ToString();
                var state = jObject["state"]?.ToString();
                if (string.IsNullOrWhiteSpace(code))
                {
                    code = value;
                }

                var accountService = ServiceManager.Services.GetService<IAccountService>();
                if (accountService != null)
                {
                    if (!string.IsNullOrWhiteSpace(code))
                    {
                        await accountService.ExchangeCodeAndLoginAsync(code, state);
                    }
                    else
                    {
                        Logger.Warning("收到 Login 动作但未提供合法的 authorization_code，已拒绝");
                    }
                }

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
                        desktop.MainWindow != null)
                    {
                        desktop.MainWindow.Show();
                        desktop.MainWindow.WindowState = WindowState.Normal;
                        desktop.MainWindow.Activate();
                        var platformHandle = desktop.MainWindow.TryGetPlatformHandle();
                        if (platformHandle is not null)
                        {
                            ServiceManager.Services.GetService<IWindowTool>()?.SetForegroundWindow(platformHandle.Handle);
                        }
                    }
                });
                break;
            }
            case StartupAction.ImageCompression:
            {
                await PluginsReady.Task;
                await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    if (StartupArgumentManager.Handlers.TryGetValue(action, out var handler))
                        await handler(values);
                    else if (toast != null)
                        await toast.Show(Lang.Get("lang.kitopia.action_failed"),
                            Lang.Format("lang.kitopia.plugins.not_installed", "KitopiaEx"));
                });
                break;
            }
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

    private static async Task ImportScenarioFromUrlAsync(long id, string? version)
    {
        var toast = ServiceManager.Services.GetRequiredService<IToastService>();
        try
        {
            var imported = await ScenarioMarketService.ImportAsync(id, version: version);
            await toast.Show(Lang.Get("lang.kitopia.scenario_imported"), imported.HasInit ? imported.Name : $"{imported.Name}：{imported.InitError}");
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "从 URL 导入情景失败 {ScenarioId}", id);
            await toast.Show(Lang.Get("lang.kitopia.scenario_import_failed"), exception.Message);
        }
    }

    private static async Task InstallPluginFromUrlAsync(OnlinePluginInfo plugin, string version)
    {
        var toast = ServiceManager.Services.GetRequiredService<IToastService>();
        var installed = await PluginManager.DownloadPluginAndEnable(plugin.NameSign, version);
        await toast.Show(Lang.Get("lang.kitopia.url_operation"), Lang.Format(installed
            ? "lang.kitopia.plugins.install_succeeded"
            : "lang.kitopia.plugins.install_failed", plugin.Name));
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
        if (arg.ApplicationMessage.Topic != "test") return;
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
