#region

using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Messaging;
using Kitopia.Desktop.Features.JsonConverter;
using Kitopia.Desktop.Features.Services.Interfaces;
using Kitopia.Desktop.Features.Utils;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using PluginCore.Config;
using PluginCore.CustomScenario.Attribute.ConfigField;
using Serilog;

#endregion

namespace Kitopia.Desktop.Features.Services.Config;

public class ConfigManger : IConfigService, IConfigProvider
{
    private static ILogger Logger = LogManager.Logger.ForContext<ConfigManger>();
    public static Version Version = System.Version.Parse(ServiceManager.Version);
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
    private static Dictionary<string, ConfigBase> _configs = new();
    public static IReadOnlyDictionary<string, ConfigBase> AllConfigs { get; private set; } = _configs.AsReadOnly();
    internal static Dictionary<string, ConfigBase> Configs
    {
        get => _configs;
        set
        {
            _configs = value;
            AllConfigs = value.AsReadOnly();
        }
    }
    public static KitopiaConfig Config => Configs.TryGetValue("KitopiaConfig", out var config) ? (KitopiaConfig)config : null!;

    private static readonly Dictionary<string, (ConfigBase Config, FieldInfo Field)> hotkeysMappings = new();
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

        RemoveConfig("KitopiaConfig");
        LoadConfig("KitopiaConfig", new KitopiaConfig(), useDefaultsOnInvalidJson: true);
        Config.GetType()
            .GetFields(BindingFlags.Instance | BindingFlags.Public)
            .ToList()
            .ForEach(x =>
            {
                if (x.GetCustomAttribute<ConfigField>() is { } configField)
                    if (configField.FieldType == ConfigFieldType.快捷键)
                    {
                        var hotKeyModel = (HotKeyModel)x.GetValue(Config);
                        hotkeysMappings[hotKeyModel.UUID] = (Config, x);
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

    internal static ConfigBase LoadConfig(string key, ConfigBase defaults, bool useDefaultsOnInvalidJson = false)
    {
        defaults.Name = key;
        var filePath = KitopiaPaths.GetConfigFilePath(key);
        var config = defaults;
        if (File.Exists(filePath))
        {
            JsonDocument? document = null;
            try
            {
                try
                {
                    try
                    {
                        document = JsonDocument.Parse(File.ReadAllText(filePath));
                        config = (ConfigBase?)document.RootElement.Deserialize(defaults.GetType(), DefaultOptions)
                                 ?? throw new JsonException($"配置 {key} 不能为空。");
                    }
                    catch (JsonException) when (File.Exists(filePath + ".bak"))
                    {
                        document?.Dispose();
                        document = JsonDocument.Parse(File.ReadAllText(filePath + ".bak"));
                        config = (ConfigBase?)document.RootElement.Deserialize(defaults.GetType(), DefaultOptions)
                                 ?? throw new JsonException($"配置 {key} 的备份不能为空。");
                        Logger.Warning("配置 {Key} 无法解析，已加载备份，原文件保留至下次保存", key);
                    }
                }
                catch (JsonException exception) when (useDefaultsOnInvalidJson)
                {
                    document?.Dispose();
                    document = null;
                    config = defaults;
                    config.ConfigVersion = config.CurrentConfigVersion;
                    Logger.Error(exception, "配置 {Key} 和备份无法解析，本次使用默认值，保留原文件", key);
                }
                config.Name = key;
                // Migration and plugin callbacks are outside JSON recovery: their failures must not replace user data.
                if (document is not null) MigrateConfig(key, document.RootElement, config);
            }
            finally { document?.Dispose(); }
        }
        else
        {
            config.ConfigVersion = config.CurrentConfigVersion;
            WriteConfigFile(key, config);
        }

        Configs.Add(key, config);
        try
        {
            // Legacy plugins may read Instance inside AfterLoad. Never retain it globally.
#pragma warning disable CS0618
            var previous = ConfigBase.Instance;
            try
            {
                ConfigBase.Instance = config;
                config.BeforeLoad();
                config.AfterLoad();
            }
            finally
            {
                ConfigBase.Instance = previous;
            }
#pragma warning restore CS0618
            foreach (var field in config.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
                if (field.GetCustomAttribute<ConfigField>()?.FieldType == ConfigFieldType.快捷键 &&
                    field.GetValue(config) is HotKeyModel model)
                    hotkeysMappings.Add(model.UUID, (config, field));
            return config;
        }
        catch
        {
            RemoveConfig(key);
            throw;
        }
    }

    public T Get<T>() where T : ConfigBase => Configs.Values.OfType<T>().SingleOrDefault()
        ?? throw new InvalidOperationException($"配置 {typeof(T).FullName} 尚未加载。");

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
        foreach (var name in Configs.Keys.Where(name => name == key ||
                     name.StartsWith(key + "#", StringComparison.Ordinal)).ToArray())
        {
            var config = Configs[name];
            foreach (var uuid in hotkeysMappings.Where(pair => ReferenceEquals(pair.Value.Config, config))
                         .Select(pair => pair.Key).ToArray())
                hotkeysMappings.Remove(uuid);
            Configs.Remove(name);
            UnsupportedConfigKeys.Remove(name);
        }
    }

    public static void RequsetUpdateHotKey(HotKeyModel model)
    {
        if (hotkeysMappings.TryGetValue(model.UUID, out var owner))
            owner.Field.SetValue(owner.Config, model);
    }

    public static void SaveHotKey(HotKeyModel model)
    {
        if (!hotkeysMappings.TryGetValue(model.UUID, out var owner)) return;
        owner.Field.SetValue(owner.Config, model);
        Save(owner.Config.Name);
        owner.Config.OnConfigChanged(model, owner.Field.Name, model);
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
    IReadOnlyDictionary<string, ConfigBase> IConfigService.Configs => AllConfigs;
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
