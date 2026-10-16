using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Media;
using Avalonia.Input;
using Kitopia.Desktop.Features.CustomScenario;
using Kitopia.Desktop.Features.CustomScenario.ViewModels.TaskEditor;
using Kitopia.Desktop.Features.Services.Config;
using Kitopia.Desktop.Features.Services.Interfaces;
using Kitopia.Desktop.Features.Services.Plugin;
using Kitopia.Desktop.Features.Search;
using Kitopia.Desktop.Features.Search.ViewModels;
using Kitopia.Desktop.Features.Indexing;
using Kitopia.Desktop.Features.ViewModel.Main;
using Kitopia.Desktop.Features.ViewModel.Pages;
using Kitopia.Desktop.Features.ViewModel.Pages.plugin;
using Kitopia.Desktop.Features.ViewModel.Windows;
using Kitopia.Desktop.Abstractions.FileSystem;
using Kitopia.Desktop.Pages;
using Kitopia.Desktop.Services;
using Kitopia.Desktop.ViewModels;
using Kitopia.Desktop.Windows;
using Kitopia.Feature.Localization;
using KitopiaEx.ImageCompression;
using KitopiaEx.VideoCompression;
using KitopiaEx.BatchRename;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using PluginCore.Media;
using PluginCore.Config;
using PluginCore.CustomScenario;
using PluginCore.CustomScenario.Attribute.Scenario;
using Kitopia.Feature.Avalonia.DeviceCommunication.ViewModels;
using Kitopia.Feature.Avalonia.DeviceCommunication.Views;
using Kitopia.Feature.DeviceCommunication.Discovery;
using Kitopia.Feature.DeviceCommunication.Application;
using System.Net;
using CommunityToolkit.Mvvm.Input;

public static class Renderer
{
    private static bool English { get; set; }
    private static string Output => Environment.GetEnvironmentVariable("KITOPIA_SCREENSHOT_OUTPUT")
        ?? Path.Combine(Program.Repo, "docs", "assets", "README");

