#region

using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Text.RegularExpressions;
using Core.SDKs;
using Core.SDKs.Services;
using Core.SDKs.Services.Config;
using Core.Window.Everything;

using Microsoft.Extensions.DependencyInjection;
using Pinyin.NET;
using PluginCore;
using Serilog;
using Vanara.PInvoke;
using Vanara.Windows.Shell;
using File = System.IO.File;

#endregion

namespace Core.Window;

public partial class AppTools
{
    private static ILogger Log =   LogManager.Logger.ForContext<AppTools>();
    private static readonly List<string> ErrorLnkList = new();

    //Console.WriteLine();


    public static PinyinProcessor _pinyinProcessor = new();

    internal static void AutoStartEverything(ConcurrentDictionary<string, SearchViewItem> collection, Action action)
    {
        if (ConfigManger.Config.autoStartEverything)
        {
            if (string.IsNullOrWhiteSpace(ConfigManger.Config.everythingOnlyKey))
                foreach (var (key, value) in collection)
                    if (key.Contains("Everything.exe"))
                    {
                        ConfigManger.Config.everythingOnlyKey = key;
                        ConfigManger.Save();
                        break;
                    }

            if (collection.TryGetValue(ConfigManger.Config.everythingOnlyKey, out var searchViewItem))
            {
                var isRun = ServiceManager.Services.GetService<IEverythingService>()
                    .IsRun();


                if (!isRun)
                {
                    var 程序名称 = "noUAC.Everything";
                    if (!File.Exists(
                            $"{AppDomain.CurrentDomain.BaseDirectory}noUAC{Path.DirectorySeparatorChar}{程序名称}.lnk"))
                    {
                        var dialog = new DialogContent()
                        {
                            Title = $"Kitopia提示",
                            Content =
                                $"Kitopia即将使用任务计划来创建绕过UAC启动Everything的快捷方式\n需要确认UAC权限\n按下取消则关闭自动启动功能\n路径:{AppDomain.CurrentDomain.BaseDirectory}noUAC{Path.DirectorySeparatorChar}{程序名称}.lnk",
                            PrimaryButtonText = "确定",
                            CloseButtonText = "取消",
                            PrimaryAction = () =>
                            {
                                Directory.CreateDirectory(AppDomain.CurrentDomain.BaseDirectory + "noUAC");
                                var TempFileName =
                                    $"{AppDomain.CurrentDomain.BaseDirectory}noUAC{Path.DirectorySeparatorChar}{程序名称}.xml";
                                var XML_Text =
                                    $"<?xml version=\"1.0\" encoding=\"UTF-16\"?>\n<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\n  <Triggers />\n  <Principals>\n    <Principal id=\"Author\">\n      <LogonType>InteractiveToken</LogonType>\n      <RunLevel>HighestAvailable</RunLevel>\n    </Principal>\n  </Principals>\n  <Settings>\n    <MultipleInstancesPolicy>Parallel</MultipleInstancesPolicy>\n    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>\n    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>\n    <AllowHardTerminate>false</AllowHardTerminate>\n    <StartWhenAvailable>false</StartWhenAvailable>\n    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>\n    <IdleSettings>\n      <StopOnIdleEnd>false</StopOnIdleEnd>\n      <RestartOnIdle>false</RestartOnIdle>\n    </IdleSettings>\n    <AllowStartOnDemand>true</AllowStartOnDemand>\n    <Enabled>true</Enabled>\n    <Hidden>false</Hidden>\n    <RunOnlyIfIdle>false</RunOnlyIfIdle>\n    <WakeToRun>false</WakeToRun>\n    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>\n    <Priority>7</Priority>\n  </Settings>\n  <Actions Context=\"Author\">\n    <Exec>{Environment.NewLine}      <Command>\"{searchViewItem.OnlyKey}\"</Command>{Environment.NewLine}      <Arguments>-startup</Arguments>{Environment.NewLine}    </Exec>\n  </Actions>\n</Task>";
                                File.WriteAllText(TempFileName, XML_Text, Encoding.Unicode);

                                Shell32.ShellExecute(IntPtr.Zero, "runas", "schtasks.exe",
                                    $"/create /tn \"{程序名称}\" /xml \"{@TempFileName}\"", "",
                                    ShowWindowCommand.SW_HIDE);
                                var shellLink = ShellLink.Create(
                                    $"{AppDomain.CurrentDomain.BaseDirectory}noUAC\\{程序名称}.lnk",
                                    "schtasks.exe", null, null, $"/run /tn \"{程序名称}\"");
                                shellLink.IconLocation = new IconLocation(searchViewItem.OnlyKey, 0);

                                Thread.Sleep(200);
                                File.Delete(TempFileName);
                                Log.Debug("创建Everything的noUAC任务计划完成");
                                Shell32.ShellExecute(IntPtr.Zero, "open",
                                    $"{AppDomain.CurrentDomain.BaseDirectory}noUAC{Path.DirectorySeparatorChar}{程序名称}.lnk",
                                    "", "",
                                    ShowWindowCommand.SW_HIDE);
                                action.Invoke();
                            },
                            CloseAction = () =>
                            {
                                Log.Debug("关闭自动启动Everything功能");
                                ConfigManger.Config.autoStartEverything = false;
                                ConfigManger.Save();
                            }
                        };
                        ((IContentDialog)ServiceManager.Services!.GetService(typeof(IContentDialog))!).ShowDialogAsync(
                            null,
                            dialog);
                    }
                    else

                    {
                        Shell32.ShellExecute(IntPtr.Zero, "open",
                            $"{AppDomain.CurrentDomain.BaseDirectory}noUAC{Path.DirectorySeparatorChar}{程序名称}.lnk", "",
                            "",
                            ShowWindowCommand.SW_HIDE);
                    }
                }
            }
        }
    }

