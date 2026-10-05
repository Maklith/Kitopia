using Kitopia.Feature.Localization;
using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using CommunityToolkit.Mvvm.Messaging;
using Kitopia.Desktop.Features.Services;
using Kitopia.Desktop.Features.Services.Config;
using Kitopia.Desktop.Features.Services.Interfaces;
using Kitopia.Desktop.Features.Utils;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using PluginCore.CustomScenario;
using Serilog;

namespace Kitopia.Desktop.Features.CustomScenario;

public class CustomScenarioManger
{
    public static ObservableCollection<CustomScenario> CustomScenarios = new();
    private static ILogger Logger = LogManager.Logger.ForContext<CustomScenarioManger>();


    public static void Init()
    {
        WeakReferenceMessenger.Default.Unregister<string, string>("null", "CustomScenarioTrigger");
        WeakReferenceMessenger.Default.Register<string, string>("null", "CustomScenarioTrigger",
            (_, name) => RunTrigger(ResolveTriggerKey(name)));
        WeakReferenceMessenger.Default.Unregister<Type, string>("null", "CustomScenarioTrigger");
        WeakReferenceMessenger.Default.Register<Type, string>("null", "CustomScenarioTrigger",
            (_, type) => RunTrigger(ResolveTriggerKey(type)));

        LoadAll();
        WeakReferenceMessenger.Default.Send("Kitopia_SoftwareStarted", "CustomScenarioTrigger");
    }

    private static void RunTrigger(string? trigger)
    {
        if (trigger is null) return;
        //设置当前线程最高优先级
        Thread.CurrentThread.Priority = ThreadPriority.Highest;
        StringBuilder sb = new();

        foreach (var customScenario in CustomScenarios)
            if (customScenario.AutoTriggers.Contains(trigger))
            {
                sb.AppendLine(customScenario.Name);
                if (trigger == "Kitopia_SoftwareShutdown")
                    ThreadPool.QueueUserWorkItem(o => { customScenario.Run(onExit: true); });
                else
                    customScenario.Run();
            }

        if (sb.Length != 0)
        {
            var triggerName = CustomScenarioGlobe.Triggers.TryGetValue(trigger, out var info) ? Lang.Get(info.Name ?? trigger) : trigger;
            sb.Insert(0, Lang.Format("lang.kitopia.scenarios.triggered", triggerName));
            Logger.Information(sb.ToString());
            ((IToastService)ServiceManager.Services.GetService(typeof(IToastService))!).Show(Lang.Get("lang.kitopia.scenarios"),
                sb.ToString());
        }
        else
        {
            Logger.Information($"情景触发器\"{trigger}\"没有触发情景");
        }
    }

