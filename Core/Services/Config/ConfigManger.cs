#region

using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using CommunityToolkit.Mvvm.Messaging;
using Core.JsonConverter;
using Core.Services.HotKey;
using Core.Services.Interfaces;
using Core.Utils;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using PluginCore.Config;
using PluginCore.CustomScenario.Attribute.ConfigField;
using Serilog;
using SearchWindowViewModel = Core.ViewModel.Windows.SearchWindowViewModel;

#endregion

namespace Core.Services.Config;

public class ConfigManger
{
    private static ILogger Logger = LogManager.Logger.ForContext<ConfigManger>();
    public static Version Version = new("1.0.0");
    public static string ApiUrl = "https://api.kitopia.top:5111";
    public static Dictionary<string, ConfigBase> Configs = new();
    public static KitopiaConfig Config => (KitopiaConfig)Configs["KitopiaConfig"];

    private static readonly Dictionary<HotKeyModel, (object, FieldInfo)> hotkeysMappings = new();

    public static JsonSerializerOptions DefaultOptions = new()
    {
        IncludeFields = true,
        WriteIndented = true,
        ReferenceHandler = ReferenceHandler.Preserve,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        Converters = { new CustomScenarioInputValueJsonConverter(), new INodeInputJsonConverter() }

        // DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static void Init()
    {
        Directory.CreateDirectory(KitopiaPaths.ConfigsDirectory);

        Configs.Add("KitopiaConfig", new KitopiaConfig { Name = "KitopiaConfig" });
        var configF = new FileInfo(KitopiaPaths.GetConfigFilePath("KitopiaConfig"));
        if (!configF.Exists)
        {
            var j = JsonSerializer.Serialize(Config, DefaultOptions);
            File.WriteAllText(configF.FullName, j);
        }
        else
        {
            var json = File.ReadAllText(configF.FullName);
            try
            {
                Configs["KitopiaConfig"] =
                    JsonSerializer.Deserialize(json, Config.GetType(), DefaultOptions)! as ConfigBase ??
                    Config;
            }
            catch (Exception e)
            {
                Logger.Error(e, "配置文件加载失败");
            }
        }

        Config!.BeforeLoad();
        Config.GetType()
            .GetFields(BindingFlags.Instance | BindingFlags.Public)
            .ToList()
            .ForEach(x =>
            {
                if (x.GetCustomAttribute<ConfigField>() is { } configField)
                    if (configField.FieldType == ConfigFieldType.快捷键)
                    {
                        var hotKeyModel = (HotKeyModel)x.GetValue(Config);
                        hotkeysMappings.Add(hotKeyModel, (Config, x));
                        if (Config.invokes.TryGetValue(configField.ActionName, out var value))
                            if (!ServiceManager.Services.GetService<IHotKetImpl>()!.Add(hotKeyModel, value as Action<HotKeyModel>))
                                ServiceManager.Services.GetService<IToastService>().Show(new DialogContent
                                {
                                    Title = $"快捷键{hotKeyModel.SignName}设置失败",
                                    Content = "请重新设置快捷键，按键与系统其他程序冲突",
                                    CloseButtonText = "关闭"
                                }.ToToastRequest());
                    }
            });
        Config.ConfigChanged += (sender, args) =>
        {
            switch (args.Name)
            {
                case "autoStart":
                {
                    ServiceManager.Services.GetService<IApplicationService>()
                        .ChangeAutoStart(args.Value as bool? ?? false);

                    break;
                }
                case "autoStartEverything":
                {
                    ((SearchWindowViewModel)ServiceManager.Services.GetService(typeof(SearchWindowViewModel)))
                        .EverythingIsOk =
                        !(bool)args.Value;
                    break;
                }
                case "themeChoice":
                {
                    switch ((ThemeEnum)args.Value)
                    {
                        case ThemeEnum.跟随系统:
                        {
                            ServiceManager.Services.GetService<IThemeChange>()
                                .followSys(true);
                            break;
                        }
                        case ThemeEnum.深色:
                        {
                            ServiceManager.Services.GetService<IThemeChange>()
                                .followSys(false);
                            ServiceManager.Services.GetService<IThemeChange>()
                                .changeTo("theme_dark");
                            break;
                        }
                        case ThemeEnum.浅色:
                        {
                            ServiceManager.Services.GetService<IThemeChange>()
                                .followSys(false);
                            ServiceManager.Services.GetService<IThemeChange>()
                                .changeTo("theme_light");
                            break;
                        }
                    }

                    break;
                }
            }
        };
    }

    public static void RemoveConfig(string key)
    {
        foreach (var (s, value) in Configs.Where(x => x.Key.StartsWith(key)))
        {
            value.GetType()
                .BaseType.GetField("Instance")
                .SetValue(value, null);
            Configs.Remove(s);
        }
    }

    public static void RequsetUpdateHotKey(HotKeyModel hotKeyModel)
    {
        foreach (var (key, (item2, fieldInfo)) in hotkeysMappings)
        {
            if (key.UUID != hotKeyModel.UUID) continue;

            try
            {
                fieldInfo.SetValue(item2, hotKeyModel);
            }
            catch
            {
                // ignored
            }
        }
    }

    public static void Save()
    {
        var keyCollection = Configs.Keys.ToList();
        foreach (var configsKey in keyCollection)
        {
            var configBase = Configs[configsKey];
            var configF = new FileInfo(KitopiaPaths.GetConfigFilePath(configsKey));


            var j = JsonSerializer.Serialize(configBase, configBase.GetType(), DefaultOptions);
            File.WriteAllText(configF.FullName, j);
        }

        WeakReferenceMessenger.Default.Send<string, string>("ConfigSave", "ConfigSave");
    }

    public static void Save(string key)
    {
        var configBase = Configs[key];
        if (configBase is null) return;

        var configF = new FileInfo(KitopiaPaths.GetConfigFilePath(key));

        var j = JsonSerializer.Serialize(configBase, configBase.GetType(), DefaultOptions);
        File.WriteAllText(configF.FullName, j);
        WeakReferenceMessenger.Default.Send<string, string>("ConfigSave", "ConfigSave");
    }
}
