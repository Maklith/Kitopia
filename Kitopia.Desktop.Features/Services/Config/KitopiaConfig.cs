using Kitopia.Feature.Localization;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using DeviceDiscoverySignature = Kitopia.Feature.DeviceCommunication.Discovery.DeviceDiscoverySignature;
using Kitopia.Desktop.Features.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using PluginCore.Config;
using PluginCore.CustomScenario.Attribute.ConfigField;
using Serilog;

// ReSharper disable InconsistentNaming
// ReSharper disable FieldCanBeMadeReadOnly.Global

namespace Kitopia.Desktop.Features.Services.Config;

public class HistoryItem
{
    public List<DateTime> AccessTimes { get; set; } = new();
}
public enum ThemeEnum
{
    [System.ComponentModel.Description("lang.kitopia.system_default")]
    跟随系统,
    [System.ComponentModel.Description("lang.kitopia.dark")]
    深色,
    [System.ComponentModel.Description("lang.kitopia.light")]
    浅色
}
[ConfigName("lang.kitopia.kitopia_settings")]
public class KitopiaConfig : ConfigBase
{
    internal const int CurrentSchemaVersion = 2;

    [JsonIgnore]
    public override int CurrentConfigVersion => CurrentSchemaVersion;

    public KitopiaConfig()
    {
        Name = "KitopiaConfig";
    }

    public override void MigrateConfig(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return;

        const int managedCollectionsVersion = 1;
        const int previewScopeVersion = 2;

        if (ConfigVersion < managedCollectionsVersion)
            MigrateLegacyCollections(root);

        if (ConfigVersion < previewScopeVersion
            && root.TryGetProperty("mouseHotkey", out var previewHotkey)
            && previewHotkey.ValueKind == JsonValueKind.Object
            && !previewHotkey.TryGetProperty(nameof(HotKeyModel.ProcessScope), out _))
        {
            mouseHotkey.ProcessScope = HotKeyProcessScope.Include;
            mouseHotkey.ProcessNames = ["explorer.exe"];
            mouseHotkey.IgnoreTextInput = true;
        }
    }

    private void MigrateLegacyCollections(JsonElement root)
    {
        if (!root.TryGetProperty("customCollections", out var legacy)
            || legacy.ValueKind != JsonValueKind.Array)
            return;

        foreach (var value in legacy.EnumerateArray())
        {
            if (value.ValueKind != JsonValueKind.String || value.GetString() is not { } path)
                continue;

            var target = Directory.Exists(path)
                ? managedIndexDirectories
                : managedIndexFiles;
            if (!target.Contains(path, StringComparer.OrdinalIgnoreCase))
                target.Add(path);
        }
    }

    private static ILogger Logger = LogManager.Logger.ForContext<KitopiaConfig>();
    internal static readonly IReadOnlyList<string> DefaultTransientDirectoryNames =
    [
        "temp", "tmp", "temporary", "cache", "caches", "inetcache", "temporary internet files",
        ".minecraft", "assets", "data", "tdata", "logs","node_modules"
    ];
    internal static readonly IReadOnlyList<string> DefaultAllowedFileExtensions =
    [
        ".md", ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx",
        ".jpg", ".jpeg", ".png", ".bmp", ".webp", ".gif"
    ];

    public List<string> alwayShows = new();
    public string userToken = string.Empty;

    public Dictionary<string, string> OnnxTargetDevices = new();
    public Dictionary<string, string> deviceCustomNames = new();

    [ConfigFieldCategory("lang.kitopia.device_sharing")]
    [ConfigField("lang.kitopia.device_display_name", "lang.kitopia.leave_empty_to_use_the_computer_name", 0xf45f, ConfigFieldType.字符串)]
    public string deviceBroadcastName = string.Empty;
    public string devicePersistentId = string.Empty;
    public string devicePrivateKey = string.Empty;

    public bool EnsureDeviceIdentity()
    {
        devicePersistentId = devicePersistentId?.Trim() ?? string.Empty;
        devicePrivateKey = devicePrivateKey?.Trim() ?? string.Empty;

        var changed = false;

        if (!DeviceDiscoverySignature.TryDerivePublicKey(devicePrivateKey, out var publicKey))
        {
            var keyPair = DeviceDiscoverySignature.CreateKeyPair();
            devicePrivateKey = keyPair.PrivateKey;
            publicKey = keyPair.PublicKey;
            changed = true;
        }

        if (!string.Equals(devicePersistentId, publicKey, StringComparison.Ordinal))
        {
            devicePersistentId = publicKey;
            changed = true;
        }

        return changed;
    }

    [ConfigFieldCategory("lang.kitopia.general")] [ConfigField<ThemeEnum>("lang.kitopia.theme", "lang.kitopia.system_default_dark_or_light", 0xf33c)]
    public ThemeEnum themeChoice = ThemeEnum.跟随系统;

    // Empty means follow the operating system's UI language.
    public string language = string.Empty;

    [ConfigField("lang.kitopia.use_system_accent_color", "lang.kitopia.use_the_accent_color_from_windows_personalization", 0xf33c, ConfigFieldType.布尔)]
    public bool followSystemAccentColor = false;

    [ConfigField("lang.kitopia.accent_color", "", 0xf33c, ConfigFieldType.颜色,
        VisibleWhen = nameof(followSystemAccentColor), VisibleWhenValue = false)]
    public string accentColor = "#0064FA";

    [ConfigField("lang.kitopia.start_automatically", "lang.kitopia.may_be_blocked_by_antivirus_software", 0xE61C, ConfigFieldType.布尔)]
    public bool autoStart = true;


    [ConfigField("lang.kitopia.allow_clipboard_access", "lang.kitopia.required_for_reading_clipboard_paths_and_saving_clipboard_images", 0xF2D7, ConfigFieldType.布尔)]
    public bool canReadClipboard = true;
    [ConfigFieldCategory("lang.kitopia.windows_enhancements")]
    [ConfigField("lang.kitopia.always_on_top_hotkey", "lang.kitopia.always_on_top_hotkey", 0xf602, ConfigFieldType.快捷键, actionName: "topMostWindowHotKeyAction")]
    public HotKeyModel topMostWindowHotKey = new()
    {
        IsEnabled = true,
        MainName = "Kitopia", Name = "置顶窗口快捷键", IsSelectCtrl = true, IsSelectAlt = true,
        IsSelectWin = false,
        IsSelectShift = false, SelectKey = EKey.T
    };
    [ConfigField("lang.kitopia.check_companion_installation", "lang.kitopia.the_companion_provides_windows_explorer_context_menu_integration", 0xE61C, ConfigFieldType.布尔)]
    public bool checkKitopiaCompanion = true;
    [ConfigFieldCategory("lang.kitopia.search_window")]
    [ConfigField("lang.kitopia.search_window_hotkey", "lang.kitopia.hotkey_to_show_the_search_window", 0xF4B8, ConfigFieldType.快捷键, actionName: "searchHotKeyAction")]
    public HotKeyModel searchHotKey = new()
    {
        IsEnabled = true,
        MainName = "Kitopia", Name = "显示搜索框", IsSelectCtrl = false, IsSelectAlt = true,
        IsSelectWin = false,
        IsSelectShift = false, SelectKey = EKey.空格
    };

    public Dictionary<string, HistoryItem> lastOpens = new();

    [ConfigField("lang.kitopia.history_limit", "lang.kitopia.maximum_history_entries", 0xF2D7, ConfigFieldType.整数列表, null, 10, 1, 1)]
    public int maxHistory = 6;
    [ConfigField("lang.kitopia.use_everything_to_index_documents", "lang.kitopia.required_for_document_indexing", 0xF3AE, ConfigFieldType.布尔)]
    public bool useEverything = true;

    [ConfigField("lang.kitopia.start_everything_automatically", "lang.kitopia.start_everything_if_it_is_not_running", 0xE61C, ConfigFieldType.布尔)]
    public bool autoStartEverything = true;

    [ConfigField("lang.kitopia.everything_indexed_file_types", "lang.kitopia.add_matching_files_found_by_everything_to_the_local_index_does_not_limit_live_searches", 0xf8cb, ConfigFieldType.字符串列表支持添加)]
    public ObservableCollection<string> everythingSearchExtensions =
        ["*.docx", "*.doc", "*.xls", "*.xlsx", "*.pdf", "*.ppt", "*.pptx"];

    [ConfigField("lang.kitopia.everything_search_prefix", "lang.kitopia.search_everything_when_the_query_starts_with_this_prefix", 0xf8cb, ConfigFieldType.字符串)]
    public string everythingSearchPreString = "@";

    [ConfigField("lang.kitopia.everything_result_limit", "lang.kitopia.maximum_files_returned_by_everything", 0xf8cb, ConfigFieldType.整数, null, 1000, 5, 5)]
    public int everythingSearchMaxCount = 50;

    [ConfigFieldCategory("lang.kitopia.semantic_search")]
    [ConfigField("lang.kitopia.enable_local_semantic_search", "lang.kitopia.use_the_built_in_chinese_semantic_model_and_pinyin_search_to_improve_relevance", 0xf3ae,
        (ConfigFieldType)5)]
    public bool enableSemanticSearch = true;

    [ConfigField("lang.kitopia.semantic_search_delay", "lang.kitopia.delay_after_typing_before_semantic_matching_starts_lower_values_respond_faster", 0xf8cb,
        (ConfigFieldType)1, null, 1000, 100, 10)]
    public int semanticSearchDebounceMilliseconds = 300;

    [ConfigField("lang.kitopia.semantic_result_limit", "lang.kitopia.maximum_candidates_returned_by_the_local_semantic_index", 0xf8cb,
        (ConfigFieldType)1, null, 100, 5, 5)]
    public int semanticSearchMaxResults = 50;

    [ConfigField("lang.kitopia.semantic_text_extensions", "lang.kitopia.extract_and_index_only_these_plain_text_extensions_e_g_md", 0xf8cb, ConfigFieldType.字符串列表支持添加)]
    public ObservableCollection<string> plainTextExtensions = [".md"];


    public List<PluginBaseInfo> EnabledPluginInfos = new()
    {
        new PluginBaseInfo
        {
            Id = 7,
            AuthorName = "Kitopia",
            AuthorId = 1,
            NameSign = "kitopiaex"
        },
        new PluginBaseInfo
        {
            Id = 2,
            AuthorName = "Kitopia",
            AuthorId = 1,
            NameSign = "kitopiaonnxruntimecpu"
        }
    };
    public List<string> errorLnk = new();
    public string everythingOnlyKey = "";

    [ConfigFieldCategory("lang.kitopia.indexing")]
    [ConfigField("lang.kitopia.indexing_cpu_limit", "lang.kitopia.limit_logical_processors_used_for_indexing_100_means_unlimited_windows_only", 0xf8cb, ConfigFieldType.整数, null, 100, 5, 5)]
    public int indexingMaximumCpuUsagePercent = 50;

    [ConfigField("lang.kitopia.excluded_folder_names", "lang.kitopia.skip_paths_containing_these_folder_names_such_as_cache_folders", 0xF2D7, ConfigFieldType.字符串列表支持添加)]
    public ObservableCollection<string> transientDirectoryNames =
        new(DefaultTransientDirectoryNames);

    [ConfigField("lang.kitopia.allowed_file_extensions", "lang.kitopia.include_these_extensions_when_scanning_folders_does_not_limit_everything_or_manually_added_files_use_pdf_pdf_or_for_all_extensions", 0xF2D7, ConfigFieldType.字符串列表支持添加)]
    public ObservableCollection<string> allowedFileExtensions =
        new(DefaultAllowedFileExtensions);

    [ConfigField("lang.kitopia.indexed_folders", "lang.kitopia.pre_index_files_in_these_folders_for_local_search", 0xF2D7, ConfigFieldType.目录列表)]
    public ObservableCollection<string> managedIndexDirectories = new();

    [ConfigField("lang.kitopia.indexed_files", "lang.kitopia.pre_index_these_files_for_local_search", 0xF2D7, ConfigFieldType.文件列表)]
    public ObservableCollection<string> managedIndexFiles = new();

    [ConfigField("lang.kitopia.excluded_items", "lang.kitopia.exclude_selected_files_or_folders", 0xF2D7, ConfigFieldType.文件和目录列表)]
    public ObservableCollection<string> ignoreItems = new();


    [ConfigFieldCategory("lang.kitopia.file_preview")] [ConfigField("lang.kitopia.capture_mouse_input", "lang.kitopia.required_for_mouse_hotkeys", 0xE61C, ConfigFieldType.布尔)]
    public bool mouseCapture = false;

    [ConfigField("lang.kitopia.file_preview_hotkey", "lang.kitopia.preview_files_selected_in_explorer_using_a_keyboard_or_mouse_hotkey", 0xF4B8, ConfigFieldType.快捷键, actionName: "mouseHotkeyAction")]
    public HotKeyModel mouseHotkey = new()
    {
        IsEnabled = true,
        MainName = "Kitopia", Name = "文件速览", IsSelectCtrl = false, IsSelectAlt = false,
        Type = HotKeyType.Keyboard,
        ProcessScope = HotKeyProcessScope.Include,
        ProcessNames = ["explorer.exe"],
        IgnoreTextInput = true,
        MouseButton = 1,
        PressTimeMillis = 1500,
        IsSelectWin = false,
        IsSelectShift = false, SelectKey = EKey.空格
    };


