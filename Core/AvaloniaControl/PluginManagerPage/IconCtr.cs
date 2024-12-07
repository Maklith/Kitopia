using System;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media.Imaging;
using Core.SDKs.Services.Config;
using Core.SDKs.Services.Plugin;
using Core.ViewModel.Pages;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PluginCore;


namespace KitopiaAvalonia.Converter.PluginManagerPage;

public class IconCtr : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not null) return value;
        var onlinePluginInfo =
            ((Control)((CompiledBindingExtension)parameter).DefaultAnchor.Target).DataContext as PluginInfo;
        {
            if (onlinePluginInfo.Icon is not null) return onlinePluginInfo.Icon;

            Task.Run(async () =>
            {
                if (!File.Exists($"{onlinePluginInfo.Path}{Path.DirectorySeparatorChar}avatar.png"))
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
                    {
                        var bitmap = new Bitmap(new MemoryStream(deserializeObject["data"].ToObject<byte[]>()));
                        bitmap.Save($"{onlinePluginInfo.Path}{Path.DirectorySeparatorChar}avatar.png");
                        onlinePluginInfo.Icon = bitmap;
                    }
                }
                else
                {
                    onlinePluginInfo.Icon =
                        new Bitmap($"{onlinePluginInfo.Path}{Path.DirectorySeparatorChar}avatar.png");
                }
            });
        }

        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}