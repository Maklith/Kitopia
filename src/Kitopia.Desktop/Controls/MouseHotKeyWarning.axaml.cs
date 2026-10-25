using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Kitopia.Desktop.Features.Services.Config;
using Kitopia.Desktop.Features.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using PluginCore.Config;
#if WINDOWS
using SharpHook;
using SharpHook.Data;
#endif

namespace Kitopia.Desktop.Controls;

public sealed partial class MouseHotKeyWarning : UserControl
{
    public static readonly StyledProperty<bool> IsMouseHotKeyProperty =
        AvaloniaProperty.Register<MouseHotKeyWarning, bool>(nameof(IsMouseHotKey));

    private KitopiaConfig? _config;
#if WINDOWS
    private SimpleGlobalHook? _hook;
    private bool _hookRunning;
#endif

    public bool IsMouseHotKey
    {
        get => GetValue(IsMouseHotKeyProperty);
        set => SetValue(IsMouseHotKeyProperty, value);
    }

    public MouseHotKeyWarning()
    {
        InitializeComponent();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsMouseHotKeyProperty) UpdateWarning();
    }

    protected override void OnAttachedToLogicalTree(LogicalTreeAttachmentEventArgs e)
    {
        base.OnAttachedToLogicalTree(e);
        _config = ConfigManger.Config;
        if (_config is not null) _config.ConfigChanged += OnConfigChanged;
#if WINDOWS
        _hook = ServiceManager.Services?.GetService<SimpleGlobalHook>();
        if (_hook is not null)
        {
            _hook.HookEnabled += OnHookChanged;
            _hook.HookDisabled += OnHookChanged;
        }
        _hookRunning = _hook is { IsRunning: true, IsDisposed: false };
#endif
        UpdateWarning();
    }

    protected override void OnDetachedFromLogicalTree(LogicalTreeAttachmentEventArgs e)
    {
        if (_config is not null) _config.ConfigChanged -= OnConfigChanged;
        _config = null;
#if WINDOWS
        if (_hook is not null)
        {
            _hook.HookEnabled -= OnHookChanged;
            _hook.HookDisabled -= OnHookChanged;
        }
        _hook = null;
        _hookRunning = false;
#endif
        base.OnDetachedFromLogicalTree(e);
    }

    private void OnConfigChanged(object? sender, ConfigChangedArgs e)
    {
        if (e.Name == nameof(KitopiaConfig.mouseCapture)) UpdateWarning();
    }

    private void WarningIndicator_OnPointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is Button button) button.Flyout?.ShowAt(button);
    }

    private void EnableCaptureButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_config is null || _config.mouseCapture) return;
        _config.mouseCapture = true;
        _config.OnConfigChanged(this, nameof(KitopiaConfig.mouseCapture), true);
        ServiceManager.Services.GetRequiredService<IConfigService>().Save(nameof(KitopiaConfig));
    }

#if WINDOWS
    private void OnHookChanged(object? sender, HookEventArgs e) => Dispatcher.UIThread.Post(() =>
    {
        if (!ReferenceEquals(sender, _hook)) return;
        // HookDisabled can arrive before SharpHook updates IsRunning.
        _hookRunning = e.RawEvent.Type == EventType.HookEnabled;
        UpdateWarning();
    });
#endif

    private void UpdateWarning()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(UpdateWarning);
            return;
        }

        var captureDisabled = _config?.mouseCapture != true;
#if WINDOWS
        var hookStopped = !_hookRunning;
#else
        const bool hookStopped = true;
#endif
        CaptureWarning.IsVisible = captureDisabled;
        EnableCaptureButton.IsVisible = captureDisabled && _config is not null;
        HookWarning.IsVisible = hookStopped;
        if (Classes.Contains("compact") && (!IsMouseHotKey || (!captureDisabled && !hookStopped)))
            this.GetVisualDescendants().OfType<Button>().FirstOrDefault(button => button.Name == "WarningIndicator")?.Flyout?.Hide();
        IsVisible = IsMouseHotKey && (captureDisabled || hookStopped);
    }
}
