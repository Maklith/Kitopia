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
        return $"{nameof(screenCaptureInfo.Index)}={screenCaptureInfo.Index},{nameof(screenCaptureInfo.hMonitor)}={(uint)screenCaptureInfo.hMonitor},{nameof(screenCaptureInfo.hdcMonitor)}={(uint)screenCaptureInfo.hdcMonitor},{nameof(screenCaptureInfo.X)}={screenCaptureInfo.X},{nameof(screenCaptureInfo.Y)}={screenCaptureInfo.Y},{nameof(screenCaptureInfo.Width)}={screenCaptureInfo.Width},{nameof(screenCaptureInfo.Height)}={screenCaptureInfo.Height}";
    }

    public object Deserialize(ReadOnlySpan<byte> value)
    {
        var str = Encoding.UTF8.GetString(value);
        
        var split = str.Split(',');
        if (split.Length != 7)
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
            Index = uint.Parse(dic[nameof(ScreenCaptureInfo.Index)]),
            hMonitor = (IntPtr)uint.Parse(dic[nameof(ScreenCaptureInfo.hMonitor)]),
            hdcMonitor = (IntPtr)uint.Parse(dic[nameof(ScreenCaptureInfo.hdcMonitor)]),
            X = int.Parse(dic[nameof(ScreenCaptureInfo.X)]),
            Y = int.Parse(dic[nameof(ScreenCaptureInfo.Y)]),
            Width = int.Parse(dic[nameof(ScreenCaptureInfo.Width)]),
            Height = int.Parse(dic[nameof(ScreenCaptureInfo.Height)]),
        };
        return screenCaptureInfo;
        
    }
}