#region

using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Messaging;
using Kitopia.Desktop.Features.JsonConverter;
using Kitopia.Desktop.Features.Services.HotKey;
using Kitopia.Desktop.Features.Services.Interfaces;
using Kitopia.Desktop.Features.Utils;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using PluginCore.Config;
using PluginCore.CustomScenario.Attribute.ConfigField;
using Serilog;

#endregion

namespace Kitopia.Desktop.Features.Services.Config;

public class ConfigManger : IConfigService
{
    private static ILogger Logger = LogManager.Logger.ForContext<ConfigManger>();
    public static Version Version = new("1.0.0");
    public static string ApiUrl
    {
        get
        {
#if DEBUG
            if (Configs.TryGetValue("KitopiaConfig", out var configBase) &&
                configBase is KitopiaConfig config &&
                config.useLocalhostDebug)
            {
                return "https://localhost:5111";
            }
#endif
            return "https://api.kitopia.top:5111";
        }
    }

    public static string WebUrl
    {
        get
        {
#if DEBUG
            if (Configs.TryGetValue("KitopiaConfig", out var configBase) &&
                configBase is KitopiaConfig config &&
                config.useLocalhostDebug)
            {
                return "http://localhost:3000";
            }
#endif
            return "https://kitopia.top";
        }
    }
    public static Dictionary<string, ConfigBase> Configs = new();
    public static KitopiaConfig Config => Configs.TryGetValue("KitopiaConfig", out var config) ? (KitopiaConfig)config : null!;

    private static readonly Dictionary<HotKeyModel, (object, FieldInfo)> hotkeysMappings = new();
    private static readonly HashSet<string> UnsupportedConfigKeys = new(StringComparer.Ordinal);

