#region

using System.Text;
using System.Text.RegularExpressions;
using Core.Services;
using Core.Services.Config;
using Core.Services.Interfaces;
using Core.Utils;
using Core.Window.Everything;
using Microsoft.Extensions.DependencyInjection;
using Pinyin.NET;
using PluginCore;
using Serilog;
using Vanara.Windows.Shell;
using File = System.IO.File;

#endregion

namespace Core.Window.AppTools;

public class AppSolver
{
    private static readonly ILogger Logger = LogManager.Logger.ForContext<AppSolver>();
    private static readonly List<string> ErrorLnkList = new();
    public static readonly PinyinProcessor PinyinProcessor = new();

    internal static void AutoStartEverything(SearchIndex index, Action action)
    {
        if (ConfigManger.Config.autoStartEverything)
        {
            if (string.IsNullOrWhiteSpace(ConfigManger.Config.everythingOnlyKey))
                foreach (var (key, _) in index.GetEntriesSnapshot())
                    if (key.Contains("Everything.exe"))
                    {
                        ConfigManger.Config.everythingOnlyKey = key;
                        ConfigManger.Save();
                        break;
                    }

            if (index.TryGetValue(ConfigManger.Config.everythingOnlyKey, out var entry))
            {
                var isRun = ServiceManager.Services.GetService<IEverythingService>()!
                    .IsRun();


                if (!isRun)
                {
                    var 程序名称 = "noUAC.Everything";
                    if (!File.Exists(
                            $"{AppDomain.CurrentDomain.BaseDirectory}noUAC{Path.DirectorySeparatorChar}{程序名称}.lnk"))
                    {
                        var dialog = new DialogContent
                        {
                            Title = "Kitopia提示",
                            Content =
                                $"Kitopia即将使用任务计划来创建绕过UAC启动Everything的快捷方式\n需要确认UAC权限\n按下取消则关闭自动启动功能\n路径:{AppDomain.CurrentDomain.BaseDirectory}noUAC{Path.DirectorySeparatorChar}{程序名称}.lnk",
                            PrimaryButtonText = "确定",
                            CloseButtonText = "取消",
                            PrimaryAction = () =>
                            {
                                Directory.CreateDirectory(AppDomain.CurrentDomain.BaseDirectory + "noUAC");
                                var tempFileName =
                                    $"{AppDomain.CurrentDomain.BaseDirectory}noUAC{Path.DirectorySeparatorChar}{程序名称}.xml";
                                var xmlText =
                                    $"<?xml version=\"1.0\" encoding=\"UTF-16\"?>\n<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\n  <Triggers />\n  <Principals>\n    <Principal id=\"Author\">\n      <LogonType>InteractiveToken</LogonType>\n      <RunLevel>HighestAvailable</RunLevel>\n    </Principal>\n  </Principals>\n  <Settings>\n    <MultipleInstancesPolicy>Parallel</MultipleInstancesPolicy>\n    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>\n    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>\n    <AllowHardTerminate>false</AllowHardTerminate>\n    <StartWhenAvailable>false</StartWhenAvailable>\n    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>\n    <IdleSettings>\n      <StopOnIdleEnd>false</StopOnIdleEnd>\n      <RestartOnIdle>false</RestartOnIdle>\n    </IdleSettings>\n    <AllowStartOnDemand>true</AllowStartOnDemand>\n    <Enabled>true</Enabled>\n    <Hidden>false</Hidden>\n    <RunOnlyIfIdle>false</RunOnlyIfIdle>\n    <WakeToRun>false</WakeToRun>\n    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>\n    <Priority>7</Priority>\n  </Settings>\n  <Actions Context=\"Author\">\n    <Exec>{Environment.NewLine}      <Command>\"{entry.OnlyKey}\"</Command>{Environment.NewLine}      <Arguments>-startup</Arguments>{Environment.NewLine}    </Exec>\n  </Actions>\n</Task>";
                                File.WriteAllText(tempFileName, xmlText, Encoding.Unicode);

                                ServiceManager.Services.GetService<IShellUtils>()!.RunAsAdmin("schtasks.exe",
                                    $"/create /tn \"{程序名称}\" /xml \"{tempFileName}\"");
                                var shellLink = ShellLink.Create(
                                    $"{AppDomain.CurrentDomain.BaseDirectory}noUAC\\{程序名称}.lnk",
                                    "schtasks.exe", null, null, $"/run /tn \"{程序名称}\"");
                                shellLink.IconLocation = new IconLocation(entry.OnlyKey, 0);

                                Thread.Sleep(200);
                                File.Delete(tempFileName);
                                Logger.Debug("创建Everything的noUAC任务计划完成");
                                ServiceManager.Services.GetService<IShellUtils>()!.Open(
                                    $"{AppDomain.CurrentDomain.BaseDirectory}noUAC{Path.DirectorySeparatorChar}{程序名称}.lnk");
                                action.Invoke();
                            },
                            CloseAction = () =>
                            {
                                Logger.Debug("关闭自动启动Everything功能");
                                ConfigManger.Config.autoStartEverything = false;
                                ConfigManger.Save();
                            }
                        };
                        ((IToastService)ServiceManager.Services.GetService(typeof(IToastService))!).Show(
                            dialog.ToToastRequest());
                    }
                    else

                    {
                        ServiceManager.Services.GetService<IShellUtils>()!.Open(
                            $"{AppDomain.CurrentDomain.BaseDirectory}noUAC{Path.DirectorySeparatorChar}{程序名称}.lnk");
                    }
                }
            }
        }
    }