    internal static void DelNullFile(ConcurrentDictionary<string, SearchViewItem> collection)
    {
        var toRemove = new List<string>();
        foreach (var (key, searchViewItem) in collection)
            switch (searchViewItem.FileType)
            {
                case FileType.文件:
                case FileType.Excel文档:
                case FileType.Word文档:
                case FileType.PDF文档:
                case FileType.PPT文档:
                {
                    if (!File.Exists(searchViewItem.OnlyKey)) toRemove.Add(key);
                    //collection.Remove(searchViewItem);
                    break;
                }
                case FileType.文件夹:
                {
                    if (!Directory.Exists(searchViewItem.OnlyKey)) toRemove.Add(key);
                    //collection.Remove(searchViewItem);
                    break;
                }
            }

        foreach (var searchViewItem in toRemove) collection.TryRemove(searchViewItem, out _);
    }

    internal static void GetAllApps(ConcurrentDictionary<string, SearchViewItem> collection,
        bool logging = false, bool useEverything = false)
    {
        Log.Debug("索引全部软件及收藏项目");


        UwpTools.GetAll(collection);
        Log.Debug("索引全部软件及收藏项目UWP");

        // 创建一个空的文件路径集合


        foreach (var enumerateFile in Directory.EnumerateFiles(
                     Environment.GetFolderPath(Environment.SpecialFolder.Desktop)))
            AppSolverA(collection, enumerateFile, logging: logging);

        foreach (var enumerateFile in Directory.EnumerateDirectories(
                     Environment.GetFolderPath(Environment.SpecialFolder.Desktop)))
            AppSolverA(collection, enumerateFile, logging: logging);

        foreach (var enumerateFile in Directory.EnumerateFiles(@"C:\ProgramData\Microsoft\Windows\Start Menu\Programs",
                     "*", SearchOption.AllDirectories))
        {
            switch (enumerateFile.Split(".")
                        .Last())
            {
                case "lnk":
                case "url":
                case "appref-ms":
                    break;
                default:
                    continue;
            }

            AppSolverA(collection, enumerateFile, logging: logging);
        }

        var folderPath = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        foreach (var enumerateFile in
                 Directory.EnumerateFiles(folderPath
                     , "*", SearchOption.AllDirectories))
        {
            switch (enumerateFile.Split(".")
                        .Last())
            {
                case "lnk":
                case "url":
                case "appref-ms":
                    break;
                default:
                    continue;
            }

            AppSolverA(collection, enumerateFile, logging: logging);
        }

        foreach (var configCustomCollection in ConfigManger.Config.customCollections)
            AppSolverA(collection, configCustomCollection, logging: logging);

        if (useEverything)
        {
            List<string> filePaths = new();
            EverythingTools.Index(filePaths);
            foreach (var filePath in filePaths) AppSolverA(collection, filePath, logging: logging);

            filePaths.Clear();
        }


        //AutoStartEverything(collection);
        if (ErrorLnkList.Any())
        {
            var c = new StringBuilder("检测到多个无效的快捷方式\n需要Kitopia帮你清理吗?(该功能每个错误快捷方式只提示一次)\n以下为无效的快捷方式列表:\n");
            foreach (var s in ErrorLnkList) c.AppendLine(s);

            Log.Debug(c.ToString());
            var dialog = new DialogContent()
            {
                Title = $"Kitopia建议",
                Content = c.ToString(),
                PrimaryButtonText = "确定",
                SecondaryButtonText = "取消",
                PrimaryAction = () =>
                {
                    foreach (var s in ErrorLnkList)
                    {
                        Log.Debug($"删除无效快捷方式:{s}");
                        try
                        {
                            File.Delete(s);
                        }
                        catch (Exception)
                        {
                            Log.Debug($"添加无效快捷方式记录:{s}");
                            ConfigManger.Config.errorLnk.Add(s);
                            ConfigManger.Save();
                        }
                    }

                    ErrorLnkList.Clear();
                },
                SecondaryAction = () =>
                {
                    foreach (var s in ErrorLnkList)
                    {
                        Log.Debug($"添加无效快捷方式记录:{s}");
                        ConfigManger.Config.errorLnk.Add(s);
                        ConfigManger.Save();
                    }

                    Log.Debug("取消删除无效快捷方式");
                    ErrorLnkList.Clear();
                }
            };
            ((IContentDialog)ServiceManager.Services!.GetService(typeof(IContentDialog))!).ShowDialogAsync(null,
                dialog);
        }
    }

