using System.ComponentModel.DataAnnotations;
using System.Threading.RateLimiting;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using PluginCore;
using Polly;
using Polly.Retry;
using Serilog;

namespace Kitopia.Desktop.Features.Services.Plugin;

public class PluginStateChanged
{
    public string PluginSignName { get; set; }

    public PluginStateChanged(string pluginSignName)
    {
        PluginSignName = pluginSignName;
    }
}

public class PluginsReloaded
{
}

public partial class PluginInfoUiHelper : ObservableObject, IDisposable
{
    private static ILogger Logger = LogManager.Logger.ForContext<PluginInfoUiHelper>();

    private static readonly ResiliencePipeline ResiliencePipeline = new ResiliencePipelineBuilder()
        .AddConcurrencyLimiter(new ConcurrencyLimiterOptions
        {
            PermitLimit = 5,
            QueueLimit = int.MaxValue
        })
        .AddRetry(
            new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder().Handle<Exception>(exception =>
                {
                    Logger.Error(exception, "错误");
                    return false;
                }),
                Delay = TimeSpan.FromSeconds(1),
                MaxRetryAttempts = 5,
                BackoffType = DelayBackoffType.Linear,
                UseJitter = true
            }).Build();

    public PluginInfoUiHelper()
    {
        WeakReferenceMessenger.Default.Register<PluginStateChanged>(this, (e, a) =>
        {
            if (a.PluginSignName == PluginBaseInfo.NameSign) PluginLocalInfo?.NotifyStatusChanged();
        });
    }

    ~PluginInfoUiHelper()
    {
        Dispose();
    }

    private CancellationTokenSource _cancellationTokenSource = new();
    public PluginBaseInfo PluginBaseInfo { get; init; }

    private Bitmap? _icon;

    public Bitmap? Icon
    {
        get
        {
            if (_icon is null)
                lock (_cancellationTokenSource)
                {
                    if (_cancellationTokenSource.IsCancellationRequested) return null;
                    ResiliencePipeline.ExecuteAsync(GetIcon, _cancellationTokenSource.Token);
                }

            return _icon;
        }
        set => SetProperty(ref _icon, value);
    }

    private async ValueTask GetIcon(CancellationToken cts)
    {
        if (OnlinePluginInfo is not null)
        {
            var bytes = await PluginNetworkService.GetAvatarBytesAsync(PluginBaseInfo.NameSign, cts);
            if (bytes != null)
                Icon = new Bitmap(new MemoryStream(bytes));
        }

        if (PluginLocalInfo is not null)
        {
            if (!File.Exists($"{PluginLocalInfo.Path}avatar.png"))
            {
                var bytes = await PluginNetworkService.GetAvatarBytesAsync(PluginBaseInfo.NameSign, cts);
                if (bytes != null)
                {
                    // Assuming we still want to save it locally if fetched
                    try 
                    {
                        await File.WriteAllBytesAsync($"{PluginLocalInfo.Path}avatar.png", bytes, cts);
                        Icon = new Bitmap(new MemoryStream(bytes));
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, "保存插件图标失败");
                        // Still show it even if save failed
                        Icon = new Bitmap(new MemoryStream(bytes));
                    }
                }
            }
            else
            {
                Icon = new Bitmap($"{PluginLocalInfo.Path}avatar.png");
            }
        }
    }

    private async ValueTask GetAuthorName(CancellationToken cts)
    {
        if (OnlinePluginInfo is not null)
        {
            if (!string.IsNullOrWhiteSpace(OnlinePluginInfo.AuthorNickname))
            {
                AuthorName = OnlinePluginInfo.AuthorNickname;
                return;
            }
            if (!string.IsNullOrWhiteSpace(OnlinePluginInfo.AuthorUserName))
            {
                AuthorName = OnlinePluginInfo.AuthorUserName;
                return;
            }
        }

        OnlinePluginInfo ??= await PluginNetworkService.GetOnlinePluginInfo(PluginBaseInfo.NameSign, cts);
        if (OnlinePluginInfo is not null)
        {
            if (!string.IsNullOrWhiteSpace(OnlinePluginInfo.AuthorNickname))
            {
                AuthorName = OnlinePluginInfo.AuthorNickname;
            }
            else if (!string.IsNullOrWhiteSpace(OnlinePluginInfo.AuthorUserName))
            {
                AuthorName = OnlinePluginInfo.AuthorUserName;
            }
            else
            {
                AuthorName = await PluginNetworkService.GetAuthorNameAsync(OnlinePluginInfo.AuthorId, cts);
            }
        }
    }

    private string? _authorName;

    public string? AuthorName
    {
        set
        {
            if (SetProperty(ref _authorName, value))
            {
                OnPropertyChanged(nameof(AuthorInitial));
            }
        }
        get
        {
            if (_authorName is null)
            {
                if (OnlinePluginInfo != null)
                {
                    if (!string.IsNullOrWhiteSpace(OnlinePluginInfo.AuthorNickname))
                    {
                        _authorName = OnlinePluginInfo.AuthorNickname;
                        return _authorName;
                    }
                    if (!string.IsNullOrWhiteSpace(OnlinePluginInfo.AuthorUserName))
                    {
                        _authorName = OnlinePluginInfo.AuthorUserName;
                        return _authorName;
                    }
                }

                lock (_cancellationTokenSource)
                {
                    if (_cancellationTokenSource.IsCancellationRequested) return null;
                    ResiliencePipeline.ExecuteAsync(GetAuthorName, _cancellationTokenSource.Token);
                }
            }

            return _authorName;
        }
    }

    public string AuthorInitial =>
        string.IsNullOrWhiteSpace(AuthorName)
            ? "作"
            : AuthorName[..1].ToUpperInvariant();

    public string PluginInitial =>
        string.IsNullOrWhiteSpace(PluginBaseInfo.Name)
            ? "?"
            : PluginBaseInfo.Name[..1].ToUpperInvariant();

    private Bitmap? _authorAvatar;

    public Bitmap? AuthorAvatar
    {
        get
        {
            if (_authorAvatar is null)
            {
                lock (_cancellationTokenSource)
                {
                    if (_cancellationTokenSource.IsCancellationRequested) return null;
                    ResiliencePipeline.ExecuteAsync(GetAuthorAvatar, _cancellationTokenSource.Token);
                }
            }

            return _authorAvatar;
        }
        set => SetProperty(ref _authorAvatar, value);
    }

    private async ValueTask GetAuthorAvatar(CancellationToken cts)
    {
        var userName = OnlinePluginInfo?.AuthorUserName;
        if (string.IsNullOrWhiteSpace(userName))
        {
            OnlinePluginInfo ??= await PluginNetworkService.GetOnlinePluginInfo(PluginBaseInfo.NameSign, cts);
            userName = OnlinePluginInfo?.AuthorUserName;
        }

        if (!string.IsNullOrWhiteSpace(userName))
        {
            var bytes = await PluginNetworkService.GetAuthorAvatarBytesAsync(userName, cts);
            if (bytes != null)
            {
                AuthorAvatar = new Bitmap(new MemoryStream(bytes));
            }
        }
    }

    public string PublicationStatusText =>
        OnlinePluginInfo?.PublicationStatus switch
        {
            1 => "待公开",
            0 => "私有",
            _ => "公开"
        };

    public IReadOnlyList<string> DisplayPlatforms
    {
        get
        {
            var list = OnlinePluginInfo?.AvailablePlatforms is { Count: > 0 } p
                ? p
                : OnlinePluginInfo?.SupportSystems;

            if (list is { Count: > 0 })
            {
                return list.Select(FormatPlatformName).Distinct().ToList();
            }

            return ["Windows"];
        }
    }

    public static string FormatPlatformName(string platform) =>
        platform.ToLowerInvariant() switch
        {
            "windows" => "Windows",
            "macos" => "macOS",
            "linux" => "Linux",
            _ => platform
        };

    public string AuthorHandle =>
        !string.IsNullOrWhiteSpace(OnlinePluginInfo?.AuthorUserName)
            ? $"@{OnlinePluginInfo.AuthorUserName}"
            : string.Empty;

    public IReadOnlyList<PluginTag> Tags => OnlinePluginInfo?.Tags ?? [];

    public bool HasTags => Tags.Count > 0;

    public long DownloadCounts => OnlinePluginInfo?.DownloadCounts ?? 0;

    public string VersionAndDateText
    {
        get
        {
            var version = !string.IsNullOrWhiteSpace(Version) ? $"v{Version}" : "—";
            if (OnlinePluginInfo is { Updatetime: var time } && time != default)
            {
                return $"{version} · {time:M月d日}";
            }
            return version;
        }
    }

    public string DownloadCountText => $"{DownloadCounts} 下载";

    public bool InLocal => PluginManager.GetPluginLocalInfoByPlgStr(PluginBaseInfo.NameSign) is not null;
    public PluginLocalInfo? PluginLocalInfo { get; set; }
    public OnlinePluginInfo? OnlinePluginInfo { get; set; }
    [Required] public bool IsLocal { get; init; }

    private bool? _canUpdate;

    public bool? CanUpdate
    {
        get
        {
            if (_canUpdate is null)

                lock (_cancellationTokenSource)
                {
                    if (_cancellationTokenSource.IsCancellationRequested) return false;
                    ResiliencePipeline.ExecuteAsync(CheckCanUpdate, _cancellationTokenSource.Token);
                }

            return _canUpdate;
        }
        set => SetProperty(ref _canUpdate, value);
    }

    public async ValueTask CheckCanUpdate(CancellationToken cts)
    {
        if (PluginLocalInfo is null)
        {
            PluginLocalInfo = PluginManager.GetPluginLocalInfoByPlgStr(PluginBaseInfo.NameSign);
            if (PluginLocalInfo is null)
            {
                CanUpdate = false;
                return;
            }
        }

        var latestVersion = await PluginNetworkService.GetLatestVersionAsync(PluginBaseInfo.NameSign, cts);
        if (!string.IsNullOrWhiteSpace(latestVersion))
        {
            CanUpdate = PluginDependencyService.IsVersionNewer(latestVersion, PluginLocalInfo.PluginBaseInfo.Version);
            CanUpdateVersion = latestVersion;
        }
        else
        {
            CanUpdate = false;
        }
    }

    [ObservableProperty] private string? _canUpdateVersion;

    public void Dispose()
    {
        _cancellationTokenSource.Cancel();
        _cancellationTokenSource.Dispose();
        Icon?.Dispose();
        AuthorAvatar?.Dispose();
    }

    public string DescriptionShort =>
        IsLocal ? (PluginLocalInfo != null ? PluginLocalInfo.PluginBaseInfo.Description : string.Empty) : (OnlinePluginInfo?.DescriptionShort ?? OnlinePluginInfo?.Description ?? string.Empty);

    public string Version =>
        IsLocal ? (PluginLocalInfo != null ? PluginLocalInfo.PluginBaseInfo.Version : string.Empty) : (OnlinePluginInfo?.LastVersion ?? string.Empty);

    private string? _description;

    public string? Description
    {
        get
        {
            if (_description is null)
                lock (_cancellationTokenSource)
                {
                    if (_cancellationTokenSource.IsCancellationRequested) return null;
                    ResiliencePipeline.ExecuteAsync(GetDescription, _cancellationTokenSource.Token);
                }

            return _description;
        }
        set => SetProperty(ref _description, value);
    }

    private async ValueTask GetDescription(CancellationToken cts)
    {
        if (IsLocal && OnlinePluginInfo is null)
            OnlinePluginInfo = await PluginNetworkService.GetOnlinePluginInfo(PluginBaseInfo.NameSign, cts);

        if (OnlinePluginInfo is null)
        {
            Description = "该插件远端未找到";
            return;
        }

        Description = OnlinePluginInfo.Description;
    }

    private List<VersionDetail>? _versionDetails;

    public List<VersionDetail>? VersionDetails
    {
        get
        {
            if (_versionDetails is null)
                lock (_cancellationTokenSource)
                {
                    if (_cancellationTokenSource.IsCancellationRequested) return null;
                    ResiliencePipeline.ExecuteAsync(GetVersionDetails, _cancellationTokenSource.Token);
                }

            return _versionDetails;
        }
        set => SetProperty(ref _versionDetails, value);
    }

    private async ValueTask GetVersionDetails(CancellationToken cts)
    {
        VersionDetails = await PluginNetworkService.GetVersionDetailsAsync(PluginBaseInfo.NameSign, null, cts);
    }
}
