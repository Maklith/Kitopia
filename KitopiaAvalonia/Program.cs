using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Core.CustomScenario;
using Core.SDKs.Services;
using Core.Services;
using Core.Services.Config;
using Core.Services.MQTT;
using Core.Services.Onnx;
using Core.Services.Plugin;
using Core.Utils;
using Core.ViewModel.Main;
using Core.ViewModel.Pages;
using Core.ViewModel.Pages.customScenario;
using Core.ViewModel.Pages.plugin;
using Core.ViewModel.TaskEditor;
using Core.ViewModel.Windows;
using Core.Window;
using Core.Window.Everything;
using KitopiaAvalonia.Pages;
using KitopiaAvalonia.Services;
using KitopiaAvalonia.Windows;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using PluginCore.Onnx;
using Serilog;
using HotKeyManager = Core.Services.HotKey.HotKeyManager;
using ScreenCaptureWindow = KitopiaAvalonia.Services.ScreenCaptureWindow;

namespace KitopiaAvalonia;

internal class Program
{
    private static ILogger Log = LogManager.Logger.ForContext<Program>();

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            // RxApp.DefaultExceptionHandler = new MyCoolObservableExceptionHandler();
            TaskScheduler.UnobservedTaskException += (sender, eventArgs) => { Log.Error(eventArgs.Exception, "错误"); };

            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                Log.Fatal((Exception)e.ExceptionObject, "错误");
            };
            AppDomain.CurrentDomain.ProcessExit += (sender, e) =>
            {
                Log.Information("程序退出");
                LogManager.Logger.Dispose();
                ServiceManager.Services.GetService<IToastService>().Unregister();
            };
            Task.Run(async () =>
            {
                while (Application.Current is null) await Task.Delay(100);

                try
                {
                    OnStartup(args);
                }
                catch (Exception e)
                {
                    Log.Fatal(e, "启动失败");
                    Environment.Exit(0);
                }
            });
            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }
        catch (Exception e)
        {
            Log.Fatal(e, "");
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
        services.AddSingleton<IPluginManger, PluginMangerService>();
        services.AddTransient<IThemeChange, ThemeChange>();

        services.AddSingleton<ISearchItemChooseService, SearchItemChooseService>();
        services.AddSingleton<IMouseQuickWindowService, MouseQuickWindowService>();
        services.AddTransient<ISearchWindowService, SearchWindowService>();
        services.AddTransient<IErrorWindow, ErrorWindow>();
        services.AddTransient<IScreenCaptureWindow, ScreenCaptureWindow>();

        services.AddTransient<IPluginToolService, PluginToolService>();

        services.AddTransient<INavigationPageService, NavigationPageService>();
        services.AddTransient<IInferenceSessionManager, InferenceSessionManager>();
        #if WINDOWS
        services.AddTransient<IHotKetImpl, HotKeyImpl>();
        services.AddTransient<IScreenCaptureManager, ScreenCaptureManager>();
        services.AddTransient<IScreenCapture, ScreenCaptureByWGC>();

        services.AddTransient<IEverythingService, EverythingService>();
        services.AddTransient<IAppToolService, AppToolService>();
        services.AddSingleton<ISearchItemTool, SearchItemTool>();
        services.AddTransient<IClipboardService, ClipboardWindow>();
        services.AddTransient<IWindowTool, WindowToolServiceWindow>();
        services.AddTransient<IApplicationService, ApplicationService>();
        services.AddTransient<IImageTool, ImageTool>();
        #endif

        #if LINUX
           services.AddTransient<IAppToolService, AppToolLinuxService>();

        #endif


        services.AddTransient<TaskEditorViewModel>();
        services.AddTransient<TaskEditor>(e => new TaskEditor { DataContext = e.GetService<TaskEditorViewModel>() });
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainWindow>(e => new MainWindow { DataContext = e.GetService<MainWindowViewModel>() });
        services.AddSingleton<SearchWindowViewModel>(e => new SearchWindowViewModel { IsActive = true });
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
        services.AddSingleton<HotKeyManagerPageViewModel>(e => new HotKeyManagerPageViewModel { });
        services.AddKeyedSingleton<UserControl, HotKeyManagerPage>("HotKeyManagerPage",
            (e, _) => new HotKeyManagerPage { DataContext = e.GetService<HotKeyManagerPageViewModel>() });
        services.AddTransient<PluginManagerPageViewModel>(e => new PluginManagerPageViewModel { IsActive = true });
        services.AddKeyedTransient<UserControl, PluginManagerPage>("PluginManagerPage",
            (e, _) => new PluginManagerPage { DataContext = e.GetService<PluginManagerPageViewModel>() });
        services.AddSingleton<PluginSettingViewModel>(e => new PluginSettingViewModel { IsActive = true });
        services.AddKeyedSingleton<UserControl, PluginSettingSelectPage>("PluginSettingSelectPage",
            (e, _) => new PluginSettingSelectPage { DataContext = e.GetService<PluginSettingViewModel>() });
        services.AddTransient<MarketPageViewModel>();
        services.AddKeyedTransient<UserControl, MarketPage>("MarketPage",
            (e, _) => new MarketPage { DataContext = e.GetService<MarketPageViewModel>() });
        services.AddTransient<OnnxModelManagerPageViewModel>();
        services.AddKeyedTransient<UserControl, OnnxModelManagerPage>("OnnxModelManagerPage",
            (e, _) => new OnnxModelManagerPage { DataContext = e.GetService<OnnxModelManagerPageViewModel>() });


        services.AddSingleton<SettingPage>(e => new SettingPage());
        services.AddSingleton<GitHubUpdateService>();

        return services.BuildServiceProvider();
    }

    private static void CheckAndDeleteLogFiles()
    {
        // 定义日志文件的目录
        var logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
        Log.Debug($"检查日志目录:{logDirectory}");
        // 定义要保留的日志文件的时间范围，这里是一周
        var timeSpan = TimeSpan.FromDays(2);

        // 获取当前的日期
        var currentDate = DateTime.Today;

        // 获取目录下的所有日志文件，按照最后修改时间排序
        try
        {
            var logFiles = Directory.EnumerateFiles(logDirectory)
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.LastWriteTime);

            // 遍历每个日志文件
            foreach (var logFile in logFiles)
                // 计算日志文件的最后修改时间和当前日期的差值
                // 如果差值大于要保留的时间范围，就删除该日志文件
                if (currentDate - logFile.LastWriteTime > timeSpan)
                {
                    Log.Debug($"删除日志文件:{logFile.FullName}");
                    logFile.Delete();
                }
        }
        catch (Exception e)
        {
            // ignored
        }
    }
    
    private static async Task CheckUpdates()
    {
        var gitHubUpdateService = ServiceManager.Services.GetService<GitHubUpdateService>();
        var (hasUpdate, latestVersion, downloadUrl, releaseNotes) = await gitHubUpdateService!.CheckForUpdatesAsync();
        if (hasUpdate && !string.IsNullOrEmpty(downloadUrl))
        {
            Log.Information($"发现新版本:{latestVersion}");
            var dialog = new DialogContent()
            {
                Title = $"Kitopia更新 - 发现新版本 {latestVersion}",
                Content = $"发现新版本 {latestVersion}，是否前往下载？\n\n更新内容:\n{releaseNotes ?? "无更新说明"}",
                PrimaryButtonText = "下载并更新",
                SecondaryButtonText = "取消",
                PrimaryAction = async () =>
                {
                    try
                    {
                        var toastService = ServiceManager.Services.GetService<IToastService>();
                        var tempPath = Path.Combine(Path.GetTempPath(), $"Kitopia_{latestVersion}_Installer.exe");
                        
                        using var client = new HttpClient();
                        using var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
                        response.EnsureSuccessStatusCode();

                        var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                        var canReportProgress = totalBytes != -1;

                        await using var contentStream = await response.Content.ReadAsStreamAsync();
                        var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

                        var buffer = new byte[8192];
                        long totalRead = 0;
                        int bytesRead;
                        var lastProgress = -1;

                        toastService!.Show("更新", "开始下载更新...");

                        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, bytesRead);
                            totalRead += bytesRead;

                            if (canReportProgress)
                            {
                                var progress = (int)((double)totalRead / totalBytes * 100);
                                if (progress > lastProgress && progress % 10 == 0) // Report every 10%
                                {
                                    lastProgress = progress;
                                    toastService.Show("更新", $"下载进度: {progress}%");
                                }
                            }
                        }
                        await fileStream.DisposeAsync();
                        toastService.Show("更新", "下载完成，正在启动安装程序...");
                        await Task.Delay(1000);
                        // Close application and start installer
                        ServiceManager.Services.GetService<ISearchItemTool>()!.OpenFile(tempPath);
                        await Task.Delay(2000);
                        Environment.Exit(0);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "更新失败");
                        ServiceManager.Services.GetService<IToastService>()!.Show("更新失败", $"下载出错: {ex.Message}",  NotificationType.Error);
                    }
                }
            };
            await ((IContentDialog)ServiceManager.Services!.GetService(typeof(IContentDialog))!).ShowDialogAsync(null,
                dialog);
        }
    }

    public static void OnStartup(string[] arg)
    {
        Log.Information("启动");
        ServiceManager.Services = ConfigureServices();

        CheckAndDeleteLogFiles();
        
        Task.Run((async () =>
        {
            await CheckUpdates();
            await Task.Delay(TimeSpan.FromMinutes(30));
        }));
        MqttManager.Init().GetAwaiter().GetResult();
        Log.Information("MQTT初始化完成");
        HotKeyManager.Init();
        Log.Debug("注册热键管理器完成");
        ConfigManger.Init();
        Log.Information("配置文件初始化完成");
        if (ConfigManger.Config.mouseCapture) HotKeyManager.HotKetImpl.StartHook();
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

        Log.Information("主题初始化完成");

        PluginManager.Init();
        Log.Information("插件管理器初始化完成");
        CustomScenarioManger.Init();
        Log.Information("场景管理器初始化完成");


        ServicePointManager.DefaultConnectionLimit = 10240;

        if (ConfigManger.Config.autoStart)
        {
            Log.Information("设置开机自启");
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
        buildAvaloniaApp.With(new FontManagerOptions
        {
            DefaultFamilyName = "avares://KitopiaAvalonia/Assets/HarmonyOS_Sans_SC_Regular.ttf#HarmonyOS Sans",
            FontFallbacks = new[]
            {
                new FontFallback
                {
                    FontFamily =
                        new FontFamily("avares://KitopiaAvalonia/Assets/HarmonyOS_Sans_SC_Regular.ttf#HarmonyOS Sans")
                }
            }
        });
        buildAvaloniaApp.With(new RenderOptions
        {
            TextRenderingMode = TextRenderingMode.Antialias,
            EdgeMode = EdgeMode.Antialias,
            BitmapInterpolationMode = BitmapInterpolationMode.HighQuality
        });
        buildAvaloniaApp.LogToTrace();
        return buildAvaloniaApp;
    }
}