    internal static string? ResolveTriggerKey(string name)
    {
        if (CustomScenarioGlobe.Triggers.ContainsKey(name)) return name;
        var matches = CustomScenarioGlobe.Triggers.Keys
            .Where(key => key.EndsWith("_" + name, StringComparison.Ordinal))
            .Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    internal static string? ResolveTriggerKey(Type type) =>
        CustomScenarioGlobe.Triggers.FirstOrDefault(pair => pair.Value.TriggerType == type).Key;

    private static void LoadAll()
    {
        Directory.CreateDirectory(KitopiaPaths.CustomScenariosDirectory);

        var info = new DirectoryInfo(KitopiaPaths.CustomScenariosDirectory);
        foreach (var fileInfo in info.GetFiles())
            if (fileInfo.Extension == ".json")
                Load(fileInfo);

        Logger.Debug($"加载情景信息完成共{CustomScenarios.Count}情景被识别");
    }

    public static void UnloadAll()
    {
        UnloadAllAsync().GetAwaiter().GetResult();
    }

    public static async Task UnloadAllAsync()
    {
        foreach (var customScenario in CustomScenarios) {
            customScenario.UnRegisterHotKey();
            await customScenario.DisposeAsync();
        }

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
        if (CustomScenarios.Any(e => e.Uuid == fileInfoName)) return;

        string? json = null;
        try
        {
            json = File.ReadAllText(fileInfo.FullName);
            var deserializeObject = JsonSerializer.Deserialize<CustomScenario>(json, ConfigManger.DefaultOptions);

            deserializeObject.OnDeserialized();


            deserializeObject.IsRunning = false;


            foreach (var deserializeObjectNode in deserializeObject.Nodes) deserializeObjectNode.ConnectorInit();
            if (deserializeObject.VerifyGraph()) deserializeObject.InitHotKey();
            CustomScenarios.Add(deserializeObject);
        }
        catch (CustomScenarioLoadFromJsonException e1)
        {
            var name = fileInfoName;
            Logger.Error(e1, "情景文件 {Path} 加载失败", fileInfo.FullName);
            using (var document = JsonDocument.Parse(json!))
                if (document.RootElement.TryGetProperty(nameof(CustomScenario.Name), out var property))
                    name = property.GetString() ?? fileInfoName;

            switch (e1.FailedType)
            {
                case CustomScenarioLoadFromJsonFailedType.插件未找到:
                {
                    var content = Lang.Format("lang.kitopia.messages.file_value_required_plugin_value_is_missing", fileInfo.FullName, e1.PluginName);
                    var dialog = new DialogContent
                    {
                        Title = Lang.Format("lang.kitopia.messages.unable_to_load_custom_scenario_value", name),
                        Content = content,
                        PrimaryButtonText = Lang.Get("lang.kitopia.install_automatically_from_the_market"),
                        CloseButtonText = Lang.Get("lang.kitopia.got_it"),
                        PrimaryAction = async () =>
                        {
                            var pluginIntegration = ServiceManager.Services
                                .GetRequiredService<ICustomScenarioPluginIntegration>();
                            var onlinePluginInfo = await pluginIntegration
                                .GetOnlinePluginAsync(e1.PluginName);
                            if (onlinePluginInfo is null)
                            {
                                ServiceManager.Services.GetService<IToastService>().Show(Lang.Get("lang.kitopia.automatic_plugin_download_failed"),
                                    $"未找到插件:{e1.PluginName}");
                                return;
                            }

                            var downloadPluginOnline = await pluginIntegration.DownloadAndEnableAsync(
                                onlinePluginInfo.NameSign,
                                onlinePluginInfo.Version);

                            if (downloadPluginOnline)
                                ServiceManager.Services.GetService<IToastService>()
                                    .Show(Lang.Get("lang.kitopia.plugin_downloaded_automatically"), Lang.Format("lang.kitopia.messages.downloaded_and_enabled_value", onlinePluginInfo.Name));
                            else
                                ServiceManager.Services.GetService<IToastService>().Show(Lang.Get("lang.kitopia.automatic_plugin_download_failed"),
                                    Lang.Format("lang.kitopia.messages.unable_to_download_plugin_value", e1.PluginName));
                        }
                    };
                    ((IToastService)ServiceManager.Services.GetService(typeof(IToastService))!).Show(
                        dialog.ToToastRequest());
                    break;
                }
                case CustomScenarioLoadFromJsonFailedType.插件未启用:
                {
                    var pluginIntegration = ServiceManager.Services
                        .GetRequiredService<ICustomScenarioPluginIntegration>();
                    var pluginByPlgStr = pluginIntegration.GetInstalledPlugin(e1.PluginName);

                    if (pluginByPlgStr is null)
                    {
                        break;
                    }

                    var content =
                        Lang.Format("lang.kitopia.messages.file_value_required_plugin_value_is_disabled", fileInfo.FullName, pluginByPlgStr.Name);

                    var dialog = new DialogContent
                    {
                        Title = Lang.Format("lang.kitopia.messages.unable_to_load_custom_scenario_value", name),
                        Content = content,
                        PrimaryButtonText = Lang.Get("lang.kitopia.enable_this_plugin"),
                        CloseButtonText = Lang.Get("lang.kitopia.got_it"),
                        PrimaryAction = async () => { await pluginIntegration.EnablePluginAsync(e1.PluginName); }
                    };
                    ((IToastService)ServiceManager.Services.GetService(typeof(IToastService))!).Show(
                        dialog.ToToastRequest());
                    break;
                }
                case CustomScenarioLoadFromJsonFailedType.方法未找到:
                {
                    var content =
                        Lang.Format("lang.kitopia.messages.file_value_required_plugin_method_is_missing_plugin_value_method_value_update_the_plugin_or_edit_this_node", fileInfo.FullName, e1.PluginName, e1.MethodName);
                    var dialog = new DialogContent
                    {
                        Title = Lang.Format("lang.kitopia.messages.unable_to_load_custom_scenario_value", name),
                        Content = content,
                        CloseButtonText = Lang.Get("lang.kitopia.got_it")
                    };
                    ((IToastService)ServiceManager.Services.GetService(typeof(IToastService))!).Show(
                        dialog.ToToastRequest());
                    break;
                }
                case CustomScenarioLoadFromJsonFailedType.类未找到:
                {
                    break;
                }
                case CustomScenarioLoadFromJsonFailedType.类的序列化转换器未找到:
                {
                    var content = Lang.Format("lang.kitopia.messages.file_value_missing_serializer_for_value_it_may_belong_to_a_plugin", fileInfo.FullName, e1.PluginName);

                    var dialog = new DialogContent
                    {
                        Title = Lang.Format("lang.kitopia.messages.unable_to_load_custom_scenario_value", name),
                        Content = content,
                        CloseButtonText = Lang.Get("lang.kitopia.got_it")
                    };
                    ((IToastService)ServiceManager.Services.GetService(typeof(IToastService))!).Show(
                        dialog.ToToastRequest());
                    break;
                }
                default:
                    break;
            }
            CustomScenarios.Add(new CustomScenario
            {
                Name = name,
                Uuid = fileInfoName,
                HasInit = false,
                InitError = e1.FailedType switch
                {
                    CustomScenarioLoadFromJsonFailedType.插件未找到 => $"缺少插件：{e1.PluginName}",
                    CustomScenarioLoadFromJsonFailedType.插件未启用 => $"插件未启用：{e1.PluginName}",
                    CustomScenarioLoadFromJsonFailedType.方法未找到 => $"方法未找到：{e1.MethodName}",
                    CustomScenarioLoadFromJsonFailedType.类未找到 => $"类型未找到：{e1.MethodName}",
                    CustomScenarioLoadFromJsonFailedType.类的序列化转换器未找到 =>
                        $"缺少序列化转换器：{e1.PluginName}",
                    _ => e1.FailedType.ToString()
                },
                IsActive = false
            });
        }
        catch (Exception e)
        {
            Logger.Error(e,"错误");
            var content = Lang.Format("lang.kitopia.messages.unable_to_load_scenario_file_value_the_file_may_be_corrupted", fileInfo.FullName);
            var dialog = new DialogContent
            {
                Title = Lang.Format("lang.kitopia.messages.unable_to_load_custom_scenario_value", fileInfo.Name),
                Content = content,
                CloseButtonText = Lang.Get("lang.kitopia.got_it"),
                PrimaryAction = () => { }
            };
            ((IToastService)ServiceManager.Services.GetService(typeof(IToastService))!).Show(
                dialog.ToToastRequest());
            CustomScenarios.Add(new CustomScenario
            {
                Name = fileInfo.Name,
                Uuid = fileInfoName,
                IsRunning = false,
                HasInit = false,
                InitError = "加载失败疑似文件已损坏",
                IsActive = false
            });
        }
    }


    public static bool Save(CustomScenario scenario)
    {
        var path = KitopiaPaths.GetCustomScenarioFilePath(scenario.Uuid);
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";

        try
        {
            var j = JsonSerializer.Serialize(scenario, ConfigManger.DefaultOptions);
            File.WriteAllText(temporaryPath, j);
            File.Move(temporaryPath, path, true);
            if (!CustomScenarios.Contains(scenario))
            {
                CustomScenarios.Add(scenario);
                try { scenario.InitHotKey(); }
                catch (Exception e)
                {
                    Logger.Error(e, "情景已保存，但快捷键注册失败: {Scenario}", scenario.Name);
                    ServiceManager.Services.GetService<IToastService>()?.Show(
                        Lang.Get("lang.kitopia.hotkey_registration_failed"), Lang.Format("lang.kitopia.messages.scenario_value_saved_check_its_hotkey_settings", scenario.Name));
                }
            }
            scenario.NotifySaved();
            return true;
        }

        catch (CustomScenarioLoadFromJsonException e)
        {
            Logger.Error(e, "情景保存失败: {Scenario}", scenario.Name);
            switch (e.FailedType)
            {
                case CustomScenarioLoadFromJsonFailedType.类的序列化转换器未找到:
                {
                    var content = Lang.Format("lang.kitopia.messages.unable_to_save_scenario_value_missing_serializer_for_value_it_may_belong_to_a_plugin", scenario.Name, e.PluginName);

                    var dialog = new DialogContent
                    {
                        Title = Lang.Format("lang.kitopia.messages.unable_to_save_custom_scenario_value", scenario.Name),
                        Content = content,
                        CloseButtonText = Lang.Get("lang.kitopia.got_it")
                    };
                    ServiceManager.Services.GetService<IToastService>()?.Show(dialog.ToToastRequest());
                    break;
                }
                default:
                    ServiceManager.Services.GetService<IToastService>()?.Show(
                        Lang.Get("lang.kitopia.scenario_save_failed"), Lang.Format("lang.kitopia.messages.unable_to_save_scenario_value_value", scenario.Name, e.FailedType));
                    break;
            }
            return false;
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            Logger.Error(e, "情景保存失败: {Scenario}", scenario.Name);
            ServiceManager.Services.GetService<IToastService>()?.Show(
                Lang.Get("lang.kitopia.scenario_save_failed"), Lang.Format("lang.kitopia.messages.unable_to_save_scenario_value_value", scenario.Name, e.Message));
            return false;
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                try { File.Delete(temporaryPath); }
                catch (IOException e) { Logger.Warning(e, "清理情景临时文件失败: {Path}", temporaryPath); }
                catch (UnauthorizedAccessException e) { Logger.Warning(e, "清理情景临时文件失败: {Path}", temporaryPath); }
            }
        }
    }


