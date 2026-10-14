#if LINUX
    using Kitopia.Desktop.Platform.Linux;
#endif

using System;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Kitopia.Desktop.Features.Indexing;
using Kitopia.Desktop.Features.Services.Interfaces;
using Kitopia.Desktop.Features.ViewModel.Main;
using Kitopia.Desktop.Windows;
using Kitopia.Feature.Localization;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
#if WINDOWS
using Vanara.Extensions;
using Vanara.PInvoke;
using Windows.UI.ViewManagement;
#endif

namespace Kitopia.Desktop;

public partial class App : Application
{
    private DispatcherTimer? _foregroundIndexTimer;
#if WINDOWS
    // Windows 11 24H2 replaced the battery-only status with Off/Standard/High Savings.
    private static readonly Guid EnergySaverSettingGuid = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 26100)
        ? new Guid("550e8400-e29b-41d4-a716-446655440000")
        : new Guid("e00958c0-c213-4ace-ac77-fecced2eeea5");
    private UISettings? _uiSettings;
    private User32.SafeHPOWERSETTINGNOTIFY? _powerSettingNotification;
    private bool _energySaverEnabled = true;
#endif

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        ApplyLanguage();
#if WINDOWS
        SetWindowEffectsEnabled(false);
#else
        SetWindowEffectsEnabled(true);
#endif
    }


    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            Lang.Current.PropertyChanged += OnLanguageChanged;
            desktop.Exit += (_, _) => Lang.Current.PropertyChanged -= OnLanguageChanged;
            desktop.MainWindow = ServiceManager.Services.GetService<MainWindow>();
#if WINDOWS
            InitializeWindowEffects(desktop.MainWindow!);
            desktop.Exit += (_, _) => StopWindowEffects(desktop.MainWindow!);
#endif
            DataContext = new AppViewModel();
            var index = ServiceManager.Services.GetRequiredService<IIndexService>();
            var navigation = ServiceManager.Services.GetRequiredService<INavigationService>();
            _foregroundIndexTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _foregroundIndexTimer.Tick += (_, _) =>
            {
#if WINDOWS
                var foreground = User32.GetForegroundWindow();
                if (foreground.IsNull)
                {
                    index.SetForegroundPause(false);
                    return;
                }

                User32.GetWindowThreadProcessId(foreground, out var processId);
                var isKitopiaForeground = processId == Environment.ProcessId;
                var isIndexPageForeground = (nint)foreground == desktop.MainWindow?.TryGetPlatformHandle()?.Handle
                                            && navigation.CurrentPageRoute == "index/status";
#else
                var activeWindow = desktop.Windows.FirstOrDefault(window => window.IsActive);
                var isKitopiaForeground = activeWindow is not null;
                var isIndexPageForeground = ReferenceEquals(activeWindow, desktop.MainWindow)
                                            && navigation.CurrentPageRoute == "index/status";
#endif
                index.SetForegroundPause(isKitopiaForeground && !isIndexPageForeground);
            };
            _foregroundIndexTimer.Start();
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void SetWindowEffectsEnabled(bool enabled)
    {
        Resources["KitopiaWindowBackgroundOpacity"] = enabled ? 0.55 : 1.0;
        Resources["KitopiaWindowTransparencyLevels"] = new[]
        {
            enabled ? WindowTransparencyLevel.AcrylicBlur : WindowTransparencyLevel.None
        };
    }

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(Lang.Language)) Dispatcher.UIThread.Post(ApplyLanguage);
    }

    private void ApplyLanguage()
    {
        foreach (var theme in Styles.OfType<Semi.Avalonia.SemiTheme>()) theme.Locale = Lang.Current.Culture;
        foreach (var theme in Styles.OfType<Ursa.Themes.Semi.Legacy.SemiTheme>()) theme.Locale = Lang.Current.Culture;
    }

#if WINDOWS
    private void InitializeWindowEffects(Window window)
    {
        _uiSettings = new UISettings();
        _uiSettings.AdvancedEffectsEnabledChanged += OnWindowEffectsChanged;
        Win32Properties.AddWndProcHookCallback(window, OnWindowPowerBroadcast);
        var settingGuid = EnergySaverSettingGuid;
        _powerSettingNotification = User32.RegisterPowerSettingNotification(
            new HANDLE(window.TryGetPlatformHandle()!.Handle), in settingGuid, User32.DEVICE_NOTIFY.DEVICE_NOTIFY_WINDOW_HANDLE);
        if (_powerSettingNotification.IsInvalid)
            Kitopia.Desktop.Features.Services.LogManager.Logger.Warning("无法注册节能模式通知，保留不透明窗口背景");
    }

    private void StopWindowEffects(Window window)
    {
        _uiSettings!.AdvancedEffectsEnabledChanged -= OnWindowEffectsChanged;
        _powerSettingNotification?.Dispose();
        Win32Properties.RemoveWndProcHookCallback(window, OnWindowPowerBroadcast);
    }

    private IntPtr OnWindowPowerBroadcast(IntPtr hwnd, uint msg, IntPtr wparam, IntPtr lparam, ref bool handled)
    {
        if (msg == (uint)User32.WindowMessage.WM_POWERBROADCAST &&
            wparam.ToInt64() == (long)User32.PowerBroadcastType.PBT_POWERSETTINGCHANGE && lparam != IntPtr.Zero)
        {
            var setting = lparam.ToStructure<User32.POWERBROADCAST_SETTING>();
            if (setting.PowerSetting == EnergySaverSettingGuid && setting.DataLength == sizeof(uint))
            {
                _energySaverEnabled = BitConverter.ToUInt32(setting.Data) != 0;
                SetWindowEffectsEnabled(_uiSettings?.AdvancedEffectsEnabled != false && !_energySaverEnabled);
            }
        }
        return IntPtr.Zero;
    }

    private void OnWindowEffectsChanged(object? sender, object args)
    {
        Dispatcher.UIThread.Post(() =>
            SetWindowEffectsEnabled(_uiSettings!.AdvancedEffectsEnabled && !_energySaverEnabled));
    }
#endif
}
