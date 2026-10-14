using Avalonia.Styling;

namespace Kitopia.Desktop.Features.Services.Plugin;

public static class PluginAvaloniaResourceManager
{
    private static Dictionary<string, IStyle> _resources = new();

    public static IStyle GetStyle(string key)
    {
        if (_resources.ContainsKey(key)) return _resources[key];

        return null;
    }

    public static IStyle AddStyle(string key, IStyle style)
    {
        if (!_resources.ContainsKey(key)) _resources.Add(key, style);

        return style;
    }
}
