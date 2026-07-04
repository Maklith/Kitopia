using Android.App;
using Android.Content.PM;
using Avalonia;
using Avalonia.Android;

namespace Kitopia.Mobile;

[Activity(
    Label = "Kitopia Mobile",
    Theme = "@style/Theme.AppCompat.Light.NoActionBar",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public sealed class MainActivity : AvaloniaMainActivity
{
    protected override void OnResume()
    {
        base.OnResume();
        if (Avalonia.Application.Current is App app)
        {
            _ = app.ResumeAsync();
        }
    }

    protected override void OnPause()
    {
        if (Avalonia.Application.Current is App app)
        {
            _ = app.PauseAsync();
        }

        base.OnPause();
    }
}