    public static JsonSerializerOptions DefaultOptions = new()
    {
        IncludeFields = true,
        WriteIndented = true,
        ReferenceHandler = ReferenceHandler.Preserve,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        Converters =
        {
            new CustomScenarioInputValueJsonConverter(),
            new INodeInputJsonConverter()
        }

        // DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static void Init()
    {
        Directory.CreateDirectory(KitopiaPaths.ConfigsDirectory);

        var defaultConfig = new KitopiaConfig { Name = "KitopiaConfig" };
        defaultConfig.ConfigVersion = defaultConfig.CurrentConfigVersion;
        Configs["KitopiaConfig"] = defaultConfig;
        UnsupportedConfigKeys.Remove("KitopiaConfig");
        var configF = new FileInfo(KitopiaPaths.GetConfigFilePath("KitopiaConfig"));
        if (!configF.Exists)
        {
            WriteConfigFile("KitopiaConfig", Config);
        }
        else
        {
            try
            {
                Configs["KitopiaConfig"] = LoadKitopiaConfig(configF.FullName);
            }
            catch (Exception e)
            {
                Logger.Error(e, "配置文件加载失败");

                var backupPath = $"{configF.FullName}.bak";
                if (File.Exists(backupPath))
                {
                    try
                    {
                        var backupConfig = LoadKitopiaConfig(backupPath);
                        Configs["KitopiaConfig"] = backupConfig;
                        if (backupConfig.ConfigVersion <= backupConfig.CurrentConfigVersion)
                        {
                            try
                            {
                                WriteConfigFile("KitopiaConfig", backupConfig);
                            }
                            catch (Exception recoveryException)
                            {
                                Logger.Error(recoveryException, "配置文件从备份恢复失败");
                            }
                        }

                        Logger.Warning("主配置文件加载失败，已从备份恢复");
                    }
                    catch (Exception backupException)
                    {
                        Logger.Error(backupException, "配置文件备份加载失败");
                    }
                }

                if (ReferenceEquals(Configs["KitopiaConfig"], defaultConfig))
                {
                    try
                    {
                        WriteConfigFile("KitopiaConfig", defaultConfig);
                        Logger.Warning("配置文件和备份均无法加载，已恢复默认配置");
                    }
                    catch (Exception recoveryException)
                    {
                        Logger.Error(recoveryException, "默认配置恢复失败");
                    }
                }
            }
        }

        Config!.BeforeLoad();
        Config.AfterLoad();
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
                            if (!ServiceManager.Services.GetService<IHotKetImpl>()!.Register(hotKeyModel, value as Action<HotKeyModel>))
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
                case "mouseCapture":
                {
                    Dispatcher.UIThread.Invoke(() =>
                        ServiceManager.Services.GetRequiredService<IHotKetImpl>().StartHook());
                    break;
                }
                case "autoStart":
                {
                    ServiceManager.Services.GetService<IApplicationService>()!
                        .ChangeAutoStart(args.Value as bool? ?? false);

                    break;
                }
                case "autoStartEverything":
                {
                    ServiceManager.Services.GetService<ISearchFeatureService>()?
                        .SetEverythingAvailability(!(bool)args.Value);
                    break;
                }
                case "themeChoice":
                {
                    switch ((ThemeEnum)args.Value)
                    {
                        case ThemeEnum.跟随系统:
                        {
                            ServiceManager.Services.GetService<IThemeChange>()!
                                .followSys(true);
                            break;
                        }
                        case ThemeEnum.深色:
                        {
                            ServiceManager.Services.GetService<IThemeChange>()!
                                .followSys(false);
                            ServiceManager.Services.GetService<IThemeChange>()!
                                .changeTo("theme_dark");
                            break;
                        }
                        case ThemeEnum.浅色:
                        {
                            ServiceManager.Services.GetService<IThemeChange>()!
                                .followSys(false);
                            ServiceManager.Services.GetService<IThemeChange>()!
                                .changeTo("theme_light");
                            break;
                        }
                    }

                    break;
                }
            }
        };
    }

    private static KitopiaConfig LoadKitopiaConfig(string filePath)
    {
        var json = File.ReadAllText(filePath);
        using var document = JsonDocument.Parse(json);
        var config = JsonSerializer.Deserialize<KitopiaConfig>(json, DefaultOptions) ?? new KitopiaConfig();
        config.Name = "KitopiaConfig";
        MigrateConfig("KitopiaConfig", document.RootElement, config);
        return config;
    }

    internal static void MigrateConfig(JsonElement root, ConfigBase config)
    {
        MigrateConfig(null, root, config);
    }

    internal static void MigrateConfig(string? key, JsonElement root, ConfigBase config)
    {
        if (config.ConfigVersion > config.CurrentConfigVersion)
        {
            if (key is not null)
                UnsupportedConfigKeys.Add(key);

            Logger.Warning("配置版本 {ConfigVersion} 高于当前版本 {CurrentConfigVersion}，跳过迁移",
                config.ConfigVersion, config.CurrentConfigVersion);
            return;
        }

        if (key is not null)
            UnsupportedConfigKeys.Remove(key);

        config.MigrateConfig(root);
        config.ConfigVersion = config.CurrentConfigVersion;
    }

    internal static void WriteConfigFile(string key, ConfigBase configBase)
    {
        var configFile = new FileInfo(KitopiaPaths.GetConfigFilePath(key));
        if (configFile.DirectoryName is { } directory)
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = $"{configFile.FullName}.{Guid.NewGuid():N}.tmp";
        var backupPath = $"{configFile.FullName}.bak";
        try
        {
            configBase.ConfigVersion = configBase.CurrentConfigVersion;
            var json = JsonSerializer.Serialize(configBase, configBase.GetType(), DefaultOptions);
            File.WriteAllText(temporaryPath, json);

            if (configFile.Exists)
            {
                try
                {
                    File.Replace(temporaryPath, configFile.FullName, backupPath, true);
                }
                catch (PlatformNotSupportedException)
                {
                    File.Move(temporaryPath, configFile.FullName, true);
                }
            }
            else
            {
                File.Move(temporaryPath, configFile.FullName);
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private static void SaveConfigFile(string key, ConfigBase configBase)
    {
        if (configBase.ConfigVersion > configBase.CurrentConfigVersion)
            UnsupportedConfigKeys.Add(key);

        if (UnsupportedConfigKeys.Contains(key))
        {
            Logger.Warning("配置 {Key} 使用更高版本，跳过保存以避免覆盖未知字段", key);
            return;
        }

        configBase.BeforeSave();
        WriteConfigFile(key, configBase);
        configBase.AfterSave();
    }

    public static void RemoveConfig(string key)
    {
        foreach (var (s, value) in Configs.Where(x => x.Key.StartsWith(key)).ToList())
        {
            value.GetType()
                .BaseType.GetField("Instance")
                .SetValue(value, null);
            Configs.Remove(s);
            UnsupportedConfigKeys.Remove(s);
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
            SaveConfigFile(configsKey, configBase);
        }

        WeakReferenceMessenger.Default.Send<string, string>("ConfigSave", "ConfigSave");
    }

    public static void Save(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            Logger.Warning("尝试保存配置但 key 为 null 或空，跳过保存");
            return;
        }

        if (!Configs.TryGetValue(key, out var configBase))
        {
            Logger.Warning("未找到 key 为 {Key} 的配置，跳过保存", key);
            return;
        }

        SaveConfigFile(key, configBase);
        WeakReferenceMessenger.Default.Send<string, string>("ConfigSave", "ConfigSave");
    }

    Version IConfigService.Version => Version;
    string IConfigService.ApiUrl => ApiUrl;
    string IConfigService.WebUrl => WebUrl;
    Dictionary<string, ConfigBase> IConfigService.Configs => Configs;
    KitopiaConfig IConfigService.Config => Config;
    JsonSerializerOptions IConfigService.DefaultOptions => DefaultOptions;

    void IConfigService.Init()
    {
        Init();
    }

    void IConfigService.RemoveConfig(string key)
    {
        RemoveConfig(key);
    }

    void IConfigService.RequsetUpdateHotKey(HotKeyModel hotKeyModel)
    {
        RequsetUpdateHotKey(hotKeyModel);
    }

    void IConfigService.Save()
    {
        Save();
    }

    void IConfigService.Save(string key)
    {
        Save(key);
    }

}
