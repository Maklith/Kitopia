using System;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.App;
using Kitopia.DeviceCommunication.Diagnostics;

namespace Kitopia.Mobile;

[Service(ForegroundServiceType = ForegroundService.TypeDataSync, Exported = false)]
public sealed class KitopiaForegroundService : Service
{
    private const int NotificationId = 1;
    private const string ChannelId = "kitopia_service";
    private const string ChannelName = "后台服务";
    private const string LogCategory = "AndroidForegroundService";

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

        _ = EnsureCommunicationHostStartedAsync();
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
        PendingIntent? pendingIntent = null;
        var intent = PackageManager?.GetLaunchIntentForPackage(PackageName ?? string.Empty);
        if (intent is not null)
        {
            pendingIntent = PendingIntent.GetActivity(
                this,
                0,
                intent,
                PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
        }

        var builder = new NotificationCompat.Builder(this, ChannelId);
        builder.SetContentTitle(title);
        builder.SetContentText(text);
        builder.SetSmallIcon(Android.Resource.Drawable.IcDialogInfo);
        builder.SetOngoing(true);
        builder.SetPriority(NotificationCompat.PriorityLow);

        if (pendingIntent is not null)
        {
            builder.SetContentIntent(pendingIntent);
        }

        return builder.Build()!;
    }

    private static async Task EnsureCommunicationHostStartedAsync()
    {
        if (Avalonia.Application.Current is not App app)
        {
            DeviceCommunicationDiagnostics.Warning(
                LogCategory,
                "Avalonia application is unavailable; foreground service cannot resume communication host.");
            return;
        }

        try
        {
            await app.ResumeAsync();
            DeviceCommunicationDiagnostics.Info(LogCategory, "Communication host resumed by foreground service.");
        }
        catch (Exception ex)
        {
            DeviceCommunicationDiagnostics.Error(
                LogCategory,
                "Failed to resume communication host from foreground service.",
                ex);
        }
    }
}