    internal static void CleanupInvalidItems(SearchIndex index)
    {
        index.RemoveWhere((key, entry) =>
        {
            return entry.FileType switch
            {
                FileType.文件 or FileType.Excel文档 or FileType.Word文档 or FileType.PDF文档 or FileType.PPT文档 =>
                    !File.Exists(entry.OnlyKey),
                FileType.文件夹 =>
                    !Directory.Exists(entry.OnlyKey),
                _ => false
            };
        });
    }

    internal static void IndexAllApps(SearchIndex index,
        bool logging = false, bool useEverything = false)
    {
        Logger.Debug("索引全部软件及收藏项目");


        UwpTools.GetAll(index);
        Logger.Debug("索引全部软件及收藏项目UWP");
        ControlPanelTools.GetAll(index);


        foreach (var enumerateFile in Directory.EnumerateFiles(
                     Environment.GetFolderPath(Environment.SpecialFolder.Desktop)))
            IndexItem(index, enumerateFile, logging: logging);

        foreach (var enumerateFile in Directory.EnumerateDirectories(
                     Environment.GetFolderPath(Environment.SpecialFolder.Desktop)))
            IndexItem(index, enumerateFile, logging: logging);

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

            IndexItem(index, enumerateFile, logging: logging);
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

            IndexItem(index, enumerateFile, logging: logging);
        }

        foreach (var configCustomCollection in ConfigManger.Config.customCollections)
            IndexItem(index, configCustomCollection, logging: logging);

        if (useEverything)
        {
            List<string> filePaths = new();
            EverythingTools.Index(filePaths);
            foreach (var filePath in filePaths) IndexItem(index, filePath, logging: logging);

            filePaths.Clear();
        }


