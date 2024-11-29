using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.Graphics.Capture;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Core.SDKs.CustomScenario;
using Core.SDKs.Services;
using Core.SDKs.Services.Config;
using Core.SDKs.Services.MQTT;
using Core.SDKs.Services.Plugin;
using Core.ViewModel;
using Core.ViewModel.Pages;
using Core.ViewModel.Pages.customScenario;
using Core.ViewModel.Pages.plugin;
using Core.ViewModel.TaskEditor;
using Core.Window;
using Core.Window.Everything;
using Kitopia.Services;
using KitopiaAvalonia.Pages;
using KitopiaAvalonia.Services;
using KitopiaAvalonia.Windows;
using log4net;
using log4net.Config;
using PluginCore;
using HotKeyManager = Core.SDKs.HotKey.HotKeyManager;
using ScreenCaptureWindow = KitopiaAvalonia.Services.ScreenCaptureWindow;

namespace KitopiaAvalonia;

class Program
{
    private static readonly ILog log = LogManager.GetLogger(nameof(Program));

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var logConfigStream = assembly.GetManifestResourceStream("KitopiaAvalonia.log4net.config")!;

        XmlConfigurator.Configure(logConfigStream);
        try
        {
            // RxApp.DefaultExceptionHandler = new MyCoolObservableExceptionHandler();
            TaskScheduler.UnobservedTaskException += (sender, eventArgs) => { log.Error("错误",eventArgs.Exception); };

            AppDomain.CurrentDomain.UnhandledException += (sender, e) => { log.Fatal("错误",(Exception)e.ExceptionObject); };
            AppDomain.CurrentDomain.ProcessExit += (sender, e) =>
            {
                log.Info("程序退出");
                ServiceManager.Services.GetService<IToastService>().Unregister();
            };
            Task.Run(async () =>
            {
                while (Application.Current is null)
                {
                    await Task.Delay(100);
                }
                
                OnStartup(args);
            });
            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }
        catch (Exception e)
        {
            log.Fatal(e);
            Environment.Exit(0);
        }
        finally
        {
        }
    }
     [MemberNotNull]
    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IToastService, ToastService>();
        services.AddTransient<IContentDialog, ContentDialogService>();
        services.AddTransient<IHotKeyEditor, HotKeyEditorService>();
        services.AddSingleton<ITaskEditorOpenService, TaskEditorOpenService>();

        services.AddTransient<IThemeChange, ThemeChange>();

        services.AddSingleton<ISearchItemChooseService, SearchItemChooseService>();
        services.AddSingleton<IMouseQuickWindowService, MouseQuickWindowService>();
        services.AddTransient<ISearchWindowService, SearchWindowService>();
        services.AddTransient<IErrorWindow, ErrorWindow>();
        services.AddTransient<IScreenCaptureWindow, ScreenCaptureWindow>();

        services.AddTransient<IPluginToolService, PluginToolService>();

        services.AddTransient<INavigationPageService, NavigationPageService>();
        #if WINDOWS
        services.AddTransient<IHotKetImpl, HotKeyImpl>();
        //services.AddTransient<IScreenCapture, ScreenCaptureByDx11>();
        services.AddTransient<IScreenCapture, ScreenCaptureByWGC>();
        services.AddTransient<IEverythingService, EverythingService>();
        services.AddTransient<IAppToolService, AppToolService>();
        services.AddSingleton<ISearchItemTool, SearchItemTool>();
        services.AddTransient<IClipboardService, ClipboardWindow>();
        services.AddTransient<IWindowTool, WindowToolServiceWindow>();
        services.AddTransient<IApplicationService, ApplicationService>();
        #endif

        #if LINUX
           services.AddTransient<IAppToolService, AppToolLinuxService>();

        #endif


        services.AddTransient<TaskEditorViewModel>();
        services.AddTransient<TaskEditor>(e => new TaskEditor() { DataContext = e.GetService<TaskEditorViewModel>() });
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainWindow>(e => new MainWindow() { DataContext = e.GetService<MainWindowViewModel>() });
        services.AddSingleton<SearchWindowViewModel>(e => new SearchWindowViewModel() { IsActive = true });
        services.AddSingleton<SearchWindow>(e => new SearchWindow()
            { DataContext = e.GetService<SearchWindowViewModel>() });
        services.AddTransient<MouseQuickWindowViewModel>();
        services.AddTransient<MouseQuickWindow>(e => new MouseQuickWindow()
            { DataContext = e.GetService<MouseQuickWindowViewModel>() });
        services.AddSingleton<HomePageViewModel>(e => new HomePageViewModel() { IsActive = true });
        services.AddKeyedSingleton<UserControl, HomePage>("HomePage",
            (e, _) => new HomePage() { DataContext = e.GetService<HomePageViewModel>() });
        services.AddSingleton<CustomScenariosManagerPageViewModel>(e => new CustomScenariosManagerPageViewModel()
            { IsActive = true });
        services.AddKeyedSingleton<UserControl, CustomScenariosManagerPage>("CustomScenariosManagerPage",
            (e, _) => new CustomScenariosManagerPage()
                { DataContext = e.GetService<CustomScenariosManagerPageViewModel>() });
        services.AddSingleton<HotKeyManagerPageViewModel>(e => new HotKeyManagerPageViewModel() { });
        services.AddKeyedSingleton<UserControl, HotKeyManagerPage>("HotKeyManagerPage",
            (e, _) => new HotKeyManagerPage() { DataContext = e.GetService<HotKeyManagerPageViewModel>() });
        services.AddTransient<PluginManagerPageViewModel>(e => new PluginManagerPageViewModel() { IsActive = true });
        services.AddKeyedTransient<UserControl, PluginManagerPage>("PluginManagerPage",
            (e, _) => new PluginManagerPage() { DataContext = e.GetService<PluginManagerPageViewModel>() });
        services.AddSingleton<PluginSettingViewModel>(e => new PluginSettingViewModel() { IsActive = true });
        services.AddKeyedSingleton<UserControl, PluginSettingSelectPage>("PluginSettingSelectPage",
            (e, _) => new PluginSettingSelectPage() { DataContext = e.GetService<PluginSettingViewModel>() });
        services.AddTransient<MarketPageViewModel>();
        services.AddKeyedTransient<UserControl, MarketPage>("MarketPage",
            (e, _) => new MarketPage() { DataContext = e.GetService<MarketPageViewModel>() });
        services.AddSingleton<SettingPage>(e => new SettingPage());

        return services.BuildServiceProvider();
    }
    private static void CheckAndDeleteLogFiles()
    {
        // 定义日志文件的目录
        var logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
        log.Debug($"检查日志目录:{logDirectory}");
        // 定义要保留的日志文件的时间范围，这里是一周
        var timeSpan = TimeSpan.FromDays(2);

        // 获取当前的日期
        var currentDate = DateTime.Today;

        // 获取目录下的所有日志文件，按照最后修改时间排序
        var logFiles = Directory.EnumerateFiles(logDirectory)
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.LastWriteTime);

        // 遍历每个日志文件
        foreach (var logFile in logFiles)
        {
            // 计算日志文件的最后修改时间和当前日期的差值
            // 如果差值大于要保留的时间范围，就删除该日志文件
            if (currentDate - logFile.LastWriteTime > timeSpan)
            {
                log.Debug($"删除日志文件:{logFile.FullName}");
                logFile.Delete();
            }
        }
    }
    
    public static void OnStartup(string[] arg)
    {
        log.Info("启动");
        ServiceManager.Services = ConfigureServices();

        CheckAndDeleteLogFiles();
        
        MqttManager.Init().Wait();
        log.Info("MQTT初始化完成");
        HotKeyManager.Init();
        log.Debug("注册热键管理器完成");
        ConfigManger.Init();
        log.Info("配置文件初始化完成");
        if (ConfigManger.Config.mouseCapture)
        {
            HotKeyManager.HotKetImpl.StartHook();
        }
        ServiceManager.Services.GetService<IToastService>().Init();
        
        
        
        switch (ConfigManger.Config.themeChoice)
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

        log.Info("主题初始化完成");

        PluginManager.Init();
        log.Info("插件管理器初始化完成");
        CustomScenarioManger.Init();
        log.Info("场景管理器初始化完成");


        ServicePointManager.DefaultConnectionLimit = 10240;

        if (ConfigManger.Config.autoStart)
        {
            log.Info("设置开机自启");
            ServiceManager.Services.GetService<IApplicationService>()
                .ChangeAutoStart(true);
        }
        ServiceManager.Services.GetService<IApplicationService>().Init();
        Dispatcher.UIThread.InvokeAsync(() => { ServiceManager.Services.GetService<SearchWindowViewModel>(); });
    }
    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
    {
        var buildAvaloniaApp = AppBuilder.Configure<App>();
        buildAvaloniaApp.UsePlatformDetect();
        buildAvaloniaApp.With(new FontManagerOptions()
        {
            DefaultFamilyName = "avares://KitopiaAvalonia/Assets/HarmonyOS_Sans_SC_Regular.ttf#HarmonyOS Sans",
            FontFallbacks = new[]
            {
                new FontFallback()
                {
                    FontFamily =
                        new FontFamily("avares://KitopiaAvalonia/Assets/HarmonyOS_Sans_SC_Regular.ttf#HarmonyOS Sans")
                }
            },
        });
        buildAvaloniaApp.With(new RenderOptions()
        {
            TextRenderingMode = TextRenderingMode.Antialias,
            EdgeMode = EdgeMode.Antialias,
            BitmapInterpolationMode = BitmapInterpolationMode.HighQuality,
        });
        buildAvaloniaApp.LogToTrace();

        return buildAvaloniaApp;
    }
}