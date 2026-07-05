using System;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.App;

namespace Kitopia.Mobile;

[Service(ForegroundServiceType = ForegroundService.TypeDataSync, Exported = false)]
public sealed class KitopiaForegroundService : Service
{
    private const int NotificationId = 1;
    private const string ChannelId = "kitopia_service";
    private const string ChannelName = "后台服务";

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        CreateChannel();
        var notification = BuildNotification("Kitopia 正在运行", "设备发现和消息接收中");

        if (OperatingSystem.IsAndroidVersionAtLeast(29))
        {
            StartForeground(NotificationId, notification, ForegroundService.TypeDataSync);
        }
        else
        {
            StartForeground(NotificationId, notification);
        }

        return StartCommandResult.Sticky;
    }

    public override void OnDestroy()
    {
        StopForeground(StopForegroundFlags.Remove);
        base.OnDestroy();
    }

    private void CreateChannel()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.O) return;

        var channel = new NotificationChannel(ChannelId, ChannelName, NotificationImportance.Low)
        {
            Description = "保持 Kitopia 在后台运行",
            LockscreenVisibility = NotificationVisibility.Public
        };

        var notificationManager = GetSystemService(NotificationService) as NotificationManager;
        notificationManager?.CreateNotificationChannel(channel);
    }

    private Notification BuildNotification(string title, string text)
    {
        var intent = PackageManager!.GetLaunchIntentForPackage(PackageName!);
        var pendingIntent = PendingIntent.GetActivity(
            this,
            0,
            intent,
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

        return new NotificationCompat.Builder(this, ChannelId)
            .SetContentTitle(title)
            .SetContentText(text)
            .SetSmallIcon(Android.Resource.Drawable.IcDialogInfo)
            .SetContentIntent(pendingIntent)
            .SetOngoing(true)
            .SetPriority(NotificationCompat.PriorityLow)
            .Build();
    }
}