        if (ErrorLnkList.Any())
        {
            var c = new StringBuilder("检测到多个无效的快捷方式\n需要Kitopia帮你清理吗?(该功能每个错误快捷方式只提示一次)\n以下为无效的快捷方式列表:\n");
            foreach (var s in ErrorLnkList) c.AppendLine(s);

            Logger.Debug(c.ToString());
            var dialog = new DialogContent
            {
                Title = "Kitopia建议",
                Content = c.ToString(),
                PrimaryButtonText = "确定",
                SecondaryButtonText = "取消",
                PrimaryAction = () =>
                {
                    foreach (var s in ErrorLnkList)
                    {
                        Logger.Debug($"删除无效快捷方式:{s}");
                        try
                        {
                            File.Delete(s);
                        }
                        catch (Exception)
                        {
                            Logger.Debug($"添加无效快捷方式记录:{s}");
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
                        Logger.Debug($"添加无效快捷方式记录:{s}");
                        ConfigManger.Config.errorLnk.Add(s);
                        ConfigManger.Save();
                    }

                    Logger.Debug("取消删除无效快捷方式");
                    ErrorLnkList.Clear();
                }
            };
            ((IToastService)ServiceManager.Services.GetService(typeof(IToastService))!).Show(
                dialog.ToToastRequest());
        }
    }

    internal static void IndexItem(SearchIndex index, string file,
        bool star = false, bool logging = false)
    {

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
                        var shellItem = new ShellLink(file);
                        localizedName = shellItem.Name;
                        var targetPath = shellItem.ShortTargetPath;
                        if (string.IsNullOrWhiteSpace(targetPath)||string.IsNullOrWhiteSpace(localizedName))
                        {
                            return;
                        }

                        var refFileInfo = new FileInfo(targetPath);
                        var fullName = refFileInfo.FullName;
                        if (ConfigManger.Config.ignoreItems.Contains(fullName))
                        {
                            Logger.Debug($"忽略索引:{fullName}");
                            return;
                        }

                        if (refFileInfo.Exists)
                        {
                            if (index.ContainsKey(fullName)) return;
                        }
                        else
                        {
                            Logger.Debug($"无效索引:\n{file}\n目标位置:{fullName}");
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
                                index.TryAdd(new SearchEntry
                                {
                                    DisplayName = localizedName,
                                    OnlyKey = fullName, Arguments = shellItem.Arguments,
                                    FileType = FileType.应用程序,
                                    StartDirectory = shellItem.WorkingDirectory
                                });
                            }

                        }

                        break;
                    }
                    case ".url":
                    {
                        var url = "";
                        var relFile = "";
                        var fileContent = File.ReadAllText(file);
                        var pattern = @"URL=(.*)";
                        var match = Regex.Match(fileContent, pattern,
                            RegexOptions.NonBacktracking);
                        if (match.Success)
                            url = match.Groups[1]
                                .Value.Replace("\r", "");

                        var onlyKey = url;
                        if (index.ContainsKey(onlyKey)) return;

                        if (ConfigManger.Config.ignoreItems.Contains(onlyKey))
                        {
                            Logger.Debug($"忽略索引:{onlyKey}");
                            return;
                        }

                        var pattern2 = @"IconFile=(.*)";
                        var match2 =
                            Regex.Match(fileContent, pattern2, RegexOptions.NonBacktracking);
                        if (match2.Success)
                            relFile = match2.Groups[1]
                                .Value.Replace("\r", "");

                        if (string.IsNullOrWhiteSpace(relFile)) return;

                        {
                            index.TryAdd(new SearchEntry
                            {
                                DisplayName = localizedName,
                                OnlyKey = onlyKey,
                                IconPath = relFile,
                                FileType = FileType.URL
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

                            index.TryAdd(new SearchEntry
                            {
                                DisplayName = localizedName,
                                FileType = fileType,
                                OnlyKey = file
                            });
                        }

                        break;
                }
            }
            else
            {
                if (!Directory.Exists(file)) return;
                if (ConfigManger.Config.ignoreItems.Contains(file)) return;

                index.TryAdd(new SearchEntry
                {
                    DisplayName = file.Split(Path.DirectorySeparatorChar)
                        .Last(),
                    FileType = FileType.文件夹,
                    OnlyKey = file
                });
            }
        }
        catch (Exception e)
        {
            Logger.Error(e, $"索引失败:{file}");
        }
    }
}
