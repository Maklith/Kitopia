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
        OnlinePluginInfo ??= await PluginNetworkService.GetOnlinePluginInfo(PluginBaseInfo.NameSign, cts);
        if (OnlinePluginInfo is not null)
        {
            AuthorName = await PluginNetworkService.GetAuthorNameAsync(OnlinePluginInfo.AuthorId, cts);
        }
    }

    private string? _authorName;

    public string? AuthorName
    {
        set => SetProperty(ref _authorName, value);
        get
        {
            if (_authorName is null)
                lock (_cancellationTokenSource)
                {
                    if (_cancellationTokenSource.IsCancellationRequested) return null;
                    ResiliencePipeline.ExecuteAsync(GetAuthorName, _cancellationTokenSource.Token);
                }

            return _authorName;
        }
    }

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
    }

    public string DescriptionShort =>
        IsLocal ? PluginLocalInfo.PluginBaseInfo.Description : OnlinePluginInfo.DescriptionShort;

    public string Version =>
        IsLocal ? PluginLocalInfo.PluginBaseInfo.Version : OnlinePluginInfo.LastVersion;

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