    public static void RenderNativePdf(bool english)
    {
        ReactiveUI.Builder.RxAppBuilder.CreateReactiveUIBuilder().WithPlatformServices().BuildApp();
        AppBuilder.Configure<Kitopia.Desktop.App>().UsePlatformDetect().UseSkia()
            .With(new FontManagerOptions { DefaultFamilyName = "avares://Kitopia.Desktop/Assets/HarmonyOS_Sans_SC_Regular.ttf#HarmonyOS Sans" })
            .SetupWithoutStarting();
        Lang.Current.UseLanguage(english ? "en-US" : "zh-CN");
        Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
        using var shutdown = new CancellationTokenSource();
        Exception? failure = null;
        Dispatcher.UIThread.Post(async () => {
            MouseQuickWindow? window = null;
            try {
                using var model = new MouseQuickWindowViewModel();
                await model.SetFilesAsync([Path.Combine(Program.Root, "media", english ? "KitopiaFeatureGuide.pdf" : "Kitopia功能介绍.pdf")]);
                window = new MouseQuickWindow { DataContext=model,Width=900,Height=650,
                    WindowStartupLocation=WindowStartupLocation.Manual,Position=new PixelPoint(-20000,-20000),ShowActivated=false,Topmost=false };
                window.Show();
                for(var attempt=0;attempt<120 && model.IsLoading;attempt++) await Task.Delay(250);
                if(model.Message is not null || model.IsLoading) throw new Exception(model.Message ?? "PDF preview timed out.");
                var host=window.GetVisualDescendants().OfType<Kitopia.Desktop.Platform.Windows.NativeFilePreviewHost>().Single();
                var controller=(Microsoft.Web.WebView2.Core.CoreWebView2Controller?)typeof(Kitopia.Desktop.Platform.Windows.NativeFilePreviewHost)
                    .GetField("_controller",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(host)
                    ?? throw new Exception("No PDF renderer.");
                await Task.Delay(2000);
                using var pdfStream=new MemoryStream();
                await controller.CoreWebView2.CapturePreviewAsync(Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png,pdfStream);
                pdfStream.Position=0;
                using var pdfBitmap=new Bitmap(pdfStream);
                using var chrome=new RenderTargetBitmap(new PixelSize(900,650),new Vector(96,96));
                chrome.Render(window);
                using var combined=new RenderTargetBitmap(new PixelSize(900,650),new Vector(96,96));
                var origin=host.TranslatePoint(new Point(0,0),window) ?? throw new Exception("No native preview bounds.");
                using(var context=combined.CreateDrawingContext()) {
                    context.DrawImage(chrome,new Avalonia.Rect(0,0,900,650));
                    context.DrawImage(pdfBitmap,new Avalonia.Rect(origin,host.Bounds.Size));
                }
                Directory.CreateDirectory(Output);
                combined.Save(Path.Combine(Output, english ? "file-preview-en-20261014.png" : "file-preview-20261014.png"));
                Console.WriteLine($"PDF preview: {pdfBitmap.PixelSize}; bounds: {origin} {host.Bounds.Size}");
            } catch(Exception error) { failure=error; }
            finally { window?.Close();shutdown.Cancel(); }
        });
        Dispatcher.UIThread.MainLoop(shutdown.Token);
        if(failure is not null) throw failure;
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<Kitopia.Desktop.App>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseSkia()
        .With(new FontManagerOptions { DefaultFamilyName = "avares://Kitopia.Desktop/Assets/HarmonyOS_Sans_SC_Regular.ttf#HarmonyOS Sans",
            FontFallbacks = [new FontFallback { FontFamily = new FontFamily("avares://Kitopia.Desktop/Assets/HarmonyOS_Sans_SC_Regular.ttf#HarmonyOS Sans") }] });

    public static async Task Run(string[] args)
    {
        ReactiveUI.Builder.RxAppBuilder.CreateReactiveUIBuilder().WithPlatformServices().BuildApp();
        await using var session = HeadlessUnitTestSession.StartNew(typeof(Renderer));
        await session.Dispatch(async () =>
        {
            try {
            Lang.Current.RegisterAssembly(typeof(KitopiaEx.Config).Assembly);
            var english = args.Contains("en") || args.Contains("search-en") || args.Contains("rename-en");
            English = english;
            Lang.Current.UseLanguage(english ? "en-US" : "zh-CN");
            PluginCore.Localization.Lang.Lookup = Lang.Get;
            PluginCore.Localization.Lang.BindingSource = Lang.Current;
            Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
            PluginOverall.Features.Clear();
            typeof(ConfigManger).GetProperty("Configs", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null,
                new Dictionary<string, ConfigBase> { ["KitopiaConfig"] = new KitopiaConfig { useEverything = false } });
            using var provider = new ServiceCollection()
                .AddSingleton<INavigationService, NavigationService>()
                .AddSingleton(System.Reflection.DispatchProxy.Create<IWindowTool, RenderProxy>())
                .AddSingleton<IAppToolService, Kitopia.Desktop.Platform.Windows.AppTools.AppToolService>()
                .AddSingleton(System.Reflection.DispatchProxy.Create<IClipboardService, RenderProxy>())
                .AddSingleton(System.Reflection.DispatchProxy.Create<IScreenCaptureManager, RenderProxy>())
                .AddSingleton(System.Reflection.DispatchProxy.Create<IIndexService, RenderProxy>())
                .AddSingleton(System.Reflection.DispatchProxy.Create<ICustomScenarioPluginIntegration, RenderProxy>())
                .AddKeyedTransient<UserControl>("HomePage", (_,_) => new HomePage { DataContext = new HomePageViewModel() })
                .BuildServiceProvider();
            ServiceManager.Services = provider;
            if (args.Contains("rename") || args.Contains("rename-en")) {
                await RenderBatchRename();
                return true;
            }
            if(args.Contains("search") || args.Contains("search-en")) {
                await RenderSearch(provider, english);
                return true;
            }
            if(args.Contains("transparent")) {
                var window=new SelectionTranslationWindow(new SelectionTranslationWindowViewModel(provider.GetRequiredService<IClipboardService>()) { SourceText="Build your own workflow with visual nodes.",TranslatedText="使用可视化节点构建自己的工作流。",SourceLanguage=TranslationSourceLanguage.English });
                Capture(window,"selection-translation"); window.Close();
                using var vm=new MouseQuickWindowViewModel();
                await vm.SetFilesAsync([Path.Combine(Program.Root, "media", "misakamikoto.jpg")]);
                var preview=new MouseQuickWindow {DataContext=vm,Width=900,Height=650};
                Capture(preview,"file-preview"); preview.Close();
                return true;
            }
            CustomScenarioGlobe.TypeNames[typeof(ScreenCaptureResult).FullName!]="lang.kitopiaex.screen_capture_data";
            CustomScenarioGlobe.TypeNames[typeof(ScreenCaptureInfo).FullName!]="lang.kitopiaex.screen_capture_information";
            CustomScenarioGlobe.TypeNames[typeof(IEnumerable<KitopiaEx.Ocr.OcrResult>).FullName!]="lang.kitopiaex.ocr_result_array";
            CustomScenarioGlobe.TypeNames[typeof(KitopiaEx.Translate.SourceTranslateLang).FullName!]="lang.kitopiaex.source_language";
            CustomScenarioGlobe.TypeNames[typeof(KitopiaEx.Translate.TargetTranslateLang).FullName!]="lang.kitopiaex.target_language";
            foreach (var type in new[] { typeof(KitopiaFeatures), typeof(ImageCompressionWindow), typeof(VideoCompressionWindow), typeof(BatchRenameWindow), typeof(KitopiaEx.ScreenCaptureExs.QRCode), typeof(KitopiaEx.ScreenCaptureExs.Translate), typeof(KitopiaEx.ScreenCaptureExs.ImagePin) })
            {
                var features = type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .Select(method => (method, attr: method.GetCustomAttribute<FeatureAttribute>())).Where(pair => pair.attr != null)
                    .Select(pair => PluginOverall.CreateFeature(type == typeof(KitopiaFeatures) ? "Kitopia" : "KitopiaEx", pair.method, pair.attr!, provider)).ToList();
                if (features.Count > 0) PluginOverall.Features.Add(type.FullName!, features);
            }
            var navigation = provider.GetRequiredService<INavigationService>();
            var main = new MainWindow { DataContext = new MainWindowViewModel(navigation), Width = 2400, Height = 1280, WindowState = WindowState.Normal };
            main.Opened -= typeof(MainWindow).GetMethod("FirstOpenEventHandler", BindingFlags.Instance | BindingFlags.NonPublic)!.CreateDelegate<EventHandler>(main);
            main.Closing -= typeof(MainWindow).GetMethod("Window_OnClosing", BindingFlags.Instance | BindingFlags.NonPublic)!.CreateDelegate<EventHandler<WindowClosingEventArgs>>(main);
            Capture(main, "home");
            main.Close();
            await RenderSearch(provider, english);
            RenderChat();
            RenderScenario(provider);
            RenderPlugins();
            RenderFileLocks();
            await RenderBatchRename();
            var translation = new SelectionTranslationWindow(new SelectionTranslationWindowViewModel(provider.GetRequiredService<IClipboardService>())
            {
                SourceText = english ? "Construisez votre propre flux de travail." : "Build your own workflow with visual nodes.",
                TranslatedText = english ? "Build your own workflow." : "使用可视化节点构建自己的工作流。",
                SourceLanguage = english ? TranslationSourceLanguage.Auto : TranslationSourceLanguage.English,
                TargetLanguage = english ? TranslationTargetLanguage.English : TranslationTargetLanguage.SimplifiedChinese
            });
            Capture(translation, "selection-translation");
            translation.Close();
            var work = Path.Combine(Path.GetTempPath(), "kitopia-doc-screenshots", english ? "en" : "zh");
            Directory.CreateDirectory(work);
            var overview = Path.Combine(work, english ? "Feature overview.png" : "功能概览.png");
            var scenario = Path.Combine(work, english ? "Screenshot translation workflow.png" : "截图翻译工作流.png");
            File.Copy(Path.Combine(Output, english ? "home-en-20261014.png" : "home-20261014.png"), overview, true);
            File.Copy(Path.Combine(Output, english ? "scenario-editor-en-20261014.png" : "scenario-editor-20261014.png"), scenario, true);
            using var imageModel = new ImageCompressionViewModel(new Ffmpeg()) { Format = ImageCompressionFormat.WebP, Lossless = false, Quality = 80, ResizePercent = 75, SkipLarger = false };
            await imageModel.AddPathsAsync([overview, scenario]);
            await imageModel.StartCommand.ExecuteAsync(null);
            var image = new ImageCompressionWindow { DataContext = imageModel, Width = 1180, Height = 740 };
            Capture(image, "image-compression");
            image.Close();
            using var videoModel = new VideoCompressionViewModel(new Ffmpeg()) { Quality = 70, ResizePercent = 75, SkipLarger=false };
            var demoVideo = Path.Combine(work, english ? "Kitopia demo.mp4" : "Kitopia功能演示.mp4");
            File.Copy(Path.Combine(Program.Root, "media", "Kitopia功能演示.mp4"), demoVideo, true);
            await videoModel.AddPathsAsync([demoVideo]);
            await videoModel.StartCommand.ExecuteAsync(null);
            var video = new VideoCompressionWindow { DataContext = videoModel, Width = 1180, Height = 860 };
            Capture(video, "video-compression");
            videoModel.Mode=VideoCompressionMode.Conversion;
            videoModel.Format=VideoCompressionFormat.MKV;
            videoModel.Codec=VideoCompressionCodec.KeepOriginal;
            await videoModel.StartCommand.ExecuteAsync(null);
            Capture(video,"video-conversion");
            video.Close();
            RenderCapture();
            await Task.CompletedTask;
            } catch (Exception error) { Console.Error.WriteLine(error); throw; }
            return true;
        }, CancellationToken.None);
    }

    internal static void Capture(Window window, string name)
    {
        if (!window.IsVisible) window.Show();
        Dispatcher.UIThread.RunJobs();
        if (English)
        {
            foreach (var text in window.GetVisualDescendants().OfType<TextBlock>())
            {
                if (text.Text == "自动检测") text.Text = "Auto detect";
                else if (text.Text == "简体中文") text.Text = "Simplified Chinese";
            }
        }
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Directory.CreateDirectory(Output);
        var path = Path.Combine(Output, name + (English && !name.EndsWith("-en", StringComparison.Ordinal) ? "-en" : "") + "-20261014.png");
        if(name is "selection-translation" or "file-preview" or "image-preview-en") {
            using var transparent=new RenderTargetBitmap(PixelSize.FromSize(window.Bounds.Size,1),new Vector(96,96));
            transparent.Render(window);
            transparent.Save(path);
            Console.WriteLine(path+" "+transparent.PixelSize);
            return;
        }
        using var bitmap = window.CaptureRenderedFrame() ?? throw new Exception("No frame: " + name);
        bitmap.Save(path);
        Console.WriteLine(path + " " + bitmap.PixelSize);
    }

    private static async Task RenderSearch(IServiceProvider provider, bool english)
    {
        using var previewModel = new MouseQuickWindowViewModel();
        var imagePath = Path.Combine(Program.Root, "media", "misakamikoto.jpg");
        await previewModel.SetFilesAsync([imagePath]);
        var preview = new MouseQuickWindow { DataContext = previewModel, Width = 900, Height = 650 };
        Capture(preview, english ? "image-preview-en" : "file-preview");
        preview.Close();
        var model = new SearchWindowViewModel();
        var window = new SearchWindow { DataContext=model };
        window.Closing -= typeof(SearchWindow).GetMethod("Window_OnClosing",BindingFlags.Instance|BindingFlags.NonPublic)!.CreateDelegate<EventHandler<WindowClosingEventArgs>>(window);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        model.Items.AddRange(new[]
        {
            new SearchViewItem { ItemDisplayName="Microsoft Edge", OnlyKey="C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe", FileType=FileType.应用程序 },
            new SearchViewItem { ItemDisplayName="JetBrains Rider", OnlyKey="C:/Users/liaom/AppData/Local/Programs/Rider/bin/rider64.exe", FileType=FileType.应用程序 },
            new SearchViewItem { ItemDisplayName="misakamikoto.jpg", OnlyKey=imagePath, FileType=FileType.图像 },
            new SearchViewItem { ItemDisplayName=english ? "Kitopia project homepage" : "Kitopia 项目主页", OnlyKey="https://github.com/Maklith/Kitopia", FileType=FileType.URL, IconPath=Path.Combine(Program.Repo, "src", "Kitopia.Desktop", "Assets", "icon.png") },
            new SearchViewItem { ItemDisplayName=english ? "Kitopia project folder" : "Kitopia 项目目录", OnlyKey=Program.Repo, FileType=FileType.文件夹 },
            new SearchViewItem { ItemDisplayName=english ? "Demo presentation.pptx" : "演示文稿1.pptx", OnlyKey="C:/Users/liaom/Desktop/演示文稿1.pptx", FileType=FileType.PPT文档 }
        });
        foreach(var type in model.Items.Select(item=>item.FileType).Distinct()) model.FileTypes.Add(new FileTypeFilter { FileType=type,IsChecked=true });
        model.ShowFileTypeFilter=true;
        foreach(var item in model.Items) {
            if(item.FileType != FileType.URL) item.OnlyKey=Path.GetFullPath(item.OnlyKey);
            if(item.IconPath is not null) item.IconPath=Path.GetFullPath(item.IconPath);
        }
        foreach(var item in model.Items) provider.GetRequiredService<IAppToolService>().LoadIcon(item);
        for(var i=0;i<60 && model.Items.Any(item=>item.Icon is null);i++) { await Task.Delay(50); Dispatcher.UIThread.RunJobs(); }
        foreach(var item in model.Items) if(item.Icon is null) throw new Exception("Icon not loaded: "+item.OnlyKey);
        if (english) ApplyEnglishSearchLabels(window);
        Capture(window, english ? "search-en" : "search");
        model.IsPreviewMode = true;
        model.SelectedItem = model.Items.Single(item=>item.OnlyKey==Path.GetFullPath(imagePath));
        await model.FilePreview.SetFilesAsync([imagePath]);
        Capture(window, english ? "search-preview-en" : "search-preview");
        window.DataContext=null;
        model.FilePreview.Dispose();
        window.Close();
    }

    private static void ApplyEnglishSearchLabels(SearchWindow window)
    {
        var labels = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["应用程序"] = "Application",
            ["图像"] = "Image",
            ["URL"] = "URL",
            ["文件夹"] = "Folder",
            ["PPT文档"] = "PPT document"
        };

        foreach (var button in window.GetVisualDescendants().OfType<ToggleButton>())
        {
            if (button.Content is FileType fileType && labels.TryGetValue(fileType.ToString(), out var label))
                button.Content = label;
        }

        foreach (var textBlock in window.GetVisualDescendants().OfType<TextBlock>())
        {
            if (textBlock.Text is not { } text || !labels.TryGetValue(text, out var label)) continue;
            textBlock.Text = label;
        }
    }

    private static void RenderChat()
    {
        var fixture=typeof(KitopiaTest.Mobile.DeviceCommunicationPageViewModelResumeTests);
        object Make(string name, params object?[] args) => Activator.CreateInstance(fixture.GetNestedType(name,BindingFlags.NonPublic)!, BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic, null,args,null)!;
        using var discovery=(IDeviceDiscoveryService)Make("FakeDiscoveryService", new object[] { new[] {
            new DiscoveredDevice { Id="demo-phone",Name=English ? "My phone" : "我的手机",OperatingSystem="Android",Ipv4Address=IPAddress.Parse("192.168.1.12"),TcpPort=50120 },
            new DiscoveredDevice { Id="demo-pc",Name=English ? "Work laptop" : "工作笔记本",OperatingSystem="Windows",Ipv4Address=IPAddress.Parse("192.168.1.15"),TcpPort=50124 }
        } });
        var messages=(IMessageAppService)Make("FakeMessageAppService",null,null,null);
        using var model=new DeviceCommunicationPageViewModel(discovery,messages,
            (IChatAttachmentStore)Make("FakeChatAttachmentStore"),(IChatPlatformService)Make("FakeChatPlatformService"),
            (IDeviceCommunicationSettings)Make("FakeDeviceCommunicationSettings"),(IChatNotificationSink)Make("FakeToastService",false),postToUi: action=>action());
        var conversation=model.Conversations.Single(item=>item.DeviceId=="demo-phone");
        model.SelectedConversation=conversation;
        var time=new DateTimeOffset(2026,10,14,14,30,0,TimeSpan.FromHours(8));
        conversation.Messages.Add(new DeviceChatMessageItem(English ? "Send me the project guide and demo files." : "把项目说明和演示素材发给我。",false,time));
        conversation.Messages.Add(new DeviceChatMessageItem(English ? "Sent. You can receive them over the LAN." : "已发送，可以直接在局域网接收。",true,time.AddMinutes(1)));
        conversation.Messages.Add(new FileChatMessageItem(English ? "Project guide.pdf" : "项目说明.pdf",2_621_440,false,time.AddMinutes(2))
        { TrackingTransferId=Guid.NewGuid(),AcceptCommand=new RelayCommand<FileChatMessageItem>(_=>{}),RejectCommand=new RelayCommand<FileChatMessageItem>(_=>{}) });
        conversation.Messages.Add(new FileChatMessageItem(English ? "Demo files.zip" : "演示素材.zip",25_165_824,true,time.AddMinutes(3))
        { Status=FileTransferStatus.InProgress,ReceiveProgress=0.65,TransferSpeedBytesPerSecond=5.5*1024*1024 });
        conversation.SetLastMessage(English ? "Demo files.zip" : "演示素材.zip",time.AddMinutes(3));
        var page=new DeviceCommunicationPage { DataContext=model };
        var window=new Window { Width=1200,Height=750,Content=page,Background=Brushes.White };
        Capture(window,"device-chat");
        window.Close();
        model.IsCompact=true;
        var mobile=new Window { Width=390,Height=800,Content=new DeviceCommunicationPage { DataContext=model },Background=Brushes.White };
        Capture(mobile,"mobile-chat");
        mobile.Close();
    }

    private static void RenderScenario(IServiceProvider provider)
    {
        var model=new TaskEditorViewModel();
        model.Scenario.Name=English ? "Screenshot translation to clipboard" : "截图翻译到剪贴板";
        model.Scenario.Description=English ? "Capture a region, extract text, translate and copy" : "选择区域、提取文字、翻译并复制结果";
        model.Scenario.Nodes[0].Title=model.Scenario.Name;
        model.Scenario.Nodes[0].Location=new Point(40,70);
        model.Scenario.Nodes[1].Location=new Point(40,190);
        var plugin=new PluginLocalInfo { PluginBaseInfo=new PluginBaseInfo { Name="Kitopia拓展",NameSign="kitopiaex",Version="1.0.0",Description="官方扩展插件" } };
        var methods=new[] {
            typeof(KitopiaEx.CustomScenarioMethods.ScreenCaptureNode).GetMethod("ScreenshotTheSelectArea",[typeof(CancellationToken)])!,
            typeof(KitopiaEx.CustomScenarioMethods.Ocr).GetMethod("OcrImg")!,
            typeof(KitopiaEx.CustomScenarioMethods.Ocr).GetMethod("JoinOcrText")!,
            typeof(KitopiaEx.CustomScenarioMethods.Translate).GetMethods().First(m=>m.GetParameters().FirstOrDefault()?.ParameterType==typeof(string)),
            typeof(KitopiaEx.CustomScenarioMethods.Ocr).GetMethod("CopyTextToClipboard")!
        };
        ScenarioMethodNode? previous=(ScenarioMethodNode)model.Scenario.Nodes[0];
        for(var i=0;i<methods.Length;i++)
        {
            var method=methods[i];
            var node=new ScenarioMethod(method,plugin,method.GetCustomAttribute<ScenarioMethodAttribute>()!,ScenarioMethodType.PluginMethod,provider).GenerateNode();
            node.Location=i<2 ? new Point(320+i*350,70) : new Point(40+(i-2)*400,390);
            foreach(var input in node.Input.Where(input=>input.InputObject.IsSelf))
                if(input.InputObject.SerializeType==typeof(KitopiaEx.Translate.SourceTranslateLang)) input.InputObject.Value=KitopiaEx.Translate.SourceTranslateLang.自动检测;
                else if(input.InputObject.SerializeType==typeof(KitopiaEx.Translate.TargetTranslateLang)) input.InputObject.Value=KitopiaEx.Translate.TargetTranslateLang.简体中文;
            model.Scenario.Nodes.Add(node);
            model.Scenario.Connections.Add(new ConnectionItem { Source=previous!.Output[0],Target=node.Input[0] });
            if(i>0) model.Scenario.Connections.Add(new ConnectionItem { Source=previous.Output.Last(),Target=node.Input[1] });
            previous=node;
        }
        var window=new Kitopia.Desktop.Windows.TaskEditors.TaskEditor { DataContext=model,Width=1500,Height=850,WindowState=WindowState.Normal };
        var editor=window.FindControl<NodifyM.Avalonia.Controls.NodifyEditor>("Editor")!;
        editor.Zoom=0.82;
        editor.OffsetX=20; editor.OffsetY=30;
        Capture(window,"scenario-editor");
        model.IsModified=false;
        window.Close();
    }

    private static void RenderCapture()
    {
        PluginOverall.ScreenCaptureExMethods["demo"]=new[] {
            ("lang.kitopia.extract_text",0xea72), ("lang.kitopiaex.read_qr_code",0xf5a1),
            ("lang.kitopiaex.translate",0xf83b),("lang.kitopiaex.pin_captured_image",0xf602),("lang.kitopiaex.save_image_locally",0xf678)
        }.Select(action=>new Kitopia.Desktop.Features.Utils.ScreenCaptureExMethod { Description=action.Item1,Symbol=action.Item2,Action=_=>{} }).ToList();
        var source=Path.Combine(Output, English ? "home-en-20261014.png" : "home-20261014.png");
        using var original=OpenCvSharp.Cv2.ImRead(source,OpenCvSharp.ImreadModes.Unchanged);
        using var image=new OpenCvSharp.Mat();
        OpenCvSharp.Cv2.Resize(original,image,new OpenCvSharp.Size(1500,800));
        using var bgra=new OpenCvSharp.Mat();
        if(image.Channels()==3) OpenCvSharp.Cv2.CvtColor(image,bgra,OpenCvSharp.ColorConversionCodes.BGR2BGRA); else image.CopyTo(bgra);
        var window=new Kitopia.Desktop.Windows.ScreenCaptureWindow([new ScreenCaptureResult { Source=bgra,Info=new ScreenCaptureInfo { ScreenInfo=new PluginCore.Rect(0,0,bgra.Width,bgra.Height) } }]);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.MouseMove(new Point(70,160)); Dispatcher.UIThread.RunJobs();
        window.MouseDown(new Point(70,160),MouseButton.Left); Dispatcher.UIThread.RunJobs();
        window.MouseMove(new Point(1400,970)); Dispatcher.UIThread.RunJobs();
        window.MouseUp(new Point(1400,970),MouseButton.Left); Dispatcher.UIThread.RunJobs();
        window.FindControl<Kitopia.Desktop.Controls.Capture.DraggableResizeableControl>("SelectBox")!.ContentRect=new Avalonia.Rect(65,75,1350,605);
        foreach(var name in new[] {"UpdateSelectBox","UpdateToolBar"}) typeof(Kitopia.Desktop.Windows.ScreenCaptureWindow).GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null);
        var rectangleButton=window.GetVisualDescendants().OfType<Button>().First(button=>Equals(ToolTip.GetTip(button),Lang.Get("lang.kitopia.rectangle")));
        typeof(Kitopia.Desktop.Windows.ScreenCaptureWindow).GetMethod("RectangleButton_OnClick",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,[rectangleButton,new Avalonia.Interactivity.RoutedEventArgs()]);
        window.MouseDown(new Point(86,100),MouseButton.Left); Dispatcher.UIThread.RunJobs();
        window.MouseMove(new Point(1350,230)); Dispatcher.UIThread.RunJobs();
        window.MouseUp(new Point(1350,230),MouseButton.Left); Dispatcher.UIThread.RunJobs();
        foreach(var tool in window.FindControl<Canvas>("Canvas")!.Children.OfType<Kitopia.Desktop.Controls.Capture.CaptureToolBase>()) tool.IsSelected=false;
        Dispatcher.UIThread.RunJobs();
        Capture(window,"screen-capture");
        window.Close();
    }