    public static void Remove(CustomScenario scenario, bool deleteFile = true)
    {
        if (scenario.RunHotKey?.UUID != null)
            ServiceManager.Services.GetService<IHotKetImpl>()!.Remove(scenario.RunHotKey.UUID);
        if (scenario.StopHotKey?.UUID != null)
            ServiceManager.Services.GetService<IHotKetImpl>()!.Remove(scenario.StopHotKey.UUID);

        scenario.Dispose();
        if (CustomScenarios.Contains(scenario)) CustomScenarios.Remove(scenario);
        ConfigManger.Save();
        if (deleteFile)
            File.Delete(KitopiaPaths.GetCustomScenarioFilePath(scenario.Uuid));
    }

    public static void UnloadWhichUseThePlugin(string plugStr)
    {
        UnloadWhichUseThePluginAsync(plugStr).GetAwaiter().GetResult();
    }

    public static async Task UnloadWhichUseThePluginAsync(string plugStr)
    {
        for (var i = CustomScenarios.Count - 1; i >= 0; i--)
            if (CustomScenarios[i].IsUseThePlugin(plugStr))
            {
                var customScenario = CustomScenarios[i];
                customScenario.UnRegisterHotKey();
                await customScenario.DisposeAsync();
                CustomScenarios.RemoveAt(i);
            }
    }

    public static void Reload(CustomScenario scenario)
    {
        Remove(scenario, false);
        var configF = new FileInfo(KitopiaPaths.GetCustomScenarioFilePath(scenario.Uuid));
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
            var configF = new FileInfo(KitopiaPaths.GetCustomScenarioFilePath(customScenario.Uuid));
            if (configF.Exists) Load(configF);
        }

        LoadAll();
    }
}
