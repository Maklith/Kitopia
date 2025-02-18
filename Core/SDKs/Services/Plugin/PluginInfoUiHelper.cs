
using System.ComponentModel.DataAnnotations;
using System.Threading.RateLimiting;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Core.SDKs.Services.Config;
using Core.ViewModel.Pages;
using log4net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PluginCore;
using Polly;
using Polly.Retry;

namespace Core.SDKs.Services.Plugin;

public class PluginStateChanged
{
    public string PluginSignName { get; set; }
    public PluginStateChanged(string pluginSignName)
    {
        PluginSignName = pluginSignName;
    }
}

public struct VersionDetail
{
    /*
     "id": 1,
			"pluginId": 7,
			"versionInt": 1,
			"version": "1.0.0",
			"detail": "第一个版本",
			"isAvailable": true
     */
    public int Id { get; set; }
    public int PluginId { get; set; }
    public int VersionInt { get; set; }
    public string Version { get; set; }
    public string Detail { get; set; }
    public bool IsAvailable { get; set; }
}
public partial class PluginInfoUiHelper : ObservableObject,IDisposable
{
    private static readonly ILog log = LogManager.GetLogger(nameof(PluginInfoUiHelper));
    private static readonly ResiliencePipeline ResiliencePipeline = new ResiliencePipelineBuilder()
        .AddConcurrencyLimiter(new ConcurrencyLimiterOptions()
        {
            PermitLimit = 5,
            QueueLimit = Int32.MaxValue
        })
        .AddRetry(
            new RetryStrategyOptions()
            {
                ShouldHandle = new PredicateBuilder().Handle<Exception>(exception =>
                {
                    log.Error("错误", exception);
                    return true;
                }),
                Delay = TimeSpan.FromSeconds(1),
                MaxRetryAttempts = 5,
                BackoffType = DelayBackoffType.Linear,
                UseJitter = true
            }).Build();
    public PluginInfoUiHelper()
    {
        WeakReferenceMessenger.Default.Register<PluginStateChanged>(this,(e, a) =>
        {
            if (a.PluginSignName == PluginBaseInfo.NameSign)
            {
                PluginLocalInfo?.NotifyStatusChanged();
            }
        });
    }

    ~PluginInfoUiHelper()
    {
        Dispose();
    }

    private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
    public PluginBaseInfo PluginBaseInfo { get; init; }
    
    private Bitmap? _icon;
    public Bitmap? Icon
    {
        get
        {
            if (_icon is null)
            {
                lock (_cancellationTokenSource)
                {
                    if (_cancellationTokenSource.IsCancellationRequested)
                    {
                        return null;
                    }
                    ResiliencePipeline.ExecuteAsync(GetIcon,_cancellationTokenSource.Token);
                }
               
            }
            return _icon;
        }
        set => SetProperty(ref _icon, value);
    }

    private async ValueTask GetIcon(CancellationToken cts)
    {
        if (OnlinePluginInfo is not null)
        {
            var request = new HttpRequestMessage()
            {
                RequestUri = new Uri($"{ConfigManger.ApiUrl}/api/plugin/avatar"),
                Method = HttpMethod.Get
            };
            request.Headers.Add("id", PluginBaseInfo.Id.ToString());
            var sendAsync = await PluginManager._httpClient.SendAsync(request, cts);
            var stringAsync = await sendAsync.Content.ReadAsStringAsync(cts);
            var deserializeObject = (JObject)JsonConvert.DeserializeObject(stringAsync);
            if (deserializeObject["flag"].ToObject<bool>()) 
                Icon = new Bitmap(new MemoryStream(deserializeObject["data"].ToObject<byte[]>()));
        }

        if (PluginLocalInfo is not null)
        {
            if (!File.Exists($"{PluginLocalInfo.Path}avatar.png"))
            {
                var request = new HttpRequestMessage()
                {
                    RequestUri = new Uri($"{ConfigManger.ApiUrl}/api/plugin/avatar"),
                    Method = HttpMethod.Get
                };
                request.Headers.Add("id", PluginBaseInfo.Id.ToString());
                var sendAsync = await PluginManager._httpClient.SendAsync(request, cts);
                var stringAsync = await sendAsync.Content.ReadAsStringAsync(cts);
                var deserializeObject = (JObject)JsonConvert.DeserializeObject(stringAsync);
                if (deserializeObject["flag"].ToObject<bool>())
                {
                    var bitmap = new Bitmap(new MemoryStream(deserializeObject["data"].ToObject<byte[]>()));
                    bitmap.Save($"{PluginLocalInfo.Path}avatar.png");
                    Icon = bitmap;
                }
            }
            else
            {
                Icon =
                    new Bitmap($"{PluginLocalInfo.Path}avatar.png");
            }
        }
        
    }

    private async ValueTask GetAuthorName(CancellationToken cts)
    {
        var request = new HttpRequestMessage()
        {
            RequestUri = new Uri($"{ConfigManger.ApiUrl}/api/user/baseInfo"),
            Method = HttpMethod.Get
        };
        request.Headers.Add("id", PluginBaseInfo.AuthorId.ToString());
        var async =await PluginManager._httpClient.SendAsync(request, cts);
        var stringAsync =await async.Content.ReadAsStringAsync(cts);
        var deserializeObject = (JObject)JsonConvert.DeserializeObject(stringAsync);

        AuthorName=deserializeObject["data"]["userName"].ToString();
    }

