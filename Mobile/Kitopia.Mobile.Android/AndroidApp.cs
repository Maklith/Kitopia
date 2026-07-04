using System;
using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using Kitopia.DeviceCommunication.Diagnostics;
using Kitopia.Mobile.Services;

namespace Kitopia.Mobile;

[Application]
public sealed class AndroidApp : AvaloniaAndroidApplication<App>
{
    public AndroidApp(IntPtr javaReference, JniHandleOwnership transfer)
        : base(javaReference, transfer)
    {
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        DeviceCommunicationDiagnostics.Current = new AndroidDeviceCommunicationDiagnostics();
        MobilePlatformRuntime.Current = new AndroidPlatformRuntimeFeatures();
        _ = builder;
        return AppBootstrap.BuildAvaloniaApp().UseAndroid();
    }
}
