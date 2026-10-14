using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
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
    private const int PostNotificationsPermissionRequestCode = 1001;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        RequestNotificationPermissionIfNeeded();
    }

    protected override void OnResume()
    {
        base.OnResume();
        RequestNotificationPermissionIfNeeded();
        StopKeepAliveService();
        if (Avalonia.Application.Current is App app)
        {
            app.SetActivityActive(true);
            _ = app.ResumeAsync();
        }
    }

    protected override void OnPause()
    {
        if (Avalonia.Application.Current is App app)
        {
            app.SetActivityActive(false);
        }

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

    private void RequestNotificationPermissionIfNeeded()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            return;
        }

        if (CheckSelfPermission(Android.Manifest.Permission.PostNotifications) == Permission.Granted)
        {
            return;
        }

        RequestPermissions(
            new[] { Android.Manifest.Permission.PostNotifications },
            PostNotificationsPermissionRequestCode);
    }
}
