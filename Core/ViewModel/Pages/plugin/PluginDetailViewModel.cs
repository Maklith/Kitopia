using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using Core.SDKs.Services.Config;
using Core.SDKs.Services.Plugin;


namespace Core.ViewModel.Pages.plugin;

public partial class PluginDetailViewModel : ObservableObject
{
    internal class ApiResponse
    {
        public bool flag { get; set; }
        public OnlinePluginInfo? data { get; set; }
    }
    public string PluginStr;
    [ObservableProperty] private bool _remote = false;
    [ObservableProperty] private bool _loading = true;
    private bool avatarLocalFirst = true;
    [ObservableProperty]
    public PluginInfoUiHelper? _pluginInfo;
    public PluginDetailViewModel(PluginInfoUiHelper pluginStr)
    {
        PluginInfo = pluginStr;
            //Task.Run(GetInfo);
    }

    private async Task GetInfo()
    {
        await GetOnlinePluginInfo();
        Loading = false; 
    }
    private async Task GetOnlinePluginInfo()
    {
        var request = new HttpRequestMessage()
        {
            RequestUri = new Uri($"{ConfigManger.ApiUrl}/api/plugin/{PluginStr}"),
            Method = HttpMethod.Get
        };
        var async =await PluginManager._httpClient.SendAsync(request);
        var stringAsync =await async.Content.ReadAsStringAsync();
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
        var apiResponse = JsonSerializer.Deserialize<ApiResponse>(stringAsync, options);
        if (apiResponse is { data: not null })
        {
            PluginInfo = new PluginInfoUiHelper()
            {
                PluginBaseInfo = apiResponse.data.ToPluginBaseInfo(),
                OnlinePluginInfo = apiResponse.data,
                IsLocal = false
            };
            Remote = true;
            
        }
          
    }

   
}