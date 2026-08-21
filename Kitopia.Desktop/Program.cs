using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ReactiveUI;
using Kitopia.Desktop.Features.CustomScenario;
using Kitopia.Desktop.Features.Services;
using Kitopia.Desktop.Features.Services.Config;
using Kitopia.Feature.DeviceCommunication.Discovery;
using Kitopia.Desktop.Features.Services.Interfaces;
using Kitopia.Desktop.Features.Services.MQTT;
using Kitopia.Desktop.Features.Services.Onnx;
using Kitopia.Desktop.Features.Services.Plugin;
using Kitopia.Desktop.Features.Utils;
using Kitopia.Desktop.Features.ViewModel.Main;
using Kitopia.Desktop.Features.ViewModel.Pages;
using Kitopia.Desktop.Features.ViewModel.Pages.plugin;
using Kitopia.Desktop.Features.ViewModel.Windows;
using Kitopia.Desktop.Ocr;
#if WINDOWS
using Kitopia.Desktop.Platform.Windows;
using Kitopia.Desktop.Platform.Windows.AppTools;
using Kitopia.Desktop.Platform.Windows.Everything;
using Kitopia.Desktop.Platform.Windows.ScreenCapture;
using Kitopia.Desktop.Platform.Windows.Services;
#endif
#if LINUX
using Kitopia.Desktop.Platform.Linux;
#endif
using Kitopia.Desktop.Abstractions;
using Kitopia.Desktop.Abstractions.FileSystem;
using Kitopia.Desktop.Abstractions.Shell;
using Kitopia.Feature.Avalonia.DeviceCommunication.ViewModels;
using Kitopia.Feature.Avalonia.DeviceCommunication.Views;
using Kitopia.Desktop.Features.CustomScenario.Services;
using Kitopia.Desktop.Features.CustomScenario.ViewModels;
using Kitopia.Desktop.Features.CustomScenario.ViewModels.TaskEditor;
using Kitopia.Desktop.Features.PluginHost;
using Kitopia.Desktop.Features.Search;
using Kitopia.Desktop.Features.Search.Services;
using Kitopia.Desktop.Features.Search.ViewModels;
using Kitopia.Desktop.Features.Indexing;
using Kitopia.Desktop.Features.Ocr;
using DesktopOcrService = Kitopia.Desktop.Features.Ocr.IOcrService;
using Kitopia.Desktop.Pages;
using Kitopia.Desktop.Services;
using Kitopia.Desktop.Windows;
using Microsoft.Extensions.DependencyInjection;
using Kitopia.Feature.DeviceCommunication.Identity;
using PluginCore;
using PluginCore.Onnx;
using Serilog;
using ScreenCaptureWindow = Kitopia.Desktop.Services.ScreenCaptureWindow;
using SharedApplication = Kitopia.Feature.DeviceCommunication.Application;
using SharedCodecs = Kitopia.Feature.DeviceCommunication.Codecs;
using SharedDeviceCommunication = Kitopia.Feature.DeviceCommunication;
using SharedMessageAppService = Kitopia.Feature.DeviceCommunication.Application.IMessageAppService;
using SharedProtocol = Kitopia.Feature.DeviceCommunication.Protocol;
using SharedSecurity = Kitopia.Feature.DeviceCommunication.Security;
using SharedSessions = Kitopia.Feature.DeviceCommunication.Sessions;
using SharedTransport = Kitopia.Feature.DeviceCommunication.Transport;
using TaskEditor = Kitopia.Desktop.Windows.TaskEditors.TaskEditor;

namespace Kitopia.Desktop;

