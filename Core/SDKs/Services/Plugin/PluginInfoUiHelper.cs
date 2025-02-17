
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
    public PluginBaseInfo PluginBaseInfo { get; init; }
    
    private Bitmap? _icon;
    public Bitmap? Icon
    {
        get
        {
            if (_icon is null)
            {
                ResiliencePipeline.ExecuteAsync(GetIcon);
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

    private string _authorName;
    public string AuthorName
    {
        set => SetProperty(ref _authorName, value);
        get
        {
            if (_authorName is null)
            {
                ResiliencePipeline.ExecuteAsync(GetAuthorName);
            }
            return _authorName;
        }
    }

    public bool InLocal => PluginManager.GetPluginLocalInfoByPlgStr(PluginBaseInfo.NameSign) is not null;
    public PluginLocalInfo? PluginLocalInfo { get; init; }
    public OnlinePluginInfo? OnlinePluginInfo { get; init; }
    
    public bool CanUpdate{ get; init; }
    public string CanUpdateVersion{ get; init; }
    public void Dispose()
    {
        Icon?.Dispose();
    }
}