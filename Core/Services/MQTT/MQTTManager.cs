using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using AvaloniaEdit.Utils;
using Core.Services.Interfaces;
using Core.Services.Plugin;
using Core.ViewModel.Windows;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;
using MQTTnet.Server;
using Newtonsoft.Json.Linq;
using PluginCore;
using Serilog;

namespace Core.Services.MQTT;


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
        var mqttFactory = new MqttFactory();
        if (File.Exists($"{AppDomain.CurrentDomain.BaseDirectory}.port"))
            try
            {
                File.Delete($"{AppDomain.CurrentDomain.BaseDirectory}.port");
            }
            catch (Exception e)
            {
                using (var fs = new FileStream($"{AppDomain.CurrentDomain.BaseDirectory}.port", FileMode.Open,
                           FileAccess.Read, FileShare.ReadWrite))
                {
                    var bt = new byte[fs.Length];
                    fs.Read(bt, 0, bt.Length);
                    fs.Close();
                    var i = int.Parse(Encoding.UTF8.GetString(bt));
                    var options = new MqttClientOptionsBuilder()
                        .WithTcpServer("localhost", i) // 指定MQTT代理服务器的地址和端口
                        .Build();
                    var mqttClient = mqttFactory.CreateMqttClient();
                    var mqttClientConnectResult = await mqttClient.ConnectAsync(options);
                    if (mqttClientConnectResult.ResultCode == MqttClientConnectResultCode.Success)
                    {
                        Logger.Debug("MQTT连接成功");
                        var jObject = new JObject();
                        // Use provided args instead of ApplicationLifetime
                        var result = StartupArgumentManager.Parse(args);
                        jObject.Add("type", (int)result.Action);
                        jObject.Add("value", result.Value);
                        
                        foreach (var kv in result.Extras)
                        {
                            jObject[kv.Key] = kv.Value;
                        }

                        await mqttClient.PublishAsync(new MqttApplicationMessage
                        {
                            Topic = "test", Payload = Encoding.UTF8.GetBytes(jObject.ToString()),
                            QualityOfServiceLevel = MqttQualityOfServiceLevel.ExactlyOnce
                        });
                        
                        // We handled the startup by sending to another instance
                        return true;
                    }
                }
            }

        var nowPort = 6600;
        restart:
        var mqttServerOptions = mqttFactory.CreateServerOptionsBuilder()
            .WithDefaultEndpoint().WithDefaultEndpointPort(nowPort).Build();
        Server = mqttFactory.CreateMqttServer(mqttServerOptions);
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


        fileStream = new FileStream($"{AppDomain.CurrentDomain.BaseDirectory}.port", FileMode.CreateNew);
        fileStream.Write(Encoding.UTF8.GetBytes(nowPort.ToString()));
        fileStream.Flush();
        
        return false;
    }
    
    // Static method to handle local args if we are the server
    public static async Task ProcessLocalArgs(string[] args)
    {
         var result = StartupArgumentManager.Parse(args);
         if (result.Action == StartupAction.None || result.Action == StartupAction.RepeatStartup) return;
         
         // Mock an InterceptingPublishEventArgs or just reuse the logic?
         // Refactor logic into a shared method would be better.
         await HandleAction(result.Action, result.Value, JObject.FromObject(new { 
             pluginId = result.Extras.GetValueOrDefault("pluginId"), 
             pluginVersionInt = result.Extras.GetValueOrDefault("pluginVersionInt")
         }));
    }

    private static async Task HandleAction(StartupAction action, string value, JObject jObject)
    {
            var searchWindow = ServiceManager.Services.GetService<SearchWindowViewModel>();
            var toast = ServiceManager.Services.GetService<IToastService>();

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
                    if (jObject["pluginId"] != null)
                    {
                         var onlinePluginInfo =
                            await PluginNetworkService.GetOnlinePluginInfo(int.Parse(jObject["pluginId"].ToString()));
                        if (onlinePluginInfo == null)
                        {
                            toast.Show("来自URL的操作失败",
                                $"下载安装插件ID:{jObject["pluginVersionInt"]}不存在");
                            break;
                        }
    
                        PluginManager.DownloadPluginAndEnable(onlinePluginInfo.Id, onlinePluginInfo.NameSign,
                            int.Parse(jObject["pluginVersionInt"].ToString()));
                        toast.Show("来自URL的操作",
                            $"下载安装插件{onlinePluginInfo.Name}ID:{jObject["pluginVersionInt"]}成功");
                    }
                    break;
                }
                case StartupAction.IndexAdd:
                    if (!string.IsNullOrEmpty(value))
                    {
                         searchWindow.AddToIndex(value);
                         toast.Show("索引操作", $"已添加到索引: {value}");
                    }
                    break;
                case StartupAction.IndexRemove:
                    if (!string.IsNullOrEmpty(value))
                    {
                         searchWindow.RemoveFromIndex(value);
                         toast.Show("索引操作", $"已从索引移除: {value}");
                    }
                    break;
                case StartupAction.IndexCheck:
                    if (!string.IsNullOrEmpty(value))
                    {
                         var exists = searchWindow.IsIndexed(value);
                         toast.Show("索引状态", exists ? $"已索引: {value}" : $"未索引: {value}");
                    }
                    break;
                case StartupAction.PinAdd:
                     if (!string.IsNullOrEmpty(value))
                    {
                         searchWindow.SetPinned(value, true);
                         toast.Show("收藏操作", $"已收藏: {value}");
                    }
                    break;
                case StartupAction.PinRemove:
                     if (!string.IsNullOrEmpty(value))
                    {
                         searchWindow.SetPinned(value, false);
                         toast.Show("收藏操作", $"已取消收藏: {value}");
                    }
                    break;
                case StartupAction.PinCheck:
                     if (!string.IsNullOrEmpty(value))
                    {
                         var pinned = searchWindow.IsPinned(value);
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
                             await PluginManager.DownloadPluginAndEnable(onlineInfo.Id, onlineInfo.NameSign);
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
                case StartupAction.FileLocksmith:
                     if (!string.IsNullOrEmpty(value))
                    {
                         // Basic implementation: Show toast with locking processes?
                         // Or ideally open a window. Since we don't have a FileLocksmith Window yet,
                         // we will list processes in a toast or dialog for now as a proof of concept.
                         var service = ServiceManager.Services.GetService<IFileLocksmith>();
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
                default:
                     break;
            }
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
            var value = jObject["value"]?.ToString();
            
            await HandleAction(action, value, jObject);
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