    internal static void AppSolverA(ConcurrentDictionary<string, SearchViewItem> collection, string file,
        bool star = false, bool logging = false)
    {
        //Log.Debug(Thread.CurrentThread.ManagedThreadId);

        try
        {
            var localizedName = file.Split("\\")
                .Last();
            var lastIndexOf = localizedName.LastIndexOf(".", StringComparison.Ordinal);
            if (lastIndexOf != -1) localizedName = localizedName.Remove(lastIndexOf);


            if (Path.HasExtension(file))
            {
                var fileInfo = new FileInfo(file);
                switch (fileInfo.Extension)
                {
                    case ".lnk":
                    {
                        localizedName = Shell32.SHCreateItemFromParsingName<Shell32.IShellItem>(file)
                            .GetDisplayName(Shell32.SIGDN.SIGDN_NORMALDISPLAY);
                        //var sb = new StringBuilder(260);
                        // var shellLink = new ShellLink(file, LinkResolution.None);
                        var link = new Shell32.IShellLinkW();
                        ((IPersistFile)link).Load(file, (int)STGM.STGM_READ);
                        var sb = new StringBuilder(260);
                        var data = new WIN32_FIND_DATA();
                        //((IShellLinkW)link).GetShowCmd
                        ((Shell32.IShellLinkW)link).GetPath(sb, sb.Capacity, out data, 0);
                        var argSb = new StringBuilder(260);
                        link.GetArguments(argSb, argSb.Capacity);
                        var workingDirectorySb = new StringBuilder(260);
                        link.GetWorkingDirectory(workingDirectorySb, workingDirectorySb.Capacity);
                        var arg = argSb.Length > 0 ? argSb.ToString() : null;
                        if (arg != null && arg.Contains('%'))
                        {
                            var regex = new Regex("%(\\w+)%");

                            // 替换后的字符串
                            arg = regex.Replace(arg, match =>
                            {
                                // 获取匹配到的环境变量名称
                                var variable = match.Groups[1].Value;

                                // 获取环境变量值
                                var value = Environment.GetEnvironmentVariable(variable);


                                // 返回替换后的值
                                return value;
                            });
                        }

                        var targetPath = sb.ToString() ?? file;

                        if (string.IsNullOrWhiteSpace(targetPath)) targetPath = file;

                        if (!File.Exists(targetPath))
                        {
                            if (File.Exists(targetPath.Replace("Program Files (x86)", "Program Files")))
                            {
                                targetPath = targetPath.Replace("Program Files (x86)", "Program Files");
                                goto next;
                            }

                            if (File.Exists(targetPath.Replace("Program Files", "Program Files (x86)")))
                            {
                                targetPath = targetPath.Replace("Program Files", "Program Files (x86)");
                                goto next;
                            }

                            if (File.Exists(targetPath.Replace("system32", "sysnative")))
                            {
                                targetPath = targetPath.Replace("system32", "sysnative");
                                goto next;
                            }
                        }

                        next:
                        var refFileInfo = new FileInfo(targetPath);
                        var fullName = refFileInfo.FullName;
                        if (ConfigManger.Config.ignoreItems.Contains(fullName))
                        {
                            Log.Debug($"忽略索引:{fullName}");
                            return;
                        }

                        if (refFileInfo.Exists)
                        {
                            if (collection.ContainsKey(fullName)) return;
                        }
                        else
                        {
                            Log.Debug($"无效索引:\n{file}\n目标位置:{fullName}");
                            if (!ErrorLnkList.Contains(file) && !ConfigManger.Config.errorLnk.Contains(file))
                                ErrorLnkList.Add(file);

                            return;
                        }


                        var extension = refFileInfo.Extension;
                        if (extension != ".url" && extension != ".txt" && extension != ".chm" &&
                            !refFileInfo.Name.Contains("powershell.exe") && !refFileInfo.Name.Contains("cmd.exe") &&
                            extension != ".pdf" && extension != ".bat" &&
                            !fileInfo.Name.Contains("install") &&
                            !fileInfo.Name.Contains("安装") && !fileInfo.Name.Contains("卸载"))
                        {
                            {
                                collection.TryAdd(fullName, new SearchViewItem
                                {
                                    PinyinItem = _pinyinProcessor.GetPinyin(localizedName, true),
                                    IsVisible = true, ItemDisplayName = localizedName,
                                    OnlyKey = fullName, IsStared = star, Arguments = arg,
                                    FileType = FileType.应用程序, Icon = null,
                                    StartDirectory = workingDirectorySb.ToString()
                                });
                            }

                            //Log.Debug($"完成索引:{file}");
                        }
                        else
                        {
                            // Log.Debug($"不符合要求跳过索引:{file}");
                        }

                        break;
                    }
                    case ".url":
                    {
                        var url = "";
                        var relFile = "";
                        var fileContent = File.ReadAllText(file); // read the file content
                        var pattern = @"URL=(.*)"; // the regex pattern to match the url
                        var match = Regex.Match(fileContent, pattern,
                            RegexOptions.NonBacktracking); // match the pattern
                        if (match.Success) // if a match is found
                            url = match.Groups[1]
                                .Value.Replace("\r", ""); // get the url from the first group

                        var onlyKey = url;
                        if (collection.ContainsKey(onlyKey)) return;

                        if (ConfigManger.Config.ignoreItems.Contains(onlyKey))
                        {
                            Log.Debug($"忽略索引:{onlyKey}");
                            return;
                        }

                        var pattern2 = @"IconFile=(.*)"; // the regex pattern to match the url
                        var match2 =
                            Regex.Match(fileContent, pattern2, RegexOptions.NonBacktracking); // match the pattern
                        if (match2.Success) // if a match is found
                            relFile = match2.Groups[1]
                                .Value.Replace("\r", ""); // get the url from the first group

                        if (string.IsNullOrWhiteSpace(relFile)) return;

                        {
                            collection.TryAdd(onlyKey, new SearchViewItem
                            {
                                PinyinItem = _pinyinProcessor.GetPinyin(localizedName, true),
                                IsVisible = true, ItemDisplayName = localizedName,
                                OnlyKey = onlyKey, IsStared = star,
                                IconPath = relFile,
                                FileType = FileType.URL, Icon = null
                            });
                        }


                        break;
                    }
                    default:
                        if (File.Exists(file))
                        {
                            if (ConfigManger.Config.ignoreItems.Contains(file)) return;

                            var fileType = FileType.文件;
                            switch (fileInfo.Extension)
                            {
                                case ".exe":
                                    fileType = FileType.应用程序;
                                    break;
                                case ".pdf":
                                    fileType = FileType.PDF文档;
                                    break;
                                case ".doc":
                                case ".docx":
                                    fileType = FileType.Word文档;
                                    break;
                                case ".xls":
                                case ".xlsx":
                                    fileType = FileType.Excel文档;
                                    break;
                                case ".ppt":
                                case ".pptx":
                                    fileType = FileType.PPT文档;
                                    break;
                            }

                            collection.TryAdd(file, new SearchViewItem()
                            {
                                ItemDisplayName = localizedName,
                                FileType = fileType,

                                OnlyKey = file,
                                PinyinItem = _pinyinProcessor.GetPinyin(localizedName, true),
                                IsStared = star,
                                IsVisible = true
                            });
                        }

                        break;
                }
            }
            else
            {
                if (!Directory.Exists(file)) return;
                if (ConfigManger.Config.ignoreItems.Contains(file)) return;

                collection.TryAdd(file, new SearchViewItem()
                {
                    ItemDisplayName = file.Split(Path.DirectorySeparatorChar)
                        .Last(),
                    FileType = FileType.文件夹,
                    IsStared = star,
                    OnlyKey = file,
                    PinyinItem = _pinyinProcessor.GetPinyin(file.Split(Path.DirectorySeparatorChar)
                        .Last(), true),
                    Icon = null,
                    IsVisible = true
                });
            }
        }
        catch (Exception e)
        {
            Log.Error(e,$"索引失败:{file}");
            
        }
    }
    
    // 使用const或readonly修饰符来声明pattern字符串
    internal static PinyinItem NameSolver(string name)
    {
        return _pinyinProcessor.GetPinyin(name, true);
    }

   
}