#region

using System.Drawing;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.RateLimiting;
using System.Xml;
using Core.Services;
using Core.Utils;
using PeNet;
using PluginCore;
using Polly;
using Polly.Retry;
using Serilog;
using Vanara.PInvoke;
using Bitmap = Avalonia.Media.Imaging.Bitmap;
using Size = System.Drawing.Size;

#endregion

namespace Core.Window;

internal partial class IconTools
{
    // ReSharper disable once InconsistentNaming
    private const uint SHGFI_ICON = 0x100;
    // ReSharper disable once InconsistentNaming
    private const uint SHGFI_LARGEICON = 0x0;
    private static readonly ILogger Logger = LogManager.Logger.ForContext<IconTools>();

    private static readonly ResiliencePipeline ResiliencePipeline = new ResiliencePipelineBuilder()
        .AddConcurrencyLimiter(new ConcurrencyLimiterOptions()
        {
            PermitLimit = 1,
            QueueLimit = Int32.MaxValue
        })
        .AddRetry(
            new RetryStrategyOptions()
            {
                ShouldHandle = new PredicateBuilder().Handle<Exception>(exception =>
                {
                    Logger.Error(exception, "错误");
                    return true;
                }),
                Delay = TimeSpan.FromSeconds(1),
                MaxRetryAttempts = 5,
                BackoffType = DelayBackoffType.Linear,
                UseJitter = true
            }).Build();

    private static readonly Dictionary<string, Bitmap> Icons = new(250);


    [DllImport("User32.dll")]
    internal static extern int PrivateExtractIcons(
        string lpszFile, //file name
        int nIconIndex, //The zero-based index of the first icon to extract.
        int cxIcon, //The horizontal icon size wanted.
        int cyIcon, //The vertical icon size wanted.
        IntPtr[] phicon, //(out) A pointer to the returned array of icon handles.
        int[] piconid, //(out) A pointer to a returned resource identifier.
        int nIcons, //The number of icons to extract from the file. Only valid when *.exe and *.dll
        int flags //Specifies flags that control this function.
    );
    
    public static Icon? GetIconFromImageList(string path, Shell32.SHIL size = Shell32.SHIL.SHIL_EXTRALARGE)
    {
        var shfi = new Shell32.SHFILEINFO();
        var result = Shell32.SHGetFileInfo(
            path, 
            0, 
            ref shfi, 
            Shell32.SHFILEINFO.Size, 
            Shell32.SHGFI.SHGFI_SYSICONINDEX); // 注意：移除了 SHGFI_ICON

        if (result == IntPtr.Zero) return null;
        var hres = Shell32.SHGetImageList(size, typeof(ComCtl32.IImageList).GUID, out var listObj);
    
        if (hres.Failed || listObj is not ComCtl32.IImageList imgList) return null;
        try 
        {
            var hIcon = imgList.GetIcon(shfi.iIcon, ComCtl32.IMAGELISTDRAWFLAGS.ILD_TRANSPARENT);
            if (hIcon == IntPtr.Zero) return null;
            var icon = (Icon)Icon.FromHandle(hIcon.DangerousGetHandle()).Clone();
            User32.DestroyIcon(hIcon);
        
            return icon;
        }
        catch
        {
            return null;
        }
    }
    private static Icon? GetIconBase(string path, string cacheKey)
    {
        try
        {
            switch (Path.GetExtension(path))
            {
                case ".png":
                case ".bmp":
                case ".ico":
                case ".jpg":
                {
                    if (!File.Exists(path)) return null;

                    using var bm = new System.Drawing.Bitmap(path);
                    using var iconBm = new System.Drawing.Bitmap(bm, new Size(64, 64));

                    retry:
                    try
                    {
                        var icon = Icon.FromHandle(iconBm.GetHicon());
                        return icon;
                    }
                    catch (Exception)
                    {
                        goto retry;
                    }
                }
                case ".msc":
                {
                    int index;
                    string dllPath;
                    var xd = new XmlDocument();
                    xd.Load(path); //加载xml文档
                    var rootNode = xd.SelectSingleNode("MMC_ConsoleFile"); //得到xml文档的根节点
                    var binaryStorage = rootNode?.SelectSingleNode("VisualAttributes")?.SelectSingleNode("Icon");
                    if (binaryStorage is null)
                    {
                        return  null;
                    }
                    index = int.Parse(((XmlElement)binaryStorage).GetAttribute("Index"));
                    dllPath = ((XmlElement)binaryStorage).GetAttribute("File");

                    dllPath = Environment.SystemDirectory + "\\" + dllPath.Split("\\").Last();
                    path = dllPath;
                    if (cacheKey.Contains("taskschd.msc"))
                    {
                        index += 1;
                    }

                    var iconTotalCount = PrivateExtractIcons(dllPath, index, 0, 0, null!, null!, 0, 0);

                    //用于接收获取到的图标指针
                    var hIcons = new IntPtr[iconTotalCount];
                    //对应的图标id
                    var ids = new int[iconTotalCount];
                    //成功获取到的图标个数
                    var successCount = PrivateExtractIcons(dllPath, index, 48, 48, hIcons, ids, iconTotalCount, 0);
                    for (var i = 0; i < successCount; i++)
                    {
                        //指针为空，跳过
                        if (hIcons[i] == IntPtr.Zero)
                        {
                            continue;
                        }

                        var icon = Icon.FromHandle(hIcons[i]);

                        return icon;
                    }

                    break;
                }
            }
            var match = MyRegex().Match(path);
            if (match.Success)
            {
                // 获取匹配到的部分
                string dllPath2 = match.Groups[1].Value;
                int iconIndex = int.Parse(match.Groups[2].Value);

                try
                {
                    var safeHicon = Shell32.ExtractIconEx(dllPath2, iconIndex, 1,
                        out User32.SafeHICON[]? large, out var small);
                    if (safeHicon != 0 && large != null && large.Length != 0)
                    {
                        if (!large[0].IsNull)
                        {
                            var icon1 = Icon.FromHandle(large[0].DangerousGetHandle());
                            return icon1;
                        }

                        for (var i = 1; i < large.Length; i++)
                        {
                            User32.DestroyIcon(large[i].DangerousGetHandle());
                        }

                        if (small != null)
                            foreach (var hicon in small)
                            {
                                User32.DestroyIcon(hicon.DangerousGetHandle());
                            }
                    }

                    var extractIcon = Icon.ExtractIcon(dllPath2, iconIndex);
                    if (extractIcon is not null) return extractIcon;
                }
                catch (Exception e)
                {
                    Logger.Error(e, "ExtractIcon获取图标失败");
                }

                var iconByPe = GetIconByPe(dllPath2);
                if (iconByPe != null) return iconByPe;
                path = dllPath2;
            }

            var jumboIcon = GetIconFromImageList(path); // 48x48 或更大
            if (jumboIcon != null) return jumboIcon;

            return null;
        }
        catch (Exception e)
        {
            Logger.Error(e, $"获取图标失败，路径：{path}，缓存键：{cacheKey}");
            return null;
        }
    }

