#region

using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Messaging;
using Kitopia.Desktop.Features.JsonConverter;
using Kitopia.Desktop.Features.Services.Interfaces;
using Kitopia.Desktop.Features.Utils;
using Kitopia.Feature.Localization;
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
            lock (SaveGate)
            {
                PendingWrites.Clear();
                _configs = value;
                AllConfigs = value.AsReadOnly();
            }
        }
    }
    public static KitopiaConfig Config => Configs.TryGetValue("KitopiaConfig", out var config) ? (KitopiaConfig)config : null!;

    private static readonly Dictionary<string, (ConfigBase Config, FieldInfo Field)> hotkeysMappings = new();
    private static readonly HashSet<string> UnsupportedConfigKeys = new(StringComparer.Ordinal);
    private static readonly HashSet<string> RecoveredConfigKeys = new(StringComparer.Ordinal);
    private static readonly HashSet<string> BackupLoadedConfigKeys = new(StringComparer.Ordinal);
    private static readonly object SaveGate = new();
    private static readonly Dictionary<string, string> PendingWrites = new(StringComparer.Ordinal);

    private static void NotifyConfigIssue(string key, string message, NotificationType type)
    {
        try
        {
            var toast = ServiceManager.Services?.GetService<IToastService>();
            if (toast is null) return;

            _ = toast.Show(new ToastRequest
            {
                Header = Lang.Get("lang.kitopia.configuration_error"),
                Text = $"{key}：{message}",
                NotificationType = type,
                AutoCloseDelay = null
            });
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "无法显示配置 {Key} 的异常提示", key);
        }
    }

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
        try
        {
            Directory.CreateDirectory(KitopiaPaths.ConfigsDirectory);
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "无法访问配置目录");
            NotifyConfigIssue(Lang.Get("lang.kitopia.messages.configuration_directory"), Lang.Get("lang.kitopia.messages.cannot_access_the_configuration_directory_check_permissions_and_disk_status"), NotificationType.Error);
            throw;
        }
        if (KitopiaPaths.ConfigMigrationError is { } migrationError)
        {
            Logger.Warning(migrationError, "旧配置目录迁移失败");
            NotifyConfigIssue(Lang.Get("lang.kitopia.messages.configuration_directory"), Lang.Get("lang.kitopia.messages.legacy_configuration_migration_failed_some_settings_may_be_unavailable_check_the_logs_and_configuration_directory"), NotificationType.Warning);
        }

        RemoveConfig("KitopiaConfig");
        LoadConfig("KitopiaConfig", new KitopiaConfig(), useDefaultsOnInvalidJson: true);
        Lang.Current.UseLanguage(Config.language);
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
                                    Title = Lang.Format("lang.kitopia.messages.unable_to_set_hotkey_value", hotKeyModel.SignName),
                                    Content = Lang.Get("lang.kitopia.choose_another_hotkey_this_key_combination_is_already_in_use"),
                                    CloseButtonText = Lang.Get("lang.kitopia.close")
                                }.ToToastRequest());
                    }
            });
        Config.ConfigChanged += (sender, args) =>
        {
            switch (args.Name)
            {
                case nameof(KitopiaConfig.followSystemAccentColor):
                {
                    ServiceManager.Services.GetRequiredService<IThemeChange>()
                        .SetAccentColor(args.Value is true, Config.accentColor);
                    break;
                }
                case nameof(KitopiaConfig.accentColor):
                {
                    ServiceManager.Services.GetRequiredService<IThemeChange>()
                        .SetAccentColor(Config.followSystemAccentColor, (string)args.Value!);
                    break;
                }
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
        RecoveredConfigKeys.Remove(key);
        BackupLoadedConfigKeys.Remove(key);
        defaults.Name = key;
        var filePath = KitopiaPaths.GetConfigFilePath(key);
        var config = defaults;
        if (File.Exists(filePath) || File.Exists(filePath + ".bak"))
        {
            JsonDocument? document = null;
            var mainMissing = !File.Exists(filePath);
            var loadedBackup = false;
            var backupAttempted = false;
            var restoreFailed = false;
            try
            {
                try
                {
                    if (mainMissing)
                        throw new JsonException($"配置 {key} 主文件不存在。");

                    document = JsonDocument.Parse(File.ReadAllText(filePath));
                    config = (ConfigBase?)document.RootElement.Deserialize(defaults.GetType(), DefaultOptions)
                             ?? throw new JsonException($"配置 {key} 不能为空。");
                }
                catch (JsonException) when (File.Exists(filePath + ".bak"))
                {
                    backupAttempted = true;
                    document?.Dispose();
                    document = null;
                    try
                    {
                        document = JsonDocument.Parse(File.ReadAllText(filePath + ".bak"));
                        config = (ConfigBase?)document.RootElement.Deserialize(defaults.GetType(), DefaultOptions)
                                 ?? throw new JsonException($"配置 {key} 的备份不能为空。");
                        BackupLoadedConfigKeys.Add(key);
                        loadedBackup = true;
                        Logger.Warning("配置 {Key} 主文件不可用，已加载备份", key);
                        if (mainMissing)
                        {
                            try
                            {
                                File.Copy(filePath + ".bak", filePath);
                                Logger.Information("配置 {Key} 已从备份恢复主文件", key);
                            }
                            catch (Exception exception)
                            {
                                restoreFailed = true;
                                Logger.Warning(exception, "配置 {Key} 无法恢复主文件，继续使用备份", key);
                            }
                        }
                    }
                    catch (JsonException exception) when (useDefaultsOnInvalidJson)
                    {
                        document?.Dispose();
                        document = null;
                        config = defaults;
                        config.ConfigVersion = config.CurrentConfigVersion;
                        RecoveredConfigKeys.Add(key);
                        Logger.Error(exception, "配置 {Key} 和备份无法解析，本次使用默认值，保留原文件", key);
                        NotifyConfigIssue(key, Lang.Get("lang.kitopia.messages.the_configuration_and_backup_are_unreadable_using_defaults_in_memory_with_saving_suspended_check_the_configuration_files"), NotificationType.Error);
                    }
                }
                catch (JsonException exception) when (useDefaultsOnInvalidJson)
                {
                    document?.Dispose();
                    document = null;
                    config = defaults;
                    config.ConfigVersion = config.CurrentConfigVersion;
                    RecoveredConfigKeys.Add(key);
                    Logger.Error(exception, "配置 {Key} 和备份无法解析，本次使用默认值，保留原文件", key);
                    NotifyConfigIssue(key, Lang.Get("lang.kitopia.messages.the_configuration_is_unreadable_and_no_backup_is_available_using_defaults_in_memory_with_saving_suspended_check_the_configuration_file"), NotificationType.Error);
                }
                config.Name = key;
                // Migration and plugin callbacks are outside JSON recovery: their failures must not replace user data.
                if (document is not null) MigrateConfig(key, document.RootElement, config);
                if (loadedBackup)
                {
                    var message = mainMissing
                        ? restoreFailed
                            ? Lang.Get("lang.kitopia.messages.the_configuration_is_missing_loaded_the_backup_but_could_not_restore_the_file_check_disk_permissions")
                            : Lang.Get("lang.kitopia.messages.the_missing_configuration_was_restored_from_its_backup")
                        : Lang.Get("lang.kitopia.messages.the_configuration_is_damaged_loaded_the_backup_the_next_save_will_repair_the_file_and_preserve_the_valid_backup");
                    NotifyConfigIssue(key, message, NotificationType.Warning);
                }
                else if (document is not null && File.Exists(filePath + ".bak"))
                {
                    try
                    {
                        using var backup = JsonDocument.Parse(File.ReadAllText(filePath + ".bak"));
                        _ = backup.RootElement.Deserialize(defaults.GetType(), DefaultOptions)
                            ?? throw new JsonException($"配置 {key} 的备份不能为空。");
                    }
                    catch (Exception exception)
                    {
                        Logger.Warning(exception, "配置 {Key} 的备份无法读取，主文件仍可用", key);
                        NotifyConfigIssue(key, Lang.Get("lang.kitopia.messages.the_backup_is_damaged_or_unreadable_the_configuration_is_still_usable_check_the_backup_file"), NotificationType.Error);
                    }
                }
            }
            catch (Exception exception)
            {
                Logger.Error(exception, "加载配置 {Key} 失败", key);
                var message = backupAttempted && !loadedBackup
                    ? Lang.Get("lang.kitopia.messages.the_configuration_and_backup_are_unreadable_nothing_was_loaded_or_overwritten_check_the_logs")
                    : Lang.Get("lang.kitopia.messages.failed_to_load_the_configuration_the_original_file_was_preserved_check_the_logs_and_configuration_file");
                NotifyConfigIssue(key, message, NotificationType.Error);
                throw;
            }
            finally { document?.Dispose(); }
        }
        else
        {
            config.ConfigVersion = config.CurrentConfigVersion;
            Logger.Information("配置 {Key} 主文件和备份均不存在，创建默认配置：{Path}", key, filePath);
            try { WriteConfigFile(key, JsonSerializer.Serialize(config, config.GetType(), DefaultOptions)); }
            catch (Exception exception)
            {
                Logger.Error(exception, "创建配置 {Key} 失败", key);
                NotifyConfigIssue(key, Lang.Get("lang.kitopia.messages.unable_to_create_default_configuration_check_disk_space_and_directory_permissions"), NotificationType.Error);
                throw;
            }
            if (useDefaultsOnInvalidJson)
                NotifyConfigIssue(key, Lang.Get("lang.kitopia.messages.no_configuration_or_backup_was_found_so_defaults_were_created_check_the_configuration_directory_if_this_is_not_the_first_launch"), NotificationType.Warning);
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
        catch (Exception exception)
        {
            RemoveConfig(key);
            Logger.Error(exception, "初始化配置 {Key} 失败", key);
            NotifyConfigIssue(key, Lang.Get("lang.kitopia.messages.configuration_initialization_failed_the_original_file_was_preserved_check_the_logs"), NotificationType.Error);
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
            if (key is not null)
                NotifyConfigIssue(key, Lang.Get("lang.kitopia.messages.the_configuration_comes_from_a_newer_version_migration_and_saving_are_suspended_to_preserve_unknown_data"), NotificationType.Warning);
            return;
        }

        if (key is not null)
            UnsupportedConfigKeys.Remove(key);

        config.MigrateConfig(root);
        config.ConfigVersion = config.CurrentConfigVersion;
    }

    internal static bool WriteConfigFile(string key, string json, bool preserveBackup = false, bool queued = false)
    {
        var configFile = new FileInfo(KitopiaPaths.GetConfigFilePath(key));
        lock (SaveGate)
        {
            if (queued && (!PendingWrites.TryGetValue(key, out var pending) || !ReferenceEquals(pending, json)))
                return false;
            if (!preserveBackup && configFile.Exists && File.ReadAllText(configFile.FullName) == json)
                return false;
        }

        if (configFile.DirectoryName is { } directory)
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = $"{configFile.FullName}.{Guid.NewGuid():N}.tmp";
        var backupPath = $"{configFile.FullName}.bak";
        try
        {
            File.WriteAllText(temporaryPath, json);

            lock (SaveGate)
            {
                // A newer request may have arrived while the temporary file was being written.
                if (queued && (!PendingWrites.TryGetValue(key, out var pending) || !ReferenceEquals(pending, json)))
                    return false;
                if (!preserveBackup && File.Exists(configFile.FullName) && File.ReadAllText(configFile.FullName) == json)
                    return false;

                if (File.Exists(configFile.FullName))
                {
                    try
                    {
                        File.Replace(temporaryPath, configFile.FullName,
                            preserveBackup ? null : backupPath, true);
                    }
                    catch (PlatformNotSupportedException)
                    {
                        if (!preserveBackup)
                            File.Copy(configFile.FullName, backupPath, true);
                        File.Move(temporaryPath, configFile.FullName, true);
                    }
                }
                else
                {
                    File.Move(temporaryPath, configFile.FullName);
                }
                BackupLoadedConfigKeys.Remove(key);
                return true;
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private static string? PrepareConfigSave(string key, ConfigBase configBase)
    {
        if (configBase.ConfigVersion > configBase.CurrentConfigVersion)
        {
            if (UnsupportedConfigKeys.Add(key))
                NotifyConfigIssue(key, Lang.Get("lang.kitopia.messages.the_configuration_comes_from_a_newer_version_saving_is_suspended_to_preserve_unknown_data"), NotificationType.Warning);
        }

        if (UnsupportedConfigKeys.Contains(key))
        {
            Logger.Warning("配置 {Key} 使用更高版本，跳过保存以避免覆盖未知字段", key);
            return null;
        }

        if (RecoveredConfigKeys.Contains(key))
        {
            Logger.Warning("配置 {Key} 仅使用了内存默认值，跳过保存以保留损坏文件", key);
            return null;
        }

        configBase.BeforeSave();
        configBase.ConfigVersion = configBase.CurrentConfigVersion;
        return JsonSerializer.Serialize(configBase, configBase.GetType(), DefaultOptions);
    }

    private static bool SaveConfigFile(string key, ConfigBase configBase)
    {
        PendingWrites.Remove(key);
        try
        {
            var json = PrepareConfigSave(key, configBase);
            if (json is null || !WriteConfigFile(key, json, BackupLoadedConfigKeys.Contains(key)))
                return false;

            configBase.AfterSave();
            return true;
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "保存配置 {Key} 失败", key);
            NotifyConfigIssue(key, Lang.Get("lang.kitopia.messages.failed_to_save_the_configuration_recent_changes_may_not_be_written_check_the_logs"), NotificationType.Error);
            throw;
        }
    }

    public static void RemoveConfig(string key)
    {
        lock (SaveGate)
        {
            foreach (var name in Configs.Keys.Where(name => name == key ||
                         name.StartsWith(key + "#", StringComparison.Ordinal)).ToArray())
            {
                var config = Configs[name];
                foreach (var uuid in hotkeysMappings.Where(pair => ReferenceEquals(pair.Value.Config, config))
                             .Select(pair => pair.Key).ToArray())
                    hotkeysMappings.Remove(uuid);
                PendingWrites.Remove(name);
                Configs.Remove(name);
                UnsupportedConfigKeys.Remove(name);
                RecoveredConfigKeys.Remove(name);
                BackupLoadedConfigKeys.Remove(name);
            }
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
        var saved = false;
        List<Exception>? errors = null;
        lock (SaveGate)
        {
            var keyCollection = Configs.Keys.ToList();
            foreach (var configsKey in keyCollection)
            {
                if (!Configs.TryGetValue(configsKey, out var configBase)) continue;
                try { saved |= SaveConfigFile(configsKey, configBase); }
                catch (Exception exception) { (errors ??= []).Add(exception); }
            }
        }

        if (saved) WeakReferenceMessenger.Default.Send<string, string>("ConfigSave", "ConfigSave");
        if (errors is not null) throw new AggregateException("One or more configurations could not be saved.", errors);
    }

    public static void Save(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            Logger.Warning("尝试保存配置但 key 为 null 或空，跳过保存");
            NotifyConfigIssue(Lang.Get("lang.kitopia.messages.unknown_configuration"), Lang.Get("lang.kitopia.messages.saving_was_skipped_because_the_configuration_identifier_is_missing"), NotificationType.Warning);
            return;
        }

        bool saved;
        lock (SaveGate)
        {
            if (!Configs.TryGetValue(key, out var configBase))
            {
                Logger.Warning("未找到 key 为 {Key} 的配置，跳过保存", key);
                NotifyConfigIssue(key, Lang.Get("lang.kitopia.messages.saving_was_skipped_because_the_configuration_has_not_been_loaded"), NotificationType.Warning);
                return;
            }
            saved = SaveConfigFile(key, configBase);
        }
        if (saved) WeakReferenceMessenger.Default.Send<string, string>("ConfigSave", "ConfigSave");
    }

    public static async Task SaveAsync(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        try
        {
            ConfigBase configBase;
            string json;
            bool preserveBackup;
            lock (SaveGate)
            {
                if (!Configs.TryGetValue(key, out configBase!))
                    return;

                PendingWrites.Remove(key);
                var snapshot = PrepareConfigSave(key, configBase);
                if (snapshot is null) return;
                json = snapshot;
                preserveBackup = BackupLoadedConfigKeys.Contains(key);
                PendingWrites[key] = json;
            }

            // Serialize on the caller's thread; only immutable JSON crosses to the file writer.
            var saved = await Task.Run(() =>
            {
                try { return WriteConfigFile(key, json, preserveBackup, queued: true); }
                finally
                {
                    lock (SaveGate)
                        if (PendingWrites.TryGetValue(key, out var pending) && ReferenceEquals(pending, json))
                            PendingWrites.Remove(key);
                }
            });
            if (!saved) return;

            lock (SaveGate)
            {
                if (!Configs.TryGetValue(key, out var current) || !ReferenceEquals(current, configBase))
                    return;
            }
            configBase.AfterSave();
            WeakReferenceMessenger.Default.Send<string, string>("ConfigSave", "ConfigSave");
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "保存配置 {Key} 失败", key);
            NotifyConfigIssue(key, Lang.Get("lang.kitopia.messages.failed_to_save_the_configuration_recent_changes_may_not_be_written_check_the_logs"), NotificationType.Error);
            throw;
        }
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