    private string? _authorName;
    public string? AuthorName
    {
        set => SetProperty(ref _authorName, value);
        get
        {
            if (_authorName is null)
            {
                lock (_cancellationTokenSource)
                {
                    if (_cancellationTokenSource.IsCancellationRequested)
                    {
                        return null;
                    }
                    ResiliencePipeline.ExecuteAsync(GetAuthorName,_cancellationTokenSource.Token);
                }
                
            }
            return _authorName;
        }
    }

    public bool InLocal => PluginManager.GetPluginLocalInfoByPlgStr(PluginBaseInfo.NameSign) is not null;
    public PluginLocalInfo? PluginLocalInfo { get; set; }
    public OnlinePluginInfo? OnlinePluginInfo { get; set; }
    [Required]
    public bool IsLocal { get; init; }
    
    private bool? _canUpdate;

    public bool? CanUpdate
    {
        get
        {
            if (_canUpdate is null)
            
            lock (_cancellationTokenSource)
            {
                if (_cancellationTokenSource.IsCancellationRequested)
                {
                    return false;
                }
                ResiliencePipeline.ExecuteAsync(CheckCanUpdate,_cancellationTokenSource.Token);
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
        try
        {
            var httpResponseMessage =await PluginManager._httpClient
                .GetAsync($"{ConfigManger.ApiUrl}/api/plugin/{PluginBaseInfo.Id}");
            var httpContent =await httpResponseMessage.Content.ReadAsStringAsync(cts);
            var deserializeObject = (JObject)JsonConvert.DeserializeObject(httpContent);
            var o = deserializeObject["data"];
            if (o.Type==JTokenType.Integer)
            {
                CanUpdate = false;
                return;
            }
            CanUpdate = o["lastVersionId"].ToObject<int>() > PluginLocalInfo.PluginBaseInfo.VersionId;
            CanUpdateVersion = o["lastVersion"].ToString();
            CanUpdateVersionId = o["lastVersionId"].ToObject<int>();
        }
        catch (Exception e)
        {
            CanUpdate = false;
        }
    }
    [ObservableProperty]
    private string _canUpdateVersion;
    [ObservableProperty]
    private int _canUpdateVersionId;
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
    public int VersionId =>
        IsLocal ? PluginLocalInfo.PluginBaseInfo.VersionId : OnlinePluginInfo.LastVersionId;
    private string? _description;
    public string? Description
    {
        get
        {
            if (_description is null)
            {
                lock (_cancellationTokenSource)
                {
                    if (_cancellationTokenSource.IsCancellationRequested)
                    {
                        return null;
                    }
                    ResiliencePipeline.ExecuteAsync(GetDescription,_cancellationTokenSource.Token);
                }
            }
            
            return _description;
        }
        set => SetProperty(ref _description, value);
    }

    private async ValueTask GetDescription(CancellationToken cts)
    {
        if (IsLocal&& OnlinePluginInfo is null)
        {
            OnlinePluginInfo =await PluginManager.GetOnlinePluginInfo(PluginBaseInfo.Id, true);
        }

        if (OnlinePluginInfo is null)
        {
            Description= "该插件远端未找到";
            return;
        }

        Description= OnlinePluginInfo.Description;
    }
    
    private List<VersionDetail>? _versionDetails;
    public List<VersionDetail>? VersionDetails
    {
        get
        {
            if (_versionDetails is null)
            {
                lock (_cancellationTokenSource)
                {
                    if (_cancellationTokenSource.IsCancellationRequested)
                    {
                        return null;
                    }
                    ResiliencePipeline.ExecuteAsync(GetVersionDetails,_cancellationTokenSource.Token);
                }
            }
           
            return _versionDetails;
        }
        set => SetProperty(ref _versionDetails, value);
    }

    private async ValueTask GetVersionDetails(CancellationToken cts)
    {
        var httpResponseMessage =await PluginManager._httpClient
            .GetAsync($"{ConfigManger.ApiUrl}/api/plugin/{PluginBaseInfo.Id}", cts);
        var httpContent =await httpResponseMessage.Content.ReadAsStringAsync(cts);
        var deserializeObject2 = (JObject)JsonConvert.DeserializeObject(httpContent);
        var o = deserializeObject2["data"];
        var request = new HttpRequestMessage()
        {
            RequestUri =
                new Uri($"{ConfigManger.ApiUrl}/api/plugin/detail/{PluginBaseInfo.Id}/{o["lastVersionId"].ToObject<int>()}"),
            Method = HttpMethod.Get
        };
        request.Headers.Add("AllBeforeThisVersion", true.ToString());
        var sendAsync = await PluginManager._httpClient.SendAsync(request, cts);
        var stringAsync = await sendAsync.Content.ReadAsStringAsync(cts);
        var deserializeObject = (JObject)JsonConvert.DeserializeObject(stringAsync);
        var list = deserializeObject["data"].ToObject<List<VersionDetail>>();
        list.Reverse();
        VersionDetails = list;
    }
}