using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.Messaging;
using Core.CustomScenario;
using Core.SDKs.CustomScenario;
using Core.SDKs.Services;
using Core.Services;
using Core.Services.Config;
using Core.ViewModel;
using Core.ViewModel.Windows;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using Serilog;
using Vanara.PInvoke;

namespace Core.Window;

public class SearchItemTool : ISearchItemTool
{
    
    private static ILogger Log =   LogManager.Logger.ForContext<SearchItemTool>();
    public void OpenFile(SearchViewItem? searchViewItem, params object[] inputValues)
    {
        if (searchViewItem is null) return;
        Log.Debug("打开指定内容" + searchViewItem.OnlyKey);
        switch (searchViewItem.OnlyKey)
        {
            case "Math": break;
            default:
            {
                switch (searchViewItem.FileType)
                {
                    case FileType.UWP应用:
                        Shell32.ShellExecute(IntPtr.Zero, "open", "explorer.exe",
                            $"shell:AppsFolder\\{searchViewItem.OnlyKey}", "",
                            ShowWindowCommand.SW_NORMAL);
                        break;
                    case FileType.自定义情景:
                        CustomScenarioManger.CustomScenarios
                            .FirstOrDefault((e) => $"CustomScenario:{e.UUID}" == searchViewItem.OnlyKey)
                            ?.Run(inputValues: inputValues);
                        break;
                    case FileType.便签:
                        ((ILabelWindowService)ServiceManager.Services.GetService(typeof(ILabelWindowService))!)
                            .Show(searchViewItem.OnlyKey);
                        break;
                    case FileType.自定义:
                        searchViewItem.Action?.Invoke(searchViewItem,inputValues[0] as string);
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
                        //var tcs = new TaskCompletionSource<bool>();
                        var thread = new Thread(() =>
                        {
                            var remove = searchViewItem.ItemDisplayName.Remove(0, 1);
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
                            Shell32.ShellExecute(IntPtr.Zero, "open", searchViewItem.OnlyKey, "",
                                searchViewItem.OnlyKey.Remove(searchViewItem.OnlyKey.LastIndexOf('\\')),
                                ShowWindowCommand.SW_NORMAL);
                        else
                            Shell32.ShellExecute(IntPtr.Zero, "open", searchViewItem.OnlyKey,
                                searchViewItem.Arguments,
                                searchViewItem.OnlyKey.Remove(searchViewItem.OnlyKey.LastIndexOf('\\')),
                                ShowWindowCommand.SW_SHOWNORMAL);

                        break;
                    }
                    default:

                        Shell32.ShellExecute(IntPtr.Zero, "open", searchViewItem.OnlyKey, searchViewItem.Arguments,
                            searchViewItem.StartDirectory,
                            ShowWindowCommand.SW_NORMAL);
                        Log.Debug(
                            $"打开指定内容{searchViewItem.OnlyKey}_{searchViewItem.Arguments}_{searchViewItem.StartDirectory}");

                        break;
                }
                RecordOpenTime(searchViewItem);
                return;
            }
        }
    }

    public void OpenFile(string path)
    {
        Log.Debug("打开指定内容" + path);
        Shell32.ShellExecute(IntPtr.Zero, "open", path, "", "",
            ShowWindowCommand.SW_NORMAL);
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
            ServiceManager.Services.GetService<SearchWindowViewModel>()!._collection.TryRemove(item.OnlyKey, out _);
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
            Log.Debug($"打开指定内容文件夹{searchViewItem.OnlyKey}_{searchViewItem.StartDirectory}");
            Shell32.ShellExecute(IntPtr.Zero, "open", "explorer.exe", "/select," + searchViewItem.OnlyKey, searchViewItem.StartDirectory,
                ShowWindowCommand.SW_SHOW);
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
            Log.Debug("以管理员身份打开指定内容" + item?.OnlyKey);
            if (item is { FileType: FileType.UWP应用 })
                //explorer.exe shell:AppsFolder\Microsoft.WindowsMaps_8wekyb3d8bbwe!App
                Shell32.ShellExecute(IntPtr.Zero, "runas", "explorer.exe", $"shell:AppsFolder\\{item.OnlyKey}!App",
                    "", ShowWindowCommand.SW_NORMAL);
            else
                Shell32.ShellExecute(IntPtr.Zero, "runas", item.OnlyKey, "", "",
                    ShowWindowCommand.SW_NORMAL);

            RecordOpenTime(item);
        });
    }

    public void Star(SearchViewItem? item)
    {
        if (item is null) return;

        var collection = ServiceManager.Services.GetService<SearchWindowViewModel>()!._collection;
        Log.Information("添加/移除收藏" + item.OnlyKey);
        item.IsStared = !item.IsStared;
        if (ConfigManger.Config!.customCollections.Contains(item.OnlyKey))
            ConfigManger.Config.customCollections.Remove(item.OnlyKey);

        if (item.IsStared) //收藏操作
        {
            ServiceManager.Services.GetService<IAppToolService>()!.AppSolverA(collection, item.OnlyKey, true);
            ConfigManger.Config.customCollections.Insert(0, item.OnlyKey);
        }
        else
        {
            var keyValuePairs = collection.Where(e =>
                e.Value.OnlyKey.Equals(item.OnlyKey));
            foreach (var keyValuePair in keyValuePairs) collection.TryRemove(keyValuePair.Key, out _);
        }

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
            Log.Debug("打开指定内容在终端中" + item.OnlyKey);
            var startInfo = new ProcessStartInfo
            {
                FileName = @"C:\Windows\System32\cmd.exe"
            };
            if (!File.Exists(@"C:\Windows\System32\cmd.exe"))
            {
                Log.Debug("64");
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
        if (((SearchWindowViewModel)ServiceManager.Services!.GetService(typeof(SearchWindowViewModel))!)._collection
            .TryGetValue(onlyKey, out var item))
            OpenFile(item, inputValues);
    }
}