    private static void RenderPlugins()
    {
        var entries=new[] {"KitopiaEx","OnnxRuntime.CPU","OnnxRuntime.Gpu.Win","OnnxRuntime.OpenVino"}.Select(project=>{
            var info=System.Text.Json.JsonSerializer.Deserialize<PluginBaseInfo>(File.ReadAllText(Path.Combine(Program.Repo,"plugins",project,"manifest.json")))!;
            if (English)
            {
                info.Name = project == "KitopiaEx" ? "Kitopia Extensions" : project.Replace("OnnxRuntime.", "ONNX ", StringComparison.Ordinal);
                info.Description = project == "KitopiaEx" ? "Official tools for capture, compression and batch renaming" : "Local ONNX inference backend";
            }
            return new PluginLocalInfo {PluginBaseInfo=info,Path=Path.Combine(Program.Repo,"plugins",project)+Path.DirectorySeparatorChar,FullPath=Path.Combine(Program.Repo,"plugins",project,info.Main)};
        }).ToArray();
        typeof(PluginManager).GetField("AllPluginInfos",BindingFlags.Static|BindingFlags.NonPublic)!.SetValue(null,entries);
        var model=new PluginManagerPageViewModel();
        foreach(var item in model.Items) {
            item.AuthorName="Kitopia";
            item.Icon=new Bitmap(Path.Combine(Program.Repo, "src", "Kitopia.Desktop", "Assets", "icon.png"));
            item.Description=item.PluginBaseInfo.Description;
            typeof(PluginInfoUiHelper).GetField("_canUpdate",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(item,false);
        }
        var window=new Window {Width=1280,Height=760,Content=new PluginManagerPage {DataContext=model},Background=Brushes.White};
        Capture(window,"plugins");
        window.Close();
    }

    private static void RenderFileLocks()
    {
        var model=new FileLocksmithWindowViewModel(DispatchProxy.Create<IFileLockService,RenderProxy>()) {AutoRefresh=false,IsTreeView=false};
        model.LoadProcesses([new FileLockInfo {ProcessId=12480,ProcessName="POWERPNT",ExecutablePath="C:/Program Files/Microsoft Office/root/Office16/POWERPNT.EXE",FilePath=English ? "D:/Demo/Project guide.pptx" : "D:/演示素材/项目说明.pptx",State=English ? "Locked" : "已锁定",User=English ? "Current user" : "当前用户"}]);
        var window=new FileLocksmithWindow {DataContext=model,Width=1150,Height=700};
        Capture(window,"file-locks");
        window.Close();
    }

    private static async Task RenderBatchRename()
    {
        var work = Path.Combine(Path.GetTempPath(), "kitopia-doc-screenshots", English ? "rename-en" : "rename-zh");
        Directory.CreateDirectory(work);
        var names = new[] { "IMG_20261001.jpg", "IMG_20261002.jpg", "IMG_20261003.jpg", "IMG_20261004.jpg" };
        foreach (var name in names) File.Copy(Path.Combine(Program.Root, "media", "misakamikoto.jpg"), Path.Combine(work, name), true);
        using var model = new BatchRenameViewModel(new Ffmpeg())
        {
            BaseName = English ? "Railgun" : "超电磁炮",
            Prefix = English ? "Collection_" : "图集_",
            KeepOriginalName = false,
            StartNumber = 1,
            NumberDigits = 3,
            SortMode = BatchRenameSortMode.Name,
            CompressImages = true,
            Format = ImageCompressionFormat.WebP,
            Lossless = false,
            Quality = 80
        };
        await model.AddPathsAsync(names.Select(name => Path.Combine(work, name)));
        var window = new BatchRenameWindow { DataContext = model, Width = 1280, Height = 850 };
        Capture(window, "batch-rename");
        window.Close();
    }
}

public class RenderProxy : DispatchProxy
{
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        var type = targetMethod!.ReturnType;
        if(targetMethod.Name=="IsPluginEnabled") return true;
        if (type == typeof(void)) return null;
        if (type == typeof(Task)) return Task.CompletedTask;
        if (type == typeof(ValueTask)) return ValueTask.CompletedTask;
        if(type == typeof(List<WindowInfo>)) return new List<WindowInfo>();
        if(type.IsGenericType && type.GetGenericTypeDefinition()==typeof(IEnumerable<>)) return Array.CreateInstance(type.GetGenericArguments()[0],0);
        return type.IsValueType ? Activator.CreateInstance(type) : null;
    }
}
