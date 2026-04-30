using System;
using System.Collections.Generic;
using System.Text;
using PluginCore;
using PluginCore.CustomScenario;

namespace KitopiaEx.CustomScenarioValueSerializer;

public class ScreenCaptureInfoCustomScenarioValueSerializer : ICustomScenarioValueSerializer {
    public string Serialize<T>(T value) {
        if (value is not ScreenCaptureInfo info) {
            return "";
        }

        List<string> list = new List<string>();

        list.Add($"ScreenCaptureType={info.ScreenCaptureType}");
        list.Add($"SdrWhiteLevelScale={info.SdrWhiteLevelScale}");

        // RequestRect
        if (info.RequestRect.HasValue) {
            list.Add($"RequestRect.X={info.RequestRect.Value.X}");
            list.Add($"RequestRect.Y={info.RequestRect.Value.Y}");
            list.Add($"RequestRect.Width={info.RequestRect.Value.Width}");
            list.Add($"RequestRect.Height={info.RequestRect.Value.Height}");
        }
        else {
            list.Add("RequestRect=null");
        }

        // ScreenInfo
        if (info.ScreenInfo.HasValue) {
            list.Add($"ScreenInfo.X={info.ScreenInfo.Value.X}");
            list.Add($"ScreenInfo.Y={info.ScreenInfo.Value.Y}");
            list.Add($"ScreenInfo.Width={info.ScreenInfo.Value.Width}");
            list.Add($"ScreenInfo.Height={info.ScreenInfo.Value.Height}");
        }
        else {
            list.Add("ScreenInfo=null");
        }

        // WindowInfo，不序列化 Hwnd
        if (info.WindowInfo.HasValue) {
            var windowInfo = info.WindowInfo.Value;
            list.Add("WindowInfo.Exists=true");
            list.Add($"WindowInfo.Title={Escape(windowInfo.Title)}");
            list.Add($"WindowInfo.ModuleFileName={Escape(windowInfo.ModuleFileName)}");
            list.Add($"WindowInfo.Rect.X={windowInfo.Rect.X}");
            list.Add($"WindowInfo.Rect.Y={windowInfo.Rect.Y}");
            list.Add($"WindowInfo.Rect.Width={windowInfo.Rect.Width}");
            list.Add($"WindowInfo.Rect.Height={windowInfo.Rect.Height}");
            list.Add($"WindowInfo.ZIndex={windowInfo.ZIndex}");
        }
        else {
            list.Add("WindowInfo.Exists=false");
        }

        return string.Join(",", list);
    }

    public object? Deserialize(string? str) {
        if (string.IsNullOrWhiteSpace(str)) {
            return null;
        }

        var split = SplitSerializedString(str);
        if (split.Count == 0) {
            return null;
        }

        Dictionary<string, string> dic = new Dictionary<string, string>();

        foreach (var se in split) {
            var index = se.IndexOf('=');
            if (index <= 0) {
                continue;
            }

            var key = se.Substring(0, index);
            var value = se.Substring(index + 1);
            dic[key] = value;
        }

        if (!dic.TryGetValue("ScreenCaptureType", out var screenCaptureTypeStr)) {
            return null;
        }

        ScreenCaptureInfo info = new ScreenCaptureInfo {
            ScreenCaptureType = Enum.Parse<ScreenCaptureType>(screenCaptureTypeStr),
            SdrWhiteLevelScale = dic.TryGetValue("SdrWhiteLevelScale", out var scaleStr)
                ? float.Parse(scaleStr)
                : 1.0f
        };

        // RequestRect
        if (!(dic.TryGetValue("RequestRect", out var requestRectNull) && requestRectNull == "null")) {
            if (dic.TryGetValue("RequestRect.X", out var requestXStr) &&
                dic.TryGetValue("RequestRect.Y", out var requestYStr) &&
                dic.TryGetValue("RequestRect.Width", out var requestWidthStr) &&
                dic.TryGetValue("RequestRect.Height", out var requestHeightStr)) {
                info.RequestRect = new Rect(
                    int.Parse(requestXStr),
                    int.Parse(requestYStr),
                    int.Parse(requestWidthStr),
                    int.Parse(requestHeightStr));
            }
        }

        // ScreenInfo
        if (!(dic.TryGetValue("ScreenInfo", out var screenInfoNull) && screenInfoNull == "null")) {
            if (dic.TryGetValue("ScreenInfo.X", out var screenXStr) &&
                dic.TryGetValue("ScreenInfo.Y", out var screenYStr) &&
                dic.TryGetValue("ScreenInfo.Width", out var screenWidthStr) &&
                dic.TryGetValue("ScreenInfo.Height", out var screenHeightStr)) {
                info.ScreenInfo = new Rect(
                    int.Parse(screenXStr),
                    int.Parse(screenYStr),
                    int.Parse(screenWidthStr),
                    int.Parse(screenHeightStr));
            }
        }

        // WindowInfo，不反序列化 Hwnd
        if (dic.TryGetValue("WindowInfo.Exists", out var windowExistsStr) &&
            bool.TryParse(windowExistsStr, out var windowExists) &&
            windowExists) {
            WindowInfo windowInfo = new WindowInfo {
                Title = dic.TryGetValue("WindowInfo.Title", out var title) ? Unescape(title) : "",
                ModuleFileName = dic.TryGetValue("WindowInfo.ModuleFileName", out var moduleFileName)
                    ? Unescape(moduleFileName)
                    : "",
                Rect = new Rect(
                    dic.TryGetValue("WindowInfo.Rect.X", out var rectXStr) ? int.Parse(rectXStr) : 0,
                    dic.TryGetValue("WindowInfo.Rect.Y", out var rectYStr) ? int.Parse(rectYStr) : 0,
                    dic.TryGetValue("WindowInfo.Rect.Width", out var rectWidthStr) ? int.Parse(rectWidthStr) : 0,
                    dic.TryGetValue("WindowInfo.Rect.Height", out var rectHeightStr) ? int.Parse(rectHeightStr) : 0
                ),
                ZIndex = dic.TryGetValue("WindowInfo.ZIndex", out var zIndexStr) ? int.Parse(zIndexStr) : 0,
                Hwnd = IntPtr.Zero
            };

            info.WindowInfo = windowInfo;
        }

        // hMonitor 不序列化，所以反序列化后给默认值
        info.HMonitor = IntPtr.Zero;

        return info;
    }

    private static string Escape(string? value) {
        if (string.IsNullOrEmpty(value)) {
            return "";
        }

        return value
            .Replace("\\", "\\\\")
            .Replace(",", "\\,")
            .Replace("=", "\\=");
    }

    private static string Unescape(string? value) {
        if (string.IsNullOrEmpty(value)) {
            return "";
        }

        StringBuilder sb = new StringBuilder();
        bool escaping = false;

        foreach (char c in value) {
            if (escaping) {
                sb.Append(c);
                escaping = false;
            }
            else if (c == '\\') {
                escaping = true;
            }
            else {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    private static List<string> SplitSerializedString(string str) {
        List<string> result = new List<string>();
        StringBuilder sb = new StringBuilder();
        bool escaping = false;

        foreach (char c in str) {
            if (escaping) {
                sb.Append(c);
                escaping = false;
            }
            else if (c == '\\') {
                sb.Append(c);
                escaping = true;
            }
            else if (c == ',') {
                result.Add(sb.ToString());
                sb.Clear();
            }
            else {
                sb.Append(c);
            }
        }

        if (sb.Length > 0) {
            result.Add(sb.ToString());
        }

        return result;
    }
}