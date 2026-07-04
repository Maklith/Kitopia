using Avalonia;

namespace Kitopia.Mobile;

public static class Program
{
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBootstrap.BuildAvaloniaApp();
    }
}

public static class AppBootstrap
{
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>();
    }
}
