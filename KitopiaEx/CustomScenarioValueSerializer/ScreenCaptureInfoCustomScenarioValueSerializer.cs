using System;
using System.Collections.Generic;
using System.Text;
using Core.SDKs.CustomScenario;
using PluginCore;

namespace KitopiaEx.CustomScenarioValueSerializer;

public class ScreenCaptureInfoCustomScenarioValueSerializer : ICustomScenarioValueSerializer
{
    public string Serialize<T>(T value)
    {
        if (value is not ScreenCaptureInfo screenCaptureInfo)
        {
            return "";
        }
        return $"ScreenCaptureType={screenCaptureInfo.ScreenCaptureType},WindowInfo.Title={screenCaptureInfo.WindowInfo.Title},ScreenInfo.X={screenCaptureInfo.ScreenInfo.X},ScreenInfo.Y={screenCaptureInfo.ScreenInfo.Y},ScreenInfo.Height={screenCaptureInfo.ScreenInfo.Height},ScreenInfo.Width={screenCaptureInfo.ScreenInfo.Width},{nameof(screenCaptureInfo.X)}={screenCaptureInfo.X},{nameof(screenCaptureInfo.Y)}={screenCaptureInfo.Y},{nameof(screenCaptureInfo.Width)}={screenCaptureInfo.Width},{nameof(screenCaptureInfo.Height)}={screenCaptureInfo.Height}";
    }

    public object Deserialize(ReadOnlySpan<byte> value)
    {
        var str = Encoding.UTF8.GetString(value);
        
        var split = str.Split(',');
        if (split.Length != 10)
        {
            return null;
        }
        Dictionary<string,string> dic = new Dictionary<string, string>();
        
        foreach (var se in split)
        {
            var strings = se.Split("=");
            dic.Add(strings[0],strings[1]);
        }
        
        var screenCaptureInfo = new ScreenCaptureInfo
        {
            ScreenCaptureType = Enum.Parse<ScreenCaptureType>(dic["ScreenCaptureType"]),
            ScreenInfo = new ScreenInfo
            {
                Height = int.Parse(dic["ScreenInfo.Height"]),
                Width = int.Parse(dic["ScreenInfo.Width"]),
                X = int.Parse(dic["ScreenInfo.X"]),
                Y = int.Parse(dic["ScreenInfo.Y"]),
            },
            WindowInfo = new WindowInfo()
            {
                Title = dic["WindowInfo.Title"],
            },
            X = int.Parse(dic[nameof(ScreenCaptureInfo.X)]),
            Y = int.Parse(dic[nameof(ScreenCaptureInfo.Y)]),
            Width = int.Parse(dic[nameof(ScreenCaptureInfo.Width)]),
            Height = int.Parse(dic[nameof(ScreenCaptureInfo.Height)]),
        };
        return screenCaptureInfo;
        
    }
}