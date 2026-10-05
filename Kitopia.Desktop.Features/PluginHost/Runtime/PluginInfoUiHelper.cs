using Kitopia.Feature.Localization;
using System.ComponentModel.DataAnnotations;
using System.Threading.RateLimiting;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using NuGet.Versioning;
using PluginCore;
using Polly;
using Polly.Retry;
using Serilog;

namespace Kitopia.Desktop.Features.Services.Plugin;

public class PluginsReloaded
{
}

public sealed record PluginBadgeItem(string Text, bool IsTag, string? TagName = null);

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
        WeakReferenceMessenger.Default.Register<PluginsReloaded>(this, static (recipient, _) =>
        {
            var item = (PluginInfoUiHelper)recipient;
            item.PluginLocalInfo = PluginManager.GetPluginLocalInfoByPlgStr(item.PluginBaseInfo.NameSign);
            if (item.PluginLocalInfo is { } local) item.PluginBaseInfo = local.PluginBaseInfo;
            item._canUpdate = null;
            item.OnPropertyChanged(nameof(PluginLocalInfo));
            item.OnPropertyChanged(nameof(Version));
            item.OnPropertyChanged(nameof(DescriptionShort));
            item.OnPropertyChanged(nameof(InLocal));
            item.OnPropertyChanged(nameof(CanUpdate));
            item.OnPropertyChanged(nameof(CanRemove));
            item.OnPropertyChanged(nameof(CanSwitch));
        });
        WeakReferenceMessenger.Default.Register<PluginDownloadProgress>(this, static (recipient, progress) =>
        {
            var item = (PluginInfoUiHelper)recipient;
            if (!string.Equals(item.PluginBaseInfo.NameSign, progress.PluginInfo.NameSign, StringComparison.OrdinalIgnoreCase)) return;
            if (item.PluginLocalInfo is null) item.PluginBaseInfo = progress.PluginInfo;
            item.OnPropertyChanged(nameof(Download));
            item.OnPropertyChanged(nameof(IsDownloading));
            item.OnPropertyChanged(nameof(Version));
            item.OnPropertyChanged(nameof(DescriptionShort));
            item.OnPropertyChanged(nameof(CanUpdate));
            item.OnPropertyChanged(nameof(CanRemove));
            item.OnPropertyChanged(nameof(CanSwitch));
        });
    }

    private bool _disposed;

    private CancellationTokenSource _cancellationTokenSource = new();
    private PluginBaseInfo _pluginBaseInfo;
    public PluginBaseInfo PluginBaseInfo
    {
        get => _pluginBaseInfo;
        set
        {
            // PluginBaseInfo equality compares only NameSign, not updated metadata.
            _pluginBaseInfo = value;
            OnPropertyChanged();
        }
    }

    public PluginDownloadProgress? Download => PluginManager.Downloads.GetValueOrDefault(PluginBaseInfo.NameSign);
    public bool IsDownloading => Download is not null;

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
        if (OnlinePluginInfo is not null || IsDownloading)
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

    public string? AuthorUserName => OnlinePluginInfo?.AuthorUserName;

    public IReadOnlyList<PluginTag> Tags
    {
        get
        {
            if (IsLocal && _onlinePluginInfo == null)
            {
                lock (_cancellationTokenSource)
                {
                    if (!_cancellationTokenSource.IsCancellationRequested)
                    {
                        ResiliencePipeline.ExecuteAsync(EnsureOnlineInfoAsync, _cancellationTokenSource.Token);
                    }
                }
            }
            return OnlinePluginInfo?.Tags ?? [];
        }
    }

    public bool HasTags => Tags.Count > 0;

    public IReadOnlyList<PluginBadgeItem> Badges
    {
        get
        {
            if (IsLocal && _onlinePluginInfo == null)
            {
                lock (_cancellationTokenSource)
                {
                    if (!_cancellationTokenSource.IsCancellationRequested)
                    {
                        ResiliencePipeline.ExecuteAsync(EnsureOnlineInfoAsync, _cancellationTokenSource.Token);
                    }
                }
            }

            var list = new List<PluginBadgeItem>();
            foreach (var platform in DisplayPlatforms)
            {
                list.Add(new PluginBadgeItem(platform, false));
            }
            foreach (var tag in Tags)
            {
                if (!string.IsNullOrWhiteSpace(tag.Name))
                {
                    list.Add(new PluginBadgeItem($"#{tag.Name}", true, tag.Name));
                }
            }
            return list;
        }
    }

    private async ValueTask EnsureOnlineInfoAsync(CancellationToken cts)
    {
        if (_onlinePluginInfo is null)
        {
            OnlinePluginInfo = await PluginNetworkService.GetOnlinePluginInfo(PluginBaseInfo.NameSign, cts);
        }
    }

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

    public string DownloadCountText => Lang.Format("lang.kitopia.messages.value_downloads", DownloadCounts);

    public double AverageRating => OnlinePluginInfo?.AverageRating ?? 0;
    public int RatingCount => OnlinePluginInfo?.RatingCount ?? 0;
    public bool HasRatings => RatingCount > 0;
    public string RatingScoreText => HasRatings ? AverageRating.ToString("F1") : string.Empty;
    public string RatingDisplayText => HasRatings ? Lang.Format("lang.kitopia.messages.value_value_ratings", AverageRating, RatingCount) : "暂无评分";
    public string RatingStar => HasRatings ? "★" : "☆";

    public bool InLocal => PluginManager.GetPluginLocalInfoByPlgStr(PluginBaseInfo.NameSign) is not null;
    public bool IsHostBundled => PluginReleaseRules.IsHostBundled(PluginBaseInfo.NameSign);
    public bool CanRemove => !IsHostBundled && InLocal && !IsDownloading;
    public bool CanSwitch => !IsHostBundled && InLocal && !IsDownloading;
    public PluginLocalInfo? PluginLocalInfo { get; set; }

    private OnlinePluginInfo? _onlinePluginInfo;
    public OnlinePluginInfo? OnlinePluginInfo
    {
        get => _onlinePluginInfo;
        set
        {
            if (SetProperty(ref _onlinePluginInfo, value))
            {
                OnPropertyChanged(nameof(Tags));
                OnPropertyChanged(nameof(HasTags));
                OnPropertyChanged(nameof(DisplayPlatforms));
                OnPropertyChanged(nameof(Badges));
                OnPropertyChanged(nameof(PublicationStatusText));
                OnPropertyChanged(nameof(DownloadCounts));
                OnPropertyChanged(nameof(DownloadCountText));
                OnPropertyChanged(nameof(VersionAndDateText));
                OnPropertyChanged(nameof(AverageRating));
                OnPropertyChanged(nameof(RatingCount));
                OnPropertyChanged(nameof(HasRatings));
                OnPropertyChanged(nameof(RatingScoreText));
                OnPropertyChanged(nameof(RatingDisplayText));
                OnPropertyChanged(nameof(RatingStar));
                OnPropertyChanged(nameof(TimelineVersionDetails));
            }
        }
    }
    [Required] public bool IsLocal { get; init; }

    private bool? _canUpdate;

    public bool? CanUpdate
    {
        get
        {
            if (IsHostBundled || IsDownloading) return false;
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
        if (_disposed) return;
        _disposed = true;
        WeakReferenceMessenger.Default.UnregisterAll(this);
        _cancellationTokenSource.Cancel();
        _icon?.Dispose();
        _authorAvatar?.Dispose();
    }

    public string DescriptionShort
    {
        get
        {
            var desc = IsLocal
                ? (PluginLocalInfo != null ? PluginLocalInfo.PluginBaseInfo.Description : PluginBaseInfo.Description)
                : (OnlinePluginInfo?.DescriptionShort ?? OnlinePluginInfo?.Description ?? string.Empty);
            return string.IsNullOrWhiteSpace(desc) ? "暂无简介" : desc.Trim();
        }
    }

    public string Version =>
        IsLocal ? (PluginLocalInfo?.PluginBaseInfo.Version ?? Download?.Version ?? PluginBaseInfo.Version) : (OnlinePluginInfo?.LastVersion ?? string.Empty);

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
            Description = Lang.Get("lang.kitopia.plugin_not_found_on_the_server");
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
        set
        {
            if (SetProperty(ref _versionDetails, value))
            {
                OnPropertyChanged(nameof(TimelineVersionDetails));
            }
        }
    }

    public IReadOnlyList<VersionDetail>? TimelineVersionDetails
    {
        get
        {
            if (VersionDetails is not { } details) return null;

            return details
                .GroupBy(detail => detail.Id)
                .Select(group =>
                {
                    var events = group
                        .OrderByDescending(detail => detail.Updatetime)
                        .ThenByDescending(detail => detail.AuditEntryId ?? 0)
                        .ToList();
                    var current = events.FirstOrDefault(detail => detail.IsCurrent) ?? events[0];
                    current.CreateTime = events.Min(detail => detail.CreateTime);
                    current.Events = events.Where(detail => detail.AuditEntryId.HasValue).ToList();
                    if (!string.IsNullOrWhiteSpace(OnlinePluginInfo?.LastVersion))
                    {
                        current.IsCurrent = string.Equals(
                            current.Version,
                            OnlinePluginInfo.LastVersion,
                            StringComparison.OrdinalIgnoreCase);
                    }
                    return current;
                })
                .OrderByDescending(detail => detail, VersionDetailComparer.Instance)
                .ToList();
        }
    }

    private sealed class VersionDetailComparer : IComparer<VersionDetail>
    {
        public static VersionDetailComparer Instance { get; } = new();

        public int Compare(VersionDetail? left, VersionDetail? right)
        {
            if (ReferenceEquals(left, right)) return 0;
            if (left is null) return -1;
            if (right is null) return 1;
            if (NuGetVersion.TryParse(left.Version, out var leftVersion) &&
                NuGetVersion.TryParse(right.Version, out var rightVersion))
            {
                return VersionComparer.VersionRelease.Compare(leftVersion, rightVersion);
            }

            return StringComparer.OrdinalIgnoreCase.Compare(left.Version, right.Version);
        }
    }

    private async ValueTask GetVersionDetails(CancellationToken cts)
    {
        VersionDetails = await PluginNetworkService.GetVersionDetailsAsync(PluginBaseInfo.NameSign, null, cts);
    }
}