    private static Icon? GetIconByPe(string dllPath2)
    {
        var peHeader1 = new PeFile(dllPath2);
        var enumerable = peHeader1.Icons();
        var first = enumerable.FirstOrDefault();
        if (first == null)
        {
            return null;
        }

        using var ms = new MemoryStream(first);
        using var bitmap = new System.Drawing.Bitmap(ms);
        var icon2 = Icon.FromHandle(bitmap.GetHicon());
        return icon2;
    }

    internal static void GetIconByItem(SearchViewItem t)
    {
        //Log.Debug($"为{t.OnlyKey}生成Icon");
        {
            switch (t.FileType)
            {
                case FileType.自定义情景:
                {
                    var path =
                        $"{AppDomain.CurrentDomain.BaseDirectory}customScenarios{Path.DirectorySeparatorChar}{t.OnlyKey.Split(":")[1]}.png";
                    if (File.Exists(path))
                    {
                        if (Icons.TryGetValue(path, out var icon2)) t.Icon = icon2;
                        ResiliencePipeline.ExecuteAsync(async e =>
                        {
                            await Task.Run(() =>
                            {
                                var iconBase = GetIconBase(path, path);
                                if (iconBase == null) return;

                                var clone = iconBase.ToBitmap().ToAvaloniaBitmap();
                                Icons.TryAdd(path, clone);
                                iconBase.Dispose();
                                t.Icon = clone;
                            }, e);
                        });
                    }

                    break;
                }
                case FileType.命令:
                case FileType.便签:
                case FileType.数学运算:
                case FileType.剪贴板图像:
                case FileType.None:
                    break;
                case FileType.应用程序:
                case FileType.Word文档:
                case FileType.PPT文档:
                case FileType.Excel文档:
                case FileType.PDF文档:
                case FileType.图像:
                case FileType.文件:
                    GetIcon(t.OnlyKey, t);
                    break;
                case FileType.文件夹:
                    GetIconByPath(t.OnlyKey, t);
                    break;
                case FileType.URL:
                    if (t.IconPath is not null)
                    {
                        GetIcon(t.IconPath, t);
                    }

                    break;
                case FileType.自定义:
                    if (t.GetIconAction != null)
                    {
                        ResiliencePipeline.ExecuteAsync(async e =>
                        {
                            await Task.Run(() =>
                            {
                                try
                                {
                                    var icon = t.GetIconAction(t);
                                    t.Icon = icon;
                                }
                                catch (Exception exception)
                                {
                                    Logger.Error(exception, "GetIconAction执行失败");
                                }
                            }, e);
                        });
                    }

                    break;
                case FileType.UWP应用:
                    GetIcon(t.IconPath!, t);
                    break;

                default:
                    if (t.GetIconAction != null)
                    {
                        ResiliencePipeline.ExecuteAsync(async e =>
                        {
                            await Task.Run(() =>
                            {
                                try
                                {
                                    var icon = t.GetIconAction(t);
                                    t.Icon = icon;
                                }
                                catch (Exception exception)
                                {
                                    Logger.Error(exception, "GetIconAction执行失败");
                                }
                            }, e);
                        });
                        break;
                    }

                    if (t.IconPath is not null)
                    {
                        GetIcon(t.IconPath, t);
                        break;
                    }
                    else
                    {
                        GetIcon(t.OnlyKey, t);
                        break;
                    }
            }
        }

        //Log.Debug(t.OnlyKey);

        //
    }

