using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media.Imaging;
using Core.SDKs.Services.Config;
using Core.SDKs.Services.Plugin;
using Core.ViewModel.Pages;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Core.AvaloniaControl.MarketPage;

public class IconCtr : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not null) return value;
        var onlinePluginInfo =
            ((Control)((CompiledBindingExtension)parameter).DefaultAnchor.Target).DataContext as OnlinePluginInfo;
        {
            if (onlinePluginInfo.Icon is not null) return onlinePluginInfo.Icon;

            Task.Run(async () =>
            {
                var request = new HttpRequestMessage()
                {
                    RequestUri = new Uri($"{ConfigManger.ApiUrl}/api/plugin/avatar"),
                    Method = HttpMethod.Get
                };
                request.Headers.Add("id", onlinePluginInfo.Id.ToString());
                var sendAsync = await PluginManager._httpClient.SendAsync(request);
                var stringAsync = await sendAsync.Content.ReadAsStringAsync();
                var deserializeObject = (JObject)JsonConvert.DeserializeObject(stringAsync);
                if (deserializeObject["flag"].ToObject<bool>())
                    onlinePluginInfo.Icon = new Bitmap(new MemoryStream(deserializeObject["data"].ToObject<byte[]>()));
            });
        }

        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}