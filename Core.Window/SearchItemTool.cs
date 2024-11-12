using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.Messaging;
using Core.SDKs.CustomScenario;
using Core.SDKs.Services;
using Core.SDKs.Services.Config;
using Core.ViewModel;
using log4net;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using Vanara.PInvoke;

namespace Core.Window;

public class SearchItemTool : ISearchItemTool
{
    private static readonly ILog Log = LogManager.GetLogger(nameof(SearchItemTool));

    public void OpenFile(SearchViewItem? searchViewItem, params object[] inputValues)
    {
        if (searchViewItem is null)
        {
            return;
        }

        WeakReferenceMessenger.Default.Send("a", "SearchWindowClose");
        Log.Debug("打开指定内容" + searchViewItem.OnlyKey);
        switch (searchViewItem.OnlyKey)
        {
            case "ClipboardImageData":
            {
                try
                {
                    var bitmap = ((IClipboardService)ServiceManager.Services!.GetService(typeof(IClipboardService))!)
                        .GetImage();
                    var ts = DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, 0);
                    var timeStamp = Convert.ToInt64(ts.TotalMilliseconds);
                    var f = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\\Downloads\\Kitopia" +
                            timeStamp + ".png";
                    bitmap.Save(f);


                    Shell32.ShellExecute(IntPtr.Zero, "open", "explorer.exe", "/select," + f, "",
                        ShowWindowCommand.SW_NORMAL);
                }
                catch (Exception e)
                {
                    Log.Error("剪贴板图片保存失败");
                    ((IToastService)ServiceManager.Services.GetService(typeof(IToastService))!)
                        .Show("剪贴板", "剪贴板图片保存失败");
                    return;
                }

                return;
            }
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
                        searchViewItem.Action?.Invoke(searchViewItem);
                        break;
                    case FileType.命令:
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "cmd.exe",
                            Arguments = $"/c {searchViewItem.OnlyKey} & pause",
                            UseShellExecute = false,
                            CreateNoWindow = false,
                        });
                        break;
                    }
                    case FileType.数学运算:
                    {
                        //var tcs = new TaskCompletionSource<bool>();
                        var thread = new Thread(() =>
                        {
                            var remove = searchViewItem.ItemDisplayName.Remove(0, 1);
                            Clipboard.SetText(remove);
                            ServiceManager.Services.GetService<IToastService>()!.Show("Kitopia",$"计算结果{remove}已经复制到剪贴板");
                        });
                        thread.SetApartmentState(ApartmentState.STA);
                        thread.Start();
                        break;
                    }

                    case FileType.文件夹:
                    {
                        if (searchViewItem.Arguments == null)
                        {
                            Shell32.ShellExecute(IntPtr.Zero, "open", searchViewItem.OnlyKey, "",
                                searchViewItem.OnlyKey.Remove(searchViewItem.OnlyKey.LastIndexOf('\\')),
                                ShowWindowCommand.SW_NORMAL);
                        }
                        else
                        {
                            Shell32.ShellExecute(IntPtr.Zero, "open", searchViewItem.OnlyKey,
                                searchViewItem.Arguments,
                                searchViewItem.OnlyKey.Remove(searchViewItem.OnlyKey.LastIndexOf('\\')),
                                ShowWindowCommand.SW_SHOWNORMAL);
                        }

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

                switch (searchViewItem.FileType)
                {
                    case FileType.文件夹:
                    case FileType.应用程序:
                    case FileType.Word文档:
                    case FileType.PPT文档:
                    case FileType.Excel文档:
                    case FileType.PDF文档:
                    case FileType.图像:
                    case FileType.文件:
                    case FileType.自定义情景:
                    {
                        if (ConfigManger.Config.lastOpens.ContainsKey(searchViewItem.OnlyKey))
                        {
                            ConfigManger.Config.lastOpens[searchViewItem.OnlyKey]++;
                        }
                        else
                        {
                            ConfigManger.Config.lastOpens.Add(searchViewItem.OnlyKey, 1);
                        }

                        break;
                    }
                }

                ConfigManger.Save();
                return;
            }
        }
    }


    public void IgnoreItem(SearchViewItem? item)
    {
        Task.Run(() =>
        {
            ConfigManger.Config.ignoreItems.Add(item.OnlyKey);
            ConfigManger.Save();
            ServiceManager.Services.GetService<SearchWindowViewModel>()!._collection.TryRemove(item.OnlyKey, out _);
        });
    }

    public void OpenFolder(SearchViewItem? searchViewItem)
    {
        Task.Run(() =>
        {
            WeakReferenceMessenger.Default.Send("a", "SearchWindowClose");
            var item = (SearchViewItem)searchViewItem;
            Log.Debug($"打开指定内容文件夹{item.OnlyKey}_{item.StartDirectory}");
            Shell32.ShellExecute(IntPtr.Zero, "open", "explorer.exe", "/select," + item.OnlyKey, item.StartDirectory,
                ShowWindowCommand.SW_SHOW);

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
                        ConfigManger.Config.lastOpens[item.OnlyKey]++;
                    }
                    else
                    {
                        ConfigManger.Config.lastOpens.Add(item.OnlyKey, 1);
                    }

                    break;
                }
                    ;
            }

            //if (ConfigManger.config.lastOpens.Count > ConfigManger.config.maxHistory) ConfigManger.config.lastOpens.RemoveAt(ConfigManger.config.lastOpens.Count-1);

            ConfigManger.Save();
        });
    }

    public void RunAsAdmin(SearchViewItem? item)
    {
        Task.Run(() =>
        {
            WeakReferenceMessenger.Default.Send("a", "SearchWindowClose");
            Log.Debug("以管理员身份打开指定内容" + item.OnlyKey);
            if (item.FileType == FileType.UWP应用)
            {
                //explorer.exe shell:AppsFolder\Microsoft.WindowsMaps_8wekyb3d8bbwe!App

                Shell32.ShellExecute(IntPtr.Zero, "runas", "explorer.exe", $"shell:AppsFolder\\{item.OnlyKey}!App",
                    "", ShowWindowCommand.SW_NORMAL);
            }
            else
            {
                Shell32.ShellExecute(IntPtr.Zero, "runas", item.OnlyKey, "", "",
                    ShowWindowCommand.SW_NORMAL);
            }

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
                        ConfigManger.Config.lastOpens[item.OnlyKey]++;
                    }
                    else
                    {
                        ConfigManger.Config.lastOpens.Add(item.OnlyKey, 1);
                    }

                    break;
                }
                    ;
            }

            //if (ConfigManger.config.lastOpens.Count > ConfigManger.config.maxHistory) ConfigManger.config.lastOpens.RemoveAt(ConfigManger.config.lastOpens.Count-1);

            ConfigManger.Save();
        });
    }

    public void Star(SearchViewItem? item)
    {
        if (item is null)
        {
            return;
        }

        var collection = ServiceManager.Services.GetService<SearchWindowViewModel>()!._collection;
        Log.Debug("添加/移除收藏" + item.OnlyKey);
        item.IsStared = !item.IsStared;
        if (ConfigManger.Config!.customCollections.Contains(item.OnlyKey))
        {
            ConfigManger.Config.customCollections.Remove(item.OnlyKey);
        }

        if (item.IsStared) //收藏操作
        {
            ServiceManager.Services.GetService<IAppToolService>()!.AppSolverA(collection, item.OnlyKey, true);
            ConfigManger.Config.customCollections.Insert(0, item.OnlyKey);
        }
        else
        {
            var keyValuePairs = collection.Where(e =>
                e.Value.OnlyKey.Equals(item.OnlyKey));
            foreach (var keyValuePair in keyValuePairs)
            {
                collection.TryRemove(keyValuePair.Key, out _);
            }
        }

        ConfigManger.Save();
    }

    public void Pin(SearchViewItem? item)
    {
        if (ConfigManger.Config.alwayShows.Contains(item.OnlyKey))
        {
            ConfigManger.Config.alwayShows.Remove(item.OnlyKey);
        }

        if (item.IsPined) //收藏操作
        {
            ConfigManger.Config.alwayShows.Insert(0, item.OnlyKey);
        }

        ConfigManger.Save();
    }

    public void OpenFolderInTerminal(SearchViewItem? item)
    {
        Task.Run(() =>
        {
            WeakReferenceMessenger.Default.Send("a", "SearchWindowClose");
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

            if (item.FileType == FileType.文件夹)
            {
                startInfo.WorkingDirectory = item.OnlyKey;
            }

            if (item.FileType is FileType.文件 or FileType.Excel文档 or FileType.Word文档 or FileType.PDF文档 or FileType.PPT文档)
            {
                startInfo.WorkingDirectory = item.OnlyKey[..item.OnlyKey.LastIndexOf('\\')];
            }

            Process.Start(startInfo);

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
                        ConfigManger.Config.lastOpens[item.OnlyKey]++;
                    }
                    else
                    {
                        ConfigManger.Config.lastOpens.Add(item.OnlyKey, 1);
                    }

                    break;
                }
                    ;
            }

            //if (ConfigManger.config.lastOpens.Count > ConfigManger.config.maxHistory) ConfigManger.config.lastOpens.RemoveAt(ConfigManger.config.lastOpens.Count-1);

            ConfigManger.Save();
        });
    }

    public void OpenSearchItemByOnlyKey(string onlyKey, params object[] inputValues)
    {
        if (((SearchWindowViewModel)ServiceManager.Services!.GetService(typeof(SearchWindowViewModel))!)._collection
            .TryGetValue(onlyKey, out var item))
        {
            OpenFile(item, inputValues: inputValues);
        }
    }
}