    internal static void GetIconByItem(CustomScenario.CustomScenario t)
    {
        var path = $"{AppDomain.CurrentDomain.BaseDirectory}customScenarios{Path.DirectorySeparatorChar}{t.UUID}.png";
        if (File.Exists(path))
        {
            if (Icons.TryGetValue(path, out var icon2)) t.Icon = icon2;
            ResiliencePipeline.ExecuteAsync(async e =>
            {
                await Task.Run(() =>
                {
                    var iconBase = GetIconBase(path, path);
                    if (iconBase == null) return;

                    var clone = iconBase.ToBitmap().ToAvaloniaBitmap();
                    Icons.TryAdd(path, clone);
                    iconBase.Dispose();
                    t.Icon = clone;
                }, e);
            });
        }
    }

    private static void GetIcon(string path, SearchViewItem item)
    {
        //log.Debug(1);

        string cacheKey;
        switch (path.ToLower().Split(".").Last())
        {
            case "docx":
            case "doc":
            case "xls":
            case "xlsx":
            case "pdf":
            case "ppt":
            case "pptx":
            {
                cacheKey = path.Split(".").Last();
                break;
            }
            case "msc":
            {
                cacheKey = path.Split(Path.DirectorySeparatorChar).Last();
                break;
            }
            default:
            {
                cacheKey = path;
                break;
            }
        }

        if (cacheKey.EndsWith("mmc.exe"))
        {
            cacheKey = item.Arguments?.Replace("\"", null) ?? cacheKey;
            path = cacheKey;
        }

        //缓存
        if (Icons.TryGetValue(cacheKey, out var icon2)) item.Icon = icon2;

        ResiliencePipeline.ExecuteAsync(async e =>
        {
            await Task.Run(() =>
            {
                var iconBase = GetIconBase(path, cacheKey);
                if (iconBase == null) return;

                var clone = iconBase.ToBitmap().ToAvaloniaBitmap();
                Icons.TryAdd(cacheKey, clone);
                iconBase.Dispose();
                item.Icon = clone;
            }, e);
        });
    }


    private static void GetIconByPath(string path, SearchViewItem item)
    {
        if (Icons.TryGetValue(path, out var fromPath)) item.Icon = fromPath;

        ResiliencePipeline.ExecuteAsync(async e =>
        {
            await Task.Run(() =>
            {
                var shinfo = new Shfileinfo();
                SHGetFileInfo(
                    path,
                    0, ref shinfo, (uint)Marshal.SizeOf(shinfo),
                    SHGFI_ICON | SHGFI_LARGEICON);
                var independenceIcon12 = Icon.FromHandle(shinfo.hIcon).ToBitmap().ToAvaloniaBitmap();
                User32.DestroyIcon(shinfo.hIcon);
                Icons.TryAdd(path, independenceIcon12);
                item.Icon = independenceIcon12;
            }, e);
        });
    }

    [DllImport("shell32.dll")]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref Shfileinfo psfi,
        uint cbSizeFileInfo, uint uFlags);

//Struct used by SHGetFileInfo function
    [StructLayout(LayoutKind.Sequential)]
    private struct Shfileinfo
    {
        internal IntPtr hIcon;
        internal int iIcon;
        internal uint dwAttributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        internal string szDisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        internal string szTypeName;
    };

    [GeneratedRegex(@"(.+),(-?\d+)(?:#.*)?")]
    private static partial Regex MyRegex();
}