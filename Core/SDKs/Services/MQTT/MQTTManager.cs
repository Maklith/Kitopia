using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using AvaloniaEdit.Utils;
using Core.SDKs.Services.Plugin;
using log4net;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;
using MQTTnet.Server;
using Newtonsoft.Json.Linq;
using PluginCore;

namespace Core.SDKs.Services.MQTT;

public enum MqttMsgType
{
    重复启动,
    下载指定插件
}

public class MqttManager
{
    private static readonly ILog Log = LogManager.GetLogger(nameof(MqttManager));
    public static MqttServer Server;
    private static FileStream fileStream;

    public static async Task Init()
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
                    var mqttClientConnectResult = mqttClient.ConnectAsync(options).Result;
                    if (mqttClientConnectResult.ResultCode == MqttClientConnectResultCode.Success)
                    {
                        Log.Debug("MQTT连接成功");
                        var jObject = new JObject();
                        if (Application.Current.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime
                            appLifetime)
                        {
                            if (appLifetime.Args is null)
                            {
                                jObject.Add("type", (int)MqttMsgType.重复启动);
                            }
                            else
                            {
                                var replace = appLifetime.Args[0].Replace("kitopiaurl://", "").TrimEnd('/');
                                jObject.Add("data", replace);
                                var strings = replace.Split(";");
                                for (var i1 = 0; i1 < strings.Length; i1++)
                                {
                                    var s = strings[i1].Split("=");
                                    if (s.Length == 2) jObject.Add(s[0], s[1]);
                                }
                            }
                        }

                        mqttClient.PublishAsync(new MqttApplicationMessage
                        {
                            Topic = "test", Payload = Encoding.UTF8.GetBytes(jObject.ToString()),
                            QualityOfServiceLevel = MqttQualityOfServiceLevel.ExactlyOnce
                        });
                    }

                    ServiceManager.Services.GetService<IApplicationService>().Stop();
                    return;
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
            Log.Debug($"MQTT启动失败,尝试启动端口{nowPort}");
            goto restart;
        }


        fileStream = new FileStream($"{AppDomain.CurrentDomain.BaseDirectory}.port", FileMode.CreateNew);
        fileStream.Write(Encoding.UTF8.GetBytes(nowPort.ToString()));
        fileStream.Flush();
    }

    private static async Task Server_InterceptingPublishAsync(InterceptingPublishEventArgs arg)
    {
        var s = Encoding.UTF8.GetString(arg.ApplicationMessage.Payload);
        Log.Debug($"Publish {arg.ApplicationMessage.Topic} {s}");
        try
        {
            var jObject = JObject.Parse(s);
            var jToken = jObject["type"];
            var o = jToken.ToObject<int>();
            switch ((MqttMsgType)o)
            {
                case MqttMsgType.重复启动:
                {
                    Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (Application.Current!.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                        {
                            desktop.MainWindow!.Show();
                            desktop.MainWindow.WindowState = WindowState.Normal;
                            ServiceManager.Services.GetService<IWindowTool>()
                                .SetForegroundWindow(desktop.MainWindow.TryGetPlatformHandle().Handle);
                        }
                    });

                    break;
                }
                case MqttMsgType.下载指定插件:
                {
                    //0 : pluginId
                    //1 : pluginVersionInt

                    var onlinePluginInfo =
                        await PluginManager.GetOnlinePluginInfo(int.Parse(jObject["pluginId"].ToString()));
                    if (onlinePluginInfo == null)
                    {
                        ServiceManager.Services.GetService<IToastService>().Show("来自URL的操作失败",
                            $"下载安装插件ID:{jObject["pluginVersionInt"]}不存在");
                        break;
                    }

                    PluginManager.DownloadPluginOnline(onlinePluginInfo,
                        int.Parse(jObject["pluginVersionInt"].ToString()));
                    ServiceManager.Services.GetService<IToastService>().Show("来自URL的操作",
                        $"下载安装插件{onlinePluginInfo.Name}ID:{jObject["pluginVersionInt"]}成功");
                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
        catch (Exception e)
        {
            Log.Error("来自URL的操作出现错误", e);
        }
    }

    private static async Task Server_ClientDisconnectedAsync(ClientDisconnectedEventArgs arg)
    {
        Log.Debug($"Client {arg.ClientId} disconnected.");
    }

    private static async Task Server_ClientConnectedAsync(ClientConnectedEventArgs arg)
    {
        Log.Debug($"Client {arg.ClientId} connected.");
    }
}