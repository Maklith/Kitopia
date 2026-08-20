using System.Collections.ObjectModel;
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
    跟随系统,
    深色,
    浅色
}
[ConfigName("Kitopia主配置文件")]
public class KitopiaConfig : ConfigBase
{
    private static ILogger Logger = LogManager.Logger.ForContext<KitopiaConfig>();
    internal static readonly IReadOnlyList<string> DefaultTransientDirectoryNames =
    [
        "temp", "tmp", "temporary", "cache", "caches", "inetcache", "temporary internet files",
        ".minecraft", "assets", "data", "tdata", "logs","node_modules"
    ];
    internal static readonly IReadOnlyList<string> DefaultAllowedFileExtensions =
    [
        ".md", ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx",
        ".jpg", ".jpeg", ".png", ".bmp", ".webp"
    ];

    public List<string> alwayShows = new();

    public Dictionary<string, string> OnnxTargetDevices = new();
    public Dictionary<string, string> deviceCustomNames = new();

    [ConfigFieldCategory("设备互传")]
    [ConfigField("对外显示名称", "为空时使用当前计算机名称", 0xf45f, ConfigFieldType.字符串)]
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

    [ConfigFieldCategory("基本")] [ConfigField<ThemeEnum>("主题选择", "跟随系统,深色还是浅色?", 0xf33c)]
    public ThemeEnum themeChoice = ThemeEnum.跟随系统;

    [ConfigField("自动启动", "可能被杀毒软件阻止", 0xE61C, ConfigFieldType.布尔)]
    public bool autoStart = true;


    [ConfigField("允许程序读取剪贴板", "自动读取剪贴板路径和剪贴板图像保存依赖于此权限", 0xF2D7, ConfigFieldType.布尔)]
    public bool canReadClipboard = true;
    [ConfigFieldCategory("Windows增强")]
    [ConfigField("置顶窗口快捷键", "置顶窗口快捷键", 0xf602, ConfigFieldType.快捷键, actionName: "topMostWindowHotKeyAction")]
    public HotKeyModel topMostWindowHotKey = new()
    {
        IsEnabled = true,
        MainName = "Kitopia", Name = "置顶窗口快捷键", IsSelectCtrl = true, IsSelectAlt = true,
        IsSelectWin = false,
        IsSelectShift = false, SelectKey = EKey.T
    };
    [ConfigField("检查Kitopia伴侣程序是否安装", "Kitopia伴侣程序用于拓展Windows资源管理器右键菜单拓展", 0xE61C, ConfigFieldType.布尔)]
    public bool checkKitopiaCompanion = true;
    [ConfigFieldCategory("搜索框")]
    [ConfigField("搜索框快捷键", "显示搜索框快捷键", 0xF4B8, ConfigFieldType.快捷键, actionName: "searchHotKeyAction")]
    public HotKeyModel searchHotKey = new()
    {
        IsEnabled = true,
        MainName = "Kitopia", Name = "显示搜索框", IsSelectCtrl = false, IsSelectAlt = true,
        IsSelectWin = false,
        IsSelectShift = false, SelectKey = EKey.空格
    };

    public Dictionary<string, HistoryItem> lastOpens = new();

    [ConfigField("最大历史记录", "最大历史记录数", 0xF2D7, ConfigFieldType.整数列表, null, 10, 1, 1)]
    public int maxHistory = 6;
    [ConfigField("允许程序调用Everything索引文档", "索引文档依赖于此功能", 0xF3AE, ConfigFieldType.布尔)]
    public bool useEverything = true;

    [ConfigField("自动启动Everything", "在Everything未启动时自动启动", 0xE61C, ConfigFieldType.布尔)]
    public bool autoStartEverything = true;

    [ConfigField("Everything 自动纳入索引的文件类型", "Everything 发现匹配扩展名的全盘文件后，将其加入普通预索引；不限制 @ 实时搜索", 0xf8cb, ConfigFieldType.字符串列表支持添加)]
    public ObservableCollection<string> everythingSearchExtensions =
        ["*.docx", "*.doc", "*.xls", "*.xlsx", "*.pdf", "*.ppt", "*.pptx"];

    [ConfigField("调用Everything直接搜索文件前缀", "如果搜索内容直接以该前缀开始,直接调用Everything而不是程序内置索引", 0xf8cb, ConfigFieldType.字符串)]
    public string everythingSearchPreString = "@";

    [ConfigField("调用Everything直接搜索文件最大数量", "设置调用Everything直接搜索文件最大数量", 0xf8cb, ConfigFieldType.整数, null, 1000, 5, 5)]
    public int everythingSearchMaxCount = 50;

    [ConfigFieldCategory("语义搜索")]
    [ConfigField("启用本地语义搜索", "使用内置中文语义模型理解搜索意图，并结合拼音搜索提升结果相关性。", 0xf3ae,
        (ConfigFieldType)5)]
    public bool enableSemanticSearch = true;

    [ConfigField("语义搜索响应延迟", "停止输入后，等待多久再开始语义匹配。数值越小，响应越快。", 0xf8cb,
        (ConfigFieldType)1, null, 1000, 100, 10)]
    public int semanticSearchDebounceMilliseconds = 300;

    [ConfigField("语义搜索候选数量", "本地语义索引最多返回的候选结果数量。", 0xf8cb,
        (ConfigFieldType)1, null, 100, 5, 5)]
    public int semanticSearchMaxResults = 50;

    [ConfigField("语义文本扩展名", "仅提取并索引这些纯文本文件扩展名，例如 .md。", 0xf8cb, ConfigFieldType.字符串列表支持添加)]
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

    [ConfigFieldCategory("索引")]
    [ConfigField("索引最大 CPU 使用率", "限制索引过程可使用的逻辑处理器比例。100 表示不限制，仅 Windows 生效。", 0xf8cb, ConfigFieldType.整数, null, 100, 5, 5)]
    public int indexingMaximumCpuUsagePercent = 50;

    [ConfigField("自动索引忽略的目录名称", "路径中包含这些目录名称时不会自动索引，例如缓存目录和 .minecraft。", 0xF2D7, ConfigFieldType.字符串列表支持添加)]
    public ObservableCollection<string> transientDirectoryNames =
        new(DefaultTransientDirectoryNames);

    [ConfigField("自动索引允许的文件扩展名", "自动扫描默认目录和自定义目录时，仅纳入这些扩展名；不限制 Everything 自动发现和手动指定的索引文件。可填写 .pdf、*.pdf 或 *（允许全部扩展名）。", 0xF2D7, ConfigFieldType.字符串列表支持添加)]
    public ObservableCollection<string> allowedFileExtensions =
        new(DefaultAllowedFileExtensions);

    [ConfigField("自定义索引目录", "普通搜索会预先索引这些目录中的文件", 0xF2D7, ConfigFieldType.目录列表)]
    public ObservableCollection<string> managedIndexDirectories = new();

    [ConfigField("自定义索引文件", "普通搜索会预先索引这些文件", 0xF2D7, ConfigFieldType.文件列表)]
    public ObservableCollection<string> managedIndexFiles = new();

    [ConfigField("忽略项", "忽略指定的文件或文件夹", 0xF2D7, ConfigFieldType.文件和目录列表)]
    public ObservableCollection<string> ignoreItems = new();


    [ConfigFieldCategory("鼠标快捷操作")] [ConfigField("允许对鼠标进行捕获", "允许对鼠标进行捕获(禁用后鼠标快捷键无效)", 0xE61C, ConfigFieldType.布尔)]
    public bool mouseCapture = false;

    [ConfigField("鼠标快捷键", "激活鼠标快捷菜单快捷键", 0xF4B8, ConfigFieldType.快捷键, actionName: "mouseHotkeyAction")]
    public HotKeyModel mouseHotkey = new()
    {
        IsEnabled = true,
        MainName = "Kitopia", Name = "激活鼠标快捷菜单", IsSelectCtrl = false, IsSelectAlt = true,
        Type = HotKeyType.Mouse,
        MouseButton = 1,
        PressTimeMillis = 1500,
        IsSelectWin = false,
        IsSelectShift = false, SelectKey = EKey.未设置
    };


    public List<string> mouseQuickItems = new();


    [ConfigFieldCategory("截图")] 
    [ConfigField("截图直接复制到剪贴板", "截图直接复制到剪贴板,不显示工具栏", 0xE61C, ConfigFieldType.布尔)]
    public bool 截图直接复制到剪贴板 = false;
    [ConfigField("截图马赛克模糊度", "数值越大生成的模糊效果越明显", 0xE61C, ConfigFieldType.整数, null, 15, 1, 1)]
    public int GaussianBlurRadius = 6;
    [ConfigField("截图方法", "使用特定的截图方法,某些情况下截图失败请尝试切换", 0xE61C, ConfigFieldType.自定义选项, actionName: "截图方法列表")]
    public string 截图方法 = "WGC";

    [ConfigField("截图快捷键", "修改截图快捷键", 0xF4B8, ConfigFieldType.快捷键, actionName: "screenShotHotKeyAction")]
    public HotKeyModel screenShotHotKey = new()
    {
        IsEnabled = true,
        MainName = "Kitopia", Name = "截图", IsSelectCtrl = true, IsSelectAlt = true,
        IsSelectWin = false,
        IsSelectShift = false, SelectKey = EKey.Q
    };

    [ConfigFieldCategory("更多")]
    [ConfigField("检查更新", "立即检查更新", 0xE974, ConfigFieldType.按钮,actionName: "检查更新")]
    public async Task CheckUpdate()
    {
        await ServiceManager.Services.GetService<IApplicationService>()!.CheckUpdate(true);
    }
    
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
                            _ = toastService.Show("截图失败", e.Exception.Message + e.Exception.StackTrace,
                                NotificationType.Error);
                        }
                    }
                });
        }));
        invokes.Add("mouseHotkeyAction", new Action<HotKeyModel>(e =>
        {
            Logger.Debug("鼠标快捷菜单快捷键触发");
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