internal class Program {
    private static readonly ILogger Logger = LogManager.Logger.ForContext<Program>();

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args) {
        ReactiveUI.Builder.RxAppBuilder.CreateReactiveUIBuilder()
            .WithExceptionHandler(new MyCoolObservableExceptionHandler())
            .WithPlatformServices()
            .BuildApp();
        ServiceManager.Services = ConfigureServices();
        PluginOverall.ScreenCaptureExMethods["Kitopia"] =
        [
            new ScreenCaptureExMethod
            {
                Description = "文字提取",
                Symbol = 0xea72,
                Action = OcrResultShowWindow.ShowForCapture
            }
        ];
        try {
            TaskScheduler.UnobservedTaskException += (_, eventArgs) => { Logger.Error(eventArgs.Exception, "错误"); };

            AppDomain.CurrentDomain.UnhandledException += (_, e) => {
                Logger.Fatal((Exception)e.ExceptionObject, "错误");
            };

            if (MqttManager.Init(args).GetAwaiter().GetResult()) {
                ExitApplication();
                return;
            }

            Task.Run(async () => {
                while (Application.Current is null) await Task.Delay(100);
                try {
                    OnStartup(args);
                }
                catch (Exception e) {
                    Logger.Fatal(e, "启动失败");
                    ExitApplication();
                }
            });
            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }
        catch (Exception e) {
            Logger.Fatal(e, "");
            ExitApplication();
        }
    }

    private static void ExitApplication(int exitCode = 0) {
        ServiceManager.Services.GetService<IApplicationService>()!.ExitAsync(exitCode).GetAwaiter().GetResult();
    }

    [MemberNotNull]
    private static IServiceProvider ConfigureServices() {
        var services = new ServiceCollection();
        services.AddDesktopPluginHost();
        services.AddSingleton<IToastService, ToastService>();
        services.AddSingleton<Kitopia.Feature.DeviceCommunication.Application.IChatNotificationSink,
            DesktopChatNotificationSink>();
        services.AddTransient<IHotKeyEditor, HotKeyEditorService>();
        services.AddSingleton<ITaskEditorOpenService, TaskEditorOpenService>();
        services.AddTransient<IThemeChange, ThemeChange>();

        services.AddSingleton<ISearchItemChooseService, SearchItemChooseService>();
        services.AddSingleton<IFeatureFilePicker, DesktopFeatureFilePicker>();
        services.AddSingleton<IMouseQuickWindowService, MouseQuickWindowService>();
        services.AddTransient<ISearchWindowService, SearchWindowService>();
        services.AddTransient<IScreenCaptureWindow, ScreenCaptureWindow>();

        services.AddSingleton<IConfigService, ConfigManger>();
        services.AddSingleton<IDeviceIdentityStore, DesktopDeviceIdentityStore>();
        services.AddSingleton<Kitopia.Feature.DeviceCommunication.Codecs.IDeviceIdentityProvider,
            Kitopia.Feature.DeviceCommunication.Identity.DeviceIdentityProvider>();
        services.AddSingleton<Kitopia.Feature.DeviceCommunication.Discovery.IDeviceCommunicationSettings, DesktopDeviceCommunicationSettings>();
        services.AddSingleton<SharedTransport.ILocalDataEndpointProvider, DesktopLocalDataEndpointProvider>();
        services.AddSingleton<IDeviceDiscoveryService, DeviceDiscoveryService>();
        services.AddSingleton<SharedTransport.IRemoteIdentityResolver, DesktopRemoteIdentityResolver>();
        services.AddSingleton<SharedSecurity.DeviceTransportSecurity>();
        services.AddSingleton<SharedSessions.IFileTransferSessionStore, SharedSessions.FileTransferSessionStore>();
        services.AddSingleton<SharedCodecs.MessageCodecRegistry>();
        services.AddSingleton<SharedApplication.IncomingMessageBuffer>();
        services.AddSingleton<SharedApplication.IIncomingMessageSink, DesktopIncomingMessageSink>();
        services.AddSingleton<SharedApplication.FileTransferPayloadHandler>();
        services.AddSingleton<SharedDeviceCommunication.DeviceMessageDispatcher>();
        services.AddSingleton<SharedProtocol.ProtocolSession>(serviceProvider =>
            new SharedProtocol.ProtocolSession(
                serviceProvider.GetRequiredService<SharedDeviceCommunication.DeviceMessageDispatcher>().DispatchAsync));
        services.AddSingleton<SharedTransport.ILocalDataListener, SharedTransport.LocalDataListenerHost>();
        services.AddSingleton<SharedDeviceCommunication.DeviceTransportService>();
        services.AddSingleton<SharedMessageAppService, SharedApplication.MessageAppService>();
        services.AddSingleton<SharedDeviceCommunication.IDeviceCommunicationRuntime,
            SharedDeviceCommunication.DeviceCommunicationRuntime>();


        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<DesktopOcrService, PaddleOcrService>();
        services.AddSingleton<PluginCore.IOcrService>(e => e.GetRequiredService<DesktopOcrService>());
        services.AddSingleton<IIndexService, IndexService>();
        services.AddSingleton<IIndexMaintenanceService, IndexMaintenanceService>();
        services.AddTransient<IInferenceSessionManager, InferenceSessionManager>();
        #if WINDOWS
        services.AddTransient<IHotKetImpl, HotKeyImpl>();
        services.AddTransient<IScreenCaptureManager, ScreenCaptureManager>();
        services.AddTransient<IScreenCapture, ScreenCaptureByWgc>();

        services.AddTransient<IEverythingService, EverythingService>();
        services.AddTransient<IAppToolService, AppToolService>();
        services.AddSingleton<ISearchItemTool, SearchItemTool>();
        services.AddSingleton<IDesktopPlatformInfo, WindowsDesktopPlatformInfo>();
        services.AddSingleton<IDesktopShell, ShellUtils>();
        services.AddTransient<IClipboardService, ClipboardWindow>();
        services.AddTransient<Kitopia.Feature.DeviceCommunication.Application.IChatClipboardService,
            DesktopChatClipboardService>();
        services.AddSingleton<IWindowTool, WindowToolServiceWindow>();
        services.AddTransient<IApplicationService, ApplicationService>();
        services.AddTransient<IImageTool, ImageTool>();
        services.AddTransient<IExplorerContextMenuService, ExplorerContextMenuService>();
        services.AddTransient<IExplorerContextMenuConfiger, ExplorerContextMenuConfiger>();
        services.AddTransient<IFileLockService, FileLocksmithService>();
        services.AddTransient<ILanFileShareWindow, LanFileShareWindow>(_ => {
            return Dispatcher.UIThread.Invoke(() => new LanFileShareWindow());
        });
        #endif

        #if LINUX
        services.AddTransient<IAppToolService, AppToolLinuxService>();
        services.AddSingleton<ISearchItemTool, SearchItemTool>();
        services.AddSingleton<IDesktopPlatformInfo, LinuxDesktopPlatformInfo>();
        services.AddSingleton<IDesktopShell, LinuxDesktopShell>();
        services.AddTransient<IFileLockService, LinuxFileLockService>();

        #endif

        services.AddTransient<IFileLocksmithWindow, FileLocksmithWindow>(_ => {
            return Dispatcher.UIThread.Invoke((() => new FileLocksmithWindow()));
        });


        services.AddTransient<TaskEditorViewModel>();
        services.AddTransient<TaskEditor>(e => new TaskEditor { DataContext = e.GetService<TaskEditorViewModel>() });
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainWindow>(e => new MainWindow { DataContext = e.GetService<MainWindowViewModel>() });
        services.AddSingleton<SearchWindowViewModel>(e => new SearchWindowViewModel { IsActive = true });
        services.AddSingleton<ISearchFeatureService>(e => e.GetRequiredService<SearchWindowViewModel>());
        services.AddSingleton<SearchWindow>(e => new SearchWindow
            { DataContext = e.GetService<SearchWindowViewModel>() });
        services.AddTransient<MouseQuickWindowViewModel>();
        services.AddTransient<MouseQuickWindow>(e => new MouseQuickWindow
            { DataContext = e.GetService<MouseQuickWindowViewModel>() });
        services.AddSingleton<HomePageViewModel>(e => new HomePageViewModel { IsActive = true });
        services.AddKeyedSingleton<UserControl, HomePage>("HomePage",
            (e, _) => new HomePage { DataContext = e.GetService<HomePageViewModel>() });
        services.AddSingleton<CustomScenariosManagerPageViewModel>(e => new CustomScenariosManagerPageViewModel
            { IsActive = true });
        services.AddKeyedSingleton<UserControl, CustomScenariosManagerPage>("CustomScenariosManagerPage",
            (e, _) => new CustomScenariosManagerPage
                { DataContext = e.GetService<CustomScenariosManagerPageViewModel>() });
        services.AddSingleton<HotKeyManagerPageViewModel>(e => new HotKeyManagerPageViewModel());
        services.AddKeyedSingleton<UserControl, HotKeyManagerPage>("HotKeyManagerPage",
            (e, _) => new HotKeyManagerPage { DataContext = e.GetService<HotKeyManagerPageViewModel>() });
        services.AddKeyedTransient<UserControl, PluginManagerPage>("PluginManagerPage",
            (e, _) => new PluginManagerPage { DataContext = e.GetService<PluginManagerPageViewModel>() });
        services.AddKeyedSingleton<UserControl, PluginSettingSelectPage>("PluginSettingSelectPage",
            (e, _) => new PluginSettingSelectPage { DataContext = e.GetService<PluginSettingViewModel>() });
        services.AddKeyedTransient<UserControl, MarketPage>("MarketPage",
            (e, _) => new MarketPage { DataContext = e.GetService<MarketPageViewModel>() });
        services.AddTransient<OnnxModelManagerPageViewModel>();
        services.AddKeyedTransient<UserControl, OnnxModelManagerPage>("OnnxModelManagerPage",
            (e, _) => new OnnxModelManagerPage { DataContext = e.GetService<OnnxModelManagerPageViewModel>() });
        services.AddTransient<IndexStatusPageViewModel>();
        services.AddKeyedTransient<UserControl, IndexStatusPage>("IndexStatusPage",
            (e, _) => new IndexStatusPage { DataContext = e.GetRequiredService<IndexStatusPageViewModel>() });
        services.AddSingleton<Kitopia.Feature.DeviceCommunication.Application.IChatAttachmentStore,
            DesktopChatAttachmentStore>();
        services.AddSingleton<Kitopia.Feature.DeviceCommunication.Application.IChatPlatformService,
            DesktopChatPlatformService>();
        services.AddTransient<DeviceCommunicationPageViewModel>(e => new DeviceCommunicationPageViewModel(
            e.GetRequiredService<IDeviceDiscoveryService>(),
            e.GetRequiredService<SharedMessageAppService>(),
            e.GetRequiredService<Kitopia.Feature.DeviceCommunication.Application.IChatAttachmentStore>(),
            e.GetRequiredService<Kitopia.Feature.DeviceCommunication.Application.IChatPlatformService>(),
            e.GetRequiredService<Kitopia.Feature.DeviceCommunication.Discovery.IDeviceCommunicationSettings>(),
            e.GetRequiredService<Kitopia.Feature.DeviceCommunication.Application.IChatNotificationSink>(),
            e.GetService<Kitopia.Feature.DeviceCommunication.Application.IChatClipboardService>(),
            autoSelectFirstConversation: true));
        services.AddKeyedTransient<UserControl, DeviceCommunicationPage>("DeviceChatPage",
            (e, _) => new DeviceCommunicationPage { DataContext = e.GetService<DeviceCommunicationPageViewModel>() });


        services.AddSingleton<SettingPage>(e => new SettingPage());
        #if WINDOWS
        services.AddSingleton<GitHubUpdateService>();
        #endif

        return services.BuildServiceProvider();
    }

    private static void CheckAndDeleteLogFiles() {
        // 定义日志文件的目录
        var logDirectory = KitopiaPaths.LogsDirectory;
        Logger.Debug($"检查日志目录:{logDirectory}");
        // 定义要保留的日志文件的时间范围，这里是一周
        var timeSpan = TimeSpan.FromDays(2);

        // 获取当前的日期
        var currentDate = DateTime.Today;

        // 获取目录下的所有日志文件，按照最后修改时间排序
        try {
            var logFiles = Directory.EnumerateFiles(logDirectory)
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.LastWriteTime);

            // 遍历每个日志文件
            foreach (var logFile in logFiles)
                // 计算日志文件的最后修改时间和当前日期的差值
                // 如果差值大于要保留的时间范围，就删除该日志文件
                if (currentDate - logFile.LastWriteTime > timeSpan) {
                    Logger.Debug($"删除日志文件:{logFile.FullName}");
                    logFile.Delete();
                }
        }
        catch (Exception e) {
            // ignored
        }
    }

    private static async Task<bool> CheckUpdates(bool toastIfNoUpdate = false) {
        return await ServiceManager.Services.GetService<IApplicationService>()!.CheckUpdate(toastIfNoUpdate);
    }

    private static void OnStartup(string[] arg) {
        Logger.Information("启动");
        CheckAndDeleteLogFiles();
        ServiceManager.Services.GetService<IToastService>()!.Init();

        Task.Run((async () => {
            while (true) {
                if (!await CheckUpdates()) {
                    return;
                }

                await Task.Delay(TimeSpan.FromMinutes(30));
            }
        }));


        ServiceManager.Services.GetService<IHotKetImpl>()!.Init();
        Logger.Debug("注册热键管理器完成");
        ConfigManger.Init();
        Logger.Information("配置文件初始化完成");
        PluginOverall.InitializeContextMenu();
        ServiceManager.Services.GetService<IHotKetImpl>()!.StartHook();

        MqttManager.ProcessLocalArgs(arg).GetAwaiter().GetResult();
        if (ConfigManger.Config.checkKitopiaCompanion) {
            if (ServiceManager.Services.GetService<IExplorerContextMenuService>()!.RegisterAsync()
                .GetAwaiter()
                .GetResult()) {
                Logger.Information("资源管理器右键菜单注册完成");
            }
            else {
                Logger.Warning("资源管理器右键菜单注册失败");
            }
        }

        switch (ConfigManger.Config.themeChoice) {
            case ThemeEnum.跟随系统: {
                ServiceManager.Services.GetService<IThemeChange>()!
                    .followSys(true);
                break;
            }
            case ThemeEnum.深色: {
                ServiceManager.Services.GetService<IThemeChange>()!
                    .followSys(false);
                ServiceManager.Services.GetService<IThemeChange>()!
                    .changeTo("theme_dark");
                break;
            }
            case ThemeEnum.浅色: {
                ServiceManager.Services.GetService<IThemeChange>()!
                    .followSys(false);
                ServiceManager.Services.GetService<IThemeChange>()!
                    .changeTo("theme_light");
                break;
            }
        }

        Logger.Information("主题初始化完成");

        PluginManager.Init();
        Logger.Information("插件管理器初始化完成");
        CustomScenarioManger.Init();
        Logger.Information("场景管理器初始化完成");
        ServiceManager.Services.GetService<SharedMessageAppService>();
        ServiceManager.Services.GetService<SharedDeviceCommunication.IDeviceCommunicationRuntime>()!
            .StartAsync().GetAwaiter().GetResult();

        if (ConfigManger.Config.autoStart) {
            Logger.Information("设置开机自启");
            ServiceManager.Services.GetService<IApplicationService>()!
                .ChangeAutoStart(true);
        }

        ServiceManager.Services.GetService<IApplicationService>()!.Init();
        _ = Task.Run(async () =>
        {
            try
            {
                await ServiceManager.Services.GetRequiredService<IIndexMaintenanceService>().InitializeAsync();
            }
            catch (Exception exception)
            {
                Logger.Warning(exception, "Unified index initialization failed; the search UI remains available.");
            }
            finally
            {
                Dispatcher.UIThread.Post(() => { ServiceManager.Services.GetService<SearchWindowViewModel>(); });
            }
        });
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp() {
        var buildAvaloniaApp = AppBuilder.Configure<App>();
        buildAvaloniaApp.UsePlatformDetect();
        buildAvaloniaApp.With(new FontManagerOptions {
            DefaultFamilyName = "avares://Kitopia.Desktop/Assets/HarmonyOS_Sans_SC_Regular.ttf#HarmonyOS Sans",
            FontFallbacks = [
                new FontFallback {
                    FontFamily =
                        new FontFamily("avares://Kitopia.Desktop/Assets/HarmonyOS_Sans_SC_Regular.ttf#HarmonyOS Sans")
                }
            ]
        });
        buildAvaloniaApp.With(new RenderOptions {
            EdgeMode = EdgeMode.Antialias,
            BitmapInterpolationMode = BitmapInterpolationMode.HighQuality
        });
        buildAvaloniaApp.With(new TextOptions() {
            TextRenderingMode = TextRenderingMode.Antialias,
        });
        buildAvaloniaApp.LogToTrace();
        #if DEBUG
        buildAvaloniaApp.WithDeveloperTools();
        #endif
        return buildAvaloniaApp;
    }
}
