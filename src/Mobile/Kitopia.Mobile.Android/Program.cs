using Avalonia;
using Avalonia.Android;

namespace Kitopia.Mobile;

public static class Program
{
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBootstrap.BuildAvaloniaApp()
            .UseAndroid();
    }
}
