using Android.App;
using Android.Content;
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
        StopKeepAliveService();
        if (Avalonia.Application.Current is App app)
        {
            _ = app.ResumeAsync();
        }
    }

    protected override void OnPause()
    {
        // Keep the process (discovery + listener) alive in the background via a foreground service
        // instead of tearing the communication host down. This lets messages keep arriving and the
        // file picker (which briefly backgrounds the app) no longer drops the peer connection.
        StartKeepAliveService();
        base.OnPause();
    }

    private void StartKeepAliveService()
    {
        var intent = new Intent(this, typeof(KitopiaForegroundService));
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            StartForegroundService(intent);
        }
        else
        {
            StartService(intent);
        }
    }

    private void StopKeepAliveService()
    {
        StopService(new Intent(this, typeof(KitopiaForegroundService)));
    }
}
