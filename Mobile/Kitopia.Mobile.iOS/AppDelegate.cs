using Avalonia;
using Avalonia.iOS;
using Foundation;
using Kitopia.Mobile.Services;

namespace Kitopia.Mobile;

[Register("AppDelegate")]
public sealed class AppDelegate : AvaloniaAppDelegate<App>
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        MobilePlatformRuntime.Current = new IosPlatformRuntimeFeatures();
        _ = builder;
        return AppBootstrap.BuildAvaloniaApp().UseiOS();
    }
}
