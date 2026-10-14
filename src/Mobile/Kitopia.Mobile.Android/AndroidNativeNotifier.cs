using System;
using Android.App;
using Android.Content;
using Android.OS;
using AndroidX.Core.App;
using Kitopia.Mobile.Services;

namespace Kitopia.Mobile;

public sealed class AndroidNativeNotifier : INativeNotifier
{
    private const string ChannelId = "kitopia_chat";
    private const string ChannelName = "设备消息";
    private const int NotificationIdBase = 1000;
    private static int _notificationIdCounter = NotificationIdBase;

    private readonly Context _context;

    public AndroidNativeNotifier(Context context)
    {
        _context = context;
        CreateChannel();
    }

    public void Show(string title, string message)
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(33)
            && _context.CheckSelfPermission("android.permission.POST_NOTIFICATIONS")
            != Android.Content.PM.Permission.Granted)
        {
            return;
        }

        var intent = _context.PackageManager!.GetLaunchIntentForPackage(_context.PackageName!);
        var pendingIntent = PendingIntent.GetActivity(
            _context,
            0,
            intent,
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

        var builder = new NotificationCompat.Builder(_context, ChannelId)
            .SetSmallIcon(Android.Resource.Drawable.IcDialogInfo)
            .SetContentTitle(title)
            .SetContentText(message)
            .SetContentIntent(pendingIntent)
            .SetAutoCancel(true)
            .SetPriority(NotificationCompat.PriorityHigh);

        var notificationManager = NotificationManagerCompat.From(_context);
        notificationManager.Notify(Interlocked.Increment(ref _notificationIdCounter), builder.Build());
    }

    private void CreateChannel()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.O) return;

        var channel = new NotificationChannel(
            ChannelId,
            ChannelName,
            NotificationImportance.High)
        {
            Description = "设备聊天的消息通知"
        };

        var notificationManager = _context.GetSystemService(Context.NotificationService)
            as NotificationManager;
        notificationManager?.CreateNotificationChannel(channel);
    }
}
