using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using CommunityToolkit.Mvvm.Messaging;
using Core.Services;
using Core.Services.Config;
using Core.Services.HotKey;
using Core.Services.Interfaces;
using Core.Services.Plugin;
using Core.Utils;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using PluginCore.CustomScenario;
using Serilog;

namespace Core.CustomScenario;

public class CustomScenarioManger
{
    public static ObservableCollection<CustomScenario> CustomScenarios = new();
    private static ILogger Logger = LogManager.Logger.ForContext<CustomScenarioManger>();


    public static void Init()
    {
        WeakReferenceMessenger.Default.Register<string, string>("null", "CustomScenarioTrigger", (_, e) =>
        {
            //设置当前线程最高优先级
            Thread.CurrentThread.Priority = ThreadPriority.Highest;
            StringBuilder sb = new();

            foreach (var customScenario in CustomScenarios)
                if (customScenario.AutoTriggers.Contains(e))
                {
                    sb.AppendLine(customScenario.Name);
                    if (e == "Kitopia_SoftwareShutdown")
                        ThreadPool.QueueUserWorkItem(o => { customScenario.Run(onExit: true); });
                    else
                        customScenario.Run();
                }

            if (sb.Length != 0)
            {
                sb.Insert(0, $"情景触发器\"{e}\"触发了以下情景:\n");
                Logger.Information(sb.ToString());
                ((IToastService)ServiceManager.Services.GetService(typeof(IToastService))!).Show("情景",
                    sb.ToString());
            }
            else
            {
                Logger.Information($"情景触发器\"{e}\"没有触发情景");
            }
        });


        LoadAll();
        WeakReferenceMessenger.Default.Send("Kitopia_SoftwareStarted", "CustomScenarioTrigger");
    }

    private static void LoadAll()
    {
        if (!Directory.Exists($"{AppDomain.CurrentDomain.BaseDirectory}customScenarios"))
            Directory.CreateDirectory($"{AppDomain.CurrentDomain.BaseDirectory}customScenarios");

        var info = new DirectoryInfo($"{AppDomain.CurrentDomain.BaseDirectory}customScenarios");
        foreach (var fileInfo in info.GetFiles())
            if (fileInfo.Extension == ".json")
                Load(fileInfo);

        Logger.Debug($"加载情景信息完成共{CustomScenarios.Count}情景被识别");
    }

    public static void UnloadAll()
    {
        foreach (var customScenario in CustomScenarios) customScenario.UnRegisterHotKey();

        CustomScenarios.Clear();
    }

    public static void Reload()
    {
        UnloadAll();
        LoadAll();
    }