    public List<string> mouseQuickItems = new();


    [ConfigFieldCategory("lang.kitopia.screenshot")]
    [ConfigField("lang.kitopia.copy_screenshots_directly_to_clipboard", "lang.kitopia.copy_screenshots_without_displaying_the_toolbar", 0xE61C, ConfigFieldType.布尔)]
    public bool 截图直接复制到剪贴板 = false;
    [ConfigField("lang.kitopia.screenshot_mosaic_blur", "lang.kitopia.higher_values_produce_stronger_blur", 0xE61C, ConfigFieldType.整数, null, 15, 1, 1)]
    public int GaussianBlurRadius = 6;
    [ConfigField("lang.kitopia.capture_method", "lang.kitopia.try_another_capture_method_if_screenshots_fail", 0xE61C, ConfigFieldType.自定义选项, actionName: "截图方法列表")]
    public string 截图方法 = "WGC";

    [ConfigField("lang.kitopia.screenshot_hotkey", "lang.kitopia.edit_screenshot_hotkey", 0xF4B8, ConfigFieldType.快捷键, actionName: "screenShotHotKeyAction")]
    public HotKeyModel screenShotHotKey = new()
    {
        IsEnabled = true,
        MainName = "Kitopia", Name = "截图", IsSelectCtrl = true, IsSelectAlt = true,
        IsSelectWin = false,
        IsSelectShift = false, SelectKey = EKey.Q
    };

    [ConfigFieldCategory("lang.kitopia.more")]
    [ConfigField("lang.kitopia.create_shortcuts_on_update", "lang.kitopia.create_desktop_and_start_menu_shortcuts_when_updating", 0xE61C, ConfigFieldType.布尔)]
    public bool createShortcutsOnUpdate = false;

    [ConfigField("lang.kitopia.check_for_updates", "lang.kitopia.check_for_updates_now", 0xE974, ConfigFieldType.按钮,actionName: "检查更新")]
    public async Task CheckUpdate()
    {
        await ServiceManager.Services.GetService<IApplicationService>()!.CheckUpdate(true);
    }

#if DEBUG
    [ConfigFieldCategory("lang.kitopia.settings.developer")]
    [ConfigField("lang.kitopia.settings.local_server", "lang.kitopia.settings.local_server_description", 0xf226, ConfigFieldType.布尔)]
    public bool useLocalhostDebug = false;
#endif
    
    public override void BeforeLoad()
    {
        invokes.Add("screenShotHotKeyAction", new Action<HotKeyModel>(e =>
        {
            Logger.Debug("截图热键被触发");

            Dispatcher.UIThread.InvokeAsync(() =>
                {
                    ServiceManager.Services.GetService<IScreenCaptureWindow>()!.CaptureScreen();
                })
                .GetTask()
                .ContinueWith((e) =>
                {
                    if (e.IsFaulted)
                    {
                        Logger.Error(e.Exception, "");
                        var toastService = ServiceManager.Services.GetService<IToastService>();
                        if (toastService is not null)
                        {
                            _ = toastService.Show(Lang.Get("lang.kitopia.screenshot_failed"), e.Exception.Message + e.Exception.StackTrace,
                                NotificationType.Error);
                        }
                    }
                });
        }));
        invokes.Add("mouseHotkeyAction", new Action<HotKeyModel>(e =>
        {
            Logger.Debug("文件速览快捷键触发");
            ServiceManager.Services.GetService<IMouseQuickWindowService>()!.Open();
        }));
        invokes.Add("searchHotKeyAction", new Action<HotKeyModel>(e =>
        {
            Logger.Debug("显示搜索框热键被触发");
            ServiceManager.Services.GetService<ISearchWindowService>()!.ShowOrHiddenSearchWindow();
        }));
        invokes.Add("topMostWindowHotKeyAction", new Action<HotKeyModel>(e =>
        {
            Logger.Debug("置顶窗口热键被触发");
            ServiceManager.Services.GetService<IWindowTool>()!.SelectAndSetWindowTopMost();
        }));
        invokes.Add("截图方法列表",
            new Func<IEnumerable<string>>(() =>
            {
                return ServiceManager.Services.GetService<IScreenCaptureManager>()!.GetCaptureMethodName();
            }));
    }
}
