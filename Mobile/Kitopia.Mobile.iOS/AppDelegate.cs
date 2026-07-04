using Avalonia;
using Avalonia.iOS;
using Foundation;

namespace Kitopia.Mobile;

[Register("AppDelegate")]
public sealed class AppDelegate : AvaloniaAppDelegate<App>
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        _ = builder;
        return AppBootstrap.BuildAvaloniaApp().UseiOS();
    }
}
