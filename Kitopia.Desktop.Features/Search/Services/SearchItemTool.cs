using System.Diagnostics;
using Kitopia.Desktop.Features.CustomScenario;
using Kitopia.Desktop.Features.Services;
using Kitopia.Desktop.Features.Services.Config;
using Kitopia.Desktop.Features.Services.Interfaces;
using Kitopia.Desktop.Abstractions.Shell;
using Kitopia.Desktop.Features.Search.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using Serilog;

namespace Kitopia.Desktop.Features.Search.Services;

public class SearchItemTool : ISearchItemTool
{
    private static ILogger Logger = LogManager.Logger.ForContext<SearchItemTool>();
    
    private IDesktopShell DesktopShell => ServiceManager.Services.GetRequiredService<IDesktopShell>();

    public void OpenFile(SearchViewItem? searchViewItem, params object[] inputValues)
    {
        if (searchViewItem is null) return;
        Logger.Debug(
            $"打开指定内容{searchViewItem.OnlyKey}_{searchViewItem.Arguments}_{searchViewItem.StartDirectory}");
        switch (searchViewItem.OnlyKey)
        {
            case "Math": break;
            default:
            {
                switch (searchViewItem.FileType)
                {
                    case FileType.UWP应用:
                        // "explorer.exe", $"shell:AppsFolder\{searchViewItem.OnlyKey}"
                        DesktopShell.Open("explorer.exe", $"shell:AppsFolder\\{searchViewItem.OnlyKey}");
                        break;
                    case FileType.自定义情景:
                        CustomScenarioManger.CustomScenarios
                            .FirstOrDefault(e => $"CustomScenario:{e.Uuid}" == searchViewItem.OnlyKey)
                            ?.Run(inputValues: inputValues);
                        break;
                    case FileType.便签:
                        ((ILabelWindowService)ServiceManager.Services.GetService(typeof(ILabelWindowService))!) 
                            .Show(searchViewItem.OnlyKey);
                        break;
                    case FileType.自定义:
                    case FileType.窗口:
                        searchViewItem.Action?.Invoke(searchViewItem, inputValues.Length > 0 ? inputValues[0] as string : null);
                        break;
                    case FileType.命令:
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "cmd.exe",
                            Arguments = $"/c {searchViewItem.OnlyKey} & pause",
                            UseShellExecute = false,
                            CreateNoWindow = false
                        });
                        break;
                    }
                    case FileType.数学运算:
                    {
                        var thread = new Thread(() =>
                        {
                            var remove = searchViewItem.ItemDisplayName!.Remove(0, 1);
                            ServiceManager.Services.GetService<IClipboardService>()!.SetText(remove);
                            ServiceManager.Services.GetService<IToastService>()!.Show("Kitopia",
                                $"计算结果{remove}已经复制到剪贴板");
                        });
                        thread.SetApartmentState(ApartmentState.STA);
                        thread.Start();
                        break;
                    }

                    case FileType.文件夹:
                    {
                        if (searchViewItem.Arguments == null)
                            DesktopShell.Open(searchViewItem.OnlyKey, "", 
                                searchViewItem.OnlyKey.Remove(searchViewItem.OnlyKey.LastIndexOf('\\'))); 
                        else
                            DesktopShell.Open(searchViewItem.OnlyKey, searchViewItem.Arguments,
                                searchViewItem.OnlyKey.Remove(searchViewItem.OnlyKey.LastIndexOf('\\')));

                        break;
                    }
                    default:
                        if (string.IsNullOrWhiteSpace(searchViewItem.LaunchPath))
                            DesktopShell.Open(searchViewItem.OnlyKey, searchViewItem.Arguments,
                                searchViewItem.StartDirectory);
                        else
                            DesktopShell.Open(searchViewItem.LaunchPath);
                        break;
                }
                RecordOpenTime(searchViewItem);
                return;
            }
        }
    }

   

    public void IgnoreItem(SearchViewItem? item)
    {
        if (item is null)
        {
            return;
        }
        Task.Run(() =>
        {
            ConfigManger.Config.ignoreItems.Add(item.OnlyKey);
            ConfigManger.Save();
            ServiceManager.Services.GetService<SearchWindowViewModel>()!.Index
                .TryRemoveAndRefreshSearcher(item.OnlyKey);
        });
    }

    public void OpenFolder(SearchViewItem? searchViewItem)
    {
        if (searchViewItem is null)
        {
            return;
        }

        Task.Run(() =>
        {
            Logger.Debug($"打开指定内容文件夹{searchViewItem.OnlyKey}_{searchViewItem.StartDirectory}");
            var path = searchViewItem.OnlyKey;
            
            DesktopShell.OpenFolderAndSelect(path);

            RecordOpenTime(searchViewItem);
        });
    }

    public void RunAsAdmin(SearchViewItem? item)
    {
        if (item is null)
        {
            return;
        }
        Task.Run(() =>
        {
            Logger.Debug("以管理员身份打开指定内容" + item?.OnlyKey);
            if (item is { FileType: FileType.UWP应用 })
                // "explorer.exe", $"shell:AppsFolder\{item.OnlyKey}!App"
                DesktopShell.RunAsAdmin("explorer.exe", $"shell:AppsFolder\\{item.OnlyKey}!App");
            else
                DesktopShell.RunAsAdmin(item.OnlyKey);

            RecordOpenTime(item);
        });
    }

    public void Star(SearchViewItem? item)
    {
        if (item is null) return;

        var index = ServiceManager.Services.GetService<SearchWindowViewModel>()!.Index;
        Logger.Information("添加/移除收藏" + item.OnlyKey);
        item.IsStared = !item.IsStared;
        if (ConfigManger.Config!.customCollections.Contains(item.OnlyKey))
            ConfigManger.Config.customCollections.Remove(item.OnlyKey);

        if (item.IsStared)
        {
            ServiceManager.Services.GetService<IAppToolService>()!.IndexItem(index, item.OnlyKey, true);
            ConfigManger.Config.customCollections.Insert(0, item.OnlyKey);
        }
        else
        {
            index.TryRemove(item.OnlyKey);
        }

        index.RebuildSearcher();

        ConfigManger.Save();
    }

    public void Pin(SearchViewItem? item)
    {
        if (ConfigManger.Config.alwayShows.Contains(item.OnlyKey))
        {
            item.IsPined = false;
            ConfigManger.Config.alwayShows.Remove(item.OnlyKey);
        }
        else
        {
            item.IsPined = true;
            ConfigManger.Config.alwayShows.Insert(0, item.OnlyKey);
        }
           
        ConfigManger.Save();
    }

    public void OpenFolderInTerminal(SearchViewItem? item)
    {
        if (item is null)
        {
            return;
        }
        Task.Run(() =>
        {
            Logger.Debug("打开指定内容在终端中" + item.OnlyKey);
            var startInfo = new ProcessStartInfo
            {
                FileName = @"C:\Windows\System32\cmd.exe"
            };
            if (!File.Exists(@"C:\Windows\System32\cmd.exe"))
            {
                Logger.Debug("64");
                startInfo.FileName = @"C:\Windows\sysnative\cmd.exe";
            }

            if (item.FileType == FileType.文件夹) startInfo.WorkingDirectory = item.OnlyKey;

            if (item.FileType is FileType.文件 or FileType.Excel文档 or FileType.Word文档 or FileType.PDF文档 or FileType.PPT文档)
                startInfo.WorkingDirectory = item.OnlyKey[..item.OnlyKey.LastIndexOf('\\')];

            Process.Start(startInfo);

            RecordOpenTime(item);
        });
    }

    private static void RecordOpenTime(SearchViewItem item)
    {
        switch (item.FileType)
        {
            case FileType.文件夹:
            case FileType.应用程序:
            case FileType.Word文档:
            case FileType.PPT文档:
            case FileType.Excel文档:
            case FileType.PDF文档:
            case FileType.图像:
            case FileType.文件:
            {
                if (ConfigManger.Config.lastOpens.ContainsKey(item.OnlyKey))
                {
                    ConfigManger.Config.lastOpens[item.OnlyKey].AccessTimes.Add(DateTime.Now);
                    ConfigManger.Config.lastOpens[item.OnlyKey].AccessTimes.RemoveAll(t => (DateTime.Now - t).TotalDays > 30);
                }
                else
                {
                    ConfigManger.Config.lastOpens.Add(item.OnlyKey,
                        new HistoryItem { AccessTimes = { DateTime.Now } });
                }

                break;
            }
                ;
        }
        ConfigManger.Save();
    }

    public void OpenSearchItemByOnlyKey(string onlyKey, params object[] inputValues)
    {
        if (ServiceManager.Services!.GetService<SearchWindowViewModel>()!.Index
            .TryGetValue(onlyKey, out var entry))
            OpenFile(entry.ToSearchViewItem(), inputValues);
    }
}