    public static void Load(FileInfo fileInfo)
    {
        var fileInfoName = fileInfo.Name.Replace(".json", "");
        if (CustomScenarios.Any(e => e.UUID == fileInfoName)) return;

        var json = File.ReadAllText(fileInfo.FullName);
        try
        {
            var deserializeObject = JsonSerializer.Deserialize<CustomScenario>(json, ConfigManger.DefaultOptions);

            deserializeObject.OnDeserialized();


            deserializeObject.HasInit = true;
            deserializeObject.IsRunning = false;


            foreach (var deserializeObjectNode in deserializeObject.nodes) deserializeObjectNode.ConnectorInit();
            deserializeObject.InitHotKey();
            CustomScenarios.Add(deserializeObject);
        }
        catch (CustomScenarioLoadFromJsonException e1)
        {
            var Name = string.Empty;
            Logger.Error($"情景文件\"{fileInfo.FullName}\"加载失败,内部异常");
            var utf8JsonReader = new Utf8JsonReader(File.ReadAllBytes(fileInfo.FullName));
            utf8JsonReader.Read();
            while (utf8JsonReader.Read())
            {
                if (utf8JsonReader.TokenType == JsonTokenType.StartObject) utf8JsonReader.Skip();

                if (utf8JsonReader.TokenType == JsonTokenType.PropertyName)
                    if (utf8JsonReader.GetString() == nameof(CustomScenario.Name))
                    {
                        utf8JsonReader.Read();
                        Name = utf8JsonReader.GetString();
                        break;
                    }
            }

            switch (e1.FailedType)
            {
                case CustomScenarioLoadFromJsonFailedType.插件未找到:
                {
                    var content = $"对应文件\n{fileInfo.FullName}\n情景所需的插件不存在\n需要插件\"{e1.PluginName}\"";
                    var dialog = new DialogContent
                    {
                        Title = $"自定义情景\"{Name}\"加载失败",
                        Content = content,
                        PrimaryButtonText = "尝试在市场中自动安装",
                        CloseButtonText = "我知道了",
                        PrimaryAction = async () =>
                        {
                            var onlinePluginInfo =
                                await PluginNetworkService.GetOnlinePluginInfo(int.Parse(e1.PluginName.Split("_")[0]));
                            if (onlinePluginInfo is null)
                            {
                                ServiceManager.Services.GetService<IToastService>().Show("自动下载插件失败",
                                    $"未找到ID:{e1.PluginName.Split("_")[0]}的插件");
                                return;
                            }

                            var downloadPluginOnline = await PluginManager.DownloadPluginAndEnable(onlinePluginInfo.Id,
                                onlinePluginInfo.NameSign, onlinePluginInfo.LastVersionId);

                            if (downloadPluginOnline)
                                ServiceManager.Services.GetService<IToastService>()
                                    .Show("自动下载插件成功", $"已自动下载并启用{onlinePluginInfo.Name}");
                            else
                                ServiceManager.Services.GetService<IToastService>().Show("自动下载插件失败",
                                    $"下载ID:{e1.PluginName.Split("_")[0]}的插件时遇到错误");
                        }
                    };
                    ((IContentDialog)ServiceManager.Services!.GetService(typeof(IContentDialog))!).ShowDialogAsync(null,
                        dialog);
                    break;
                }
                case CustomScenarioLoadFromJsonFailedType.插件未启用:
                {
                    var pluginByPlgStr = PluginManager.GetPluginLocalInfoByPlgStr(e1.PluginName);

                    var content =
                        $"对应文件\n{fileInfo.FullName}\n情景所需的插件未启用\n需要插件{pluginByPlgStr.PluginBaseInfo.Name}(ID:{pluginByPlgStr.PluginBaseInfo.Id})";

                    var dialog = new DialogContent
                    {
                        Title = $"自定义情景\"{Name}\"加载失败",
                        Content = content,
                        PrimaryButtonText = "启用该插件",
                        CloseButtonText = "我知道了",
                        PrimaryAction = () => { PluginManager.EnablePlugin(pluginByPlgStr); }
                    };
                    ((IContentDialog)ServiceManager.Services!.GetService(typeof(IContentDialog))!).ShowDialogAsync(null,
                        dialog);
                    break;
                }
                case CustomScenarioLoadFromJsonFailedType.方法未找到:
                {
                    break;
                }
                case CustomScenarioLoadFromJsonFailedType.类未找到:
                {
                    break;
                }
                case CustomScenarioLoadFromJsonFailedType.类的序列化转换器未找到:
                {
                    var content = $"对应文件\n{fileInfo.FullName}\n情景所需{e1.PluginName}类的序列化转换器未找到\n它可能来自某个插件";

                    var dialog = new DialogContent
                    {
                        Title = $"自定义情景\"{Name}\"加载失败",
                        Content = content,
                        CloseButtonText = "我知道了"
                    };
                    ((IContentDialog)ServiceManager.Services!.GetService(typeof(IContentDialog))!).ShowDialogAsync(null,
                        dialog);
                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
        catch (Exception e)
        {
            Logger.Error(e,"错误");
            var content = $"情景文件\n{fileInfo.FullName}\n加载失败疑似文件已损坏";
            var dialog = new DialogContent
            {
                Title = $"自定义情景\"{fileInfo.Name}\"加载失败",
                Content = content,
                CloseButtonText = "我知道了",
                PrimaryAction = () => { }
            };
            ((IContentDialog)ServiceManager.Services!.GetService(typeof(IContentDialog))!).ShowDialogAsync(null,
                dialog);
            CustomScenarios.Add(new CustomScenario
            {
                Name = fileInfo.Name,
                UUID = fileInfoName,
                IsRunning = false,
                HasInit = false,
                InitError = "加载失败疑似文件已损坏",
                IsActive = false
            });
        }
    }


    public static void Save(CustomScenario scenario)
    {
        if (!CustomScenarios.Contains(scenario))
        {
            CustomScenarios.Add(scenario);

            scenario.InitHotKey();
        }

        foreach (var tempValueKey in scenario.TempValue.Keys) scenario.TempValue[tempValueKey].Value = null;

        var configF = new FileInfo(AppDomain.CurrentDomain.BaseDirectory +
                                   $"customScenarios{Path.DirectorySeparatorChar}{scenario.UUID}.json");

        try
        {
            var j = JsonSerializer.Serialize(scenario, ConfigManger.DefaultOptions);
            File.WriteAllText(configF.FullName, j);
            scenario.NotifySaved();
        }

        catch (CustomScenarioLoadFromJsonException e)
        {
            Logger.Error("情景保存失败", e);
            switch (e.FailedType)
            {
                case CustomScenarioLoadFromJsonFailedType.类的序列化转换器未找到:
                {
                    var content = $"情景'{scenario.Name}'保存失败所需{e.PluginName}类的序列化转换器未找到\n它可能来自某个插件";

                    var dialog = new DialogContent
                    {
                        Title = $"自定义情景\"{scenario.Name}\"保存失败",
                        Content = content,
                        CloseButtonText = "我知道了"
                    };
                    ((IContentDialog)ServiceManager.Services!.GetService(typeof(IContentDialog))!).ShowDialogAsync(null,
                        dialog);
                    break;
                }
            }
        }
    }


    public static void Remove(CustomScenario scenario, bool deleteFile = true)
    {
        HotKeyManager.HotKetImpl.DeleteCompletely(scenario.RunHotKey.UUID);
        HotKeyManager.HotKetImpl.DeleteCompletely(scenario.StopHotKey.UUID);


        scenario.Dispose();
        if (CustomScenarios.Contains(scenario)) CustomScenarios.Remove(scenario);
        ConfigManger.Save();
        if (deleteFile)
            File.Delete(
                $"{AppDomain.CurrentDomain.BaseDirectory}customScenarios{Path.DirectorySeparatorChar}{scenario.UUID}.json");
    }

    public static void UnloadWhichUseThePlugin(string plugStr)
    {
        for (var i = CustomScenarios.Count - 1; i >= 0; i--)
            if (CustomScenarios[i].IsUseThePlugin(plugStr))
            {
                var customScenario = CustomScenarios[i];
                CustomScenarios.RemoveAt(i);
                Remove(customScenario, false);
                customScenario = null;
            }
    }

    public static void Reload(CustomScenario scenario)
    {
        Remove(scenario, false);
        var configF = new FileInfo(
            $"{AppDomain.CurrentDomain.BaseDirectory}customScenarios{Path.DirectorySeparatorChar}{scenario.UUID}.json");
        if (configF.Exists) Load(configF);
    }

    public static void ReCheck(bool onlyError = true)
    {
        var toRemove = new List<CustomScenario>();
        if (onlyError)
            foreach (var customScenario in CustomScenarios.Where(e => !e.HasInit))
                toRemove.Add(customScenario);
        else
            foreach (var customScenario in CustomScenarios)
                toRemove.Add(customScenario);

        foreach (var customScenario in toRemove)
        {
            if (customScenario.IsRunning) customScenario.Stop();

            Remove(customScenario, false);
            var configF = new FileInfo(
                $"{AppDomain.CurrentDomain.BaseDirectory}customScenarios{Path.DirectorySeparatorChar}{customScenario.UUID}.json");
            if (configF.Exists) Load(configF);
        }

        LoadAll();
    }
}