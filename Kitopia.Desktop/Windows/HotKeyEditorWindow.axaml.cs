using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Win32.Input;
using Kitopia.Desktop.Features.Services.HotKey;
using Kitopia.Desktop.Features.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using Ursa.Controls;

namespace Kitopia.Desktop.Windows;

public partial class HotKeyEditorWindow : UrsaWindow
{
    private readonly HotKeyModel _hotKeyModel;
    private HotKeyType _type;
    private EKey? _selectedKey;
    private ushort? _selectedMouseButton;

    public HotKeyEditorWindow(HotKeyModel hotKeyModel)
    {
        ArgumentNullException.ThrowIfNull(hotKeyModel);
        InitializeComponent();
        _hotKeyModel = hotKeyModel;
        Name.Text = Converter.HotKeySignNameToStringCtr.FormatFriendlyName(hotKeyModel.SignName);
        _type = hotKeyModel.Type;
        _selectedKey = hotKeyModel.SelectKey;
        _selectedMouseButton = hotKeyModel.MouseButton;
        KeyBoard.IsChecked = _type == HotKeyType.Keyboard;
        Mouse.IsChecked = _type == HotKeyType.Mouse;
        Slider.Value = hotKeyModel.PressTimeMillis;
        Ctrl.IsVisible = _type == HotKeyType.Keyboard && hotKeyModel.IsSelectCtrl;
        Alt.IsVisible = _type == HotKeyType.Keyboard && hotKeyModel.IsSelectAlt;
        Shift.IsVisible = _type == HotKeyType.Keyboard && hotKeyModel.IsSelectShift;
        Win.IsVisible = _type == HotKeyType.Keyboard && hotKeyModel.IsSelectWin;
        KeyName.Content = _type == HotKeyType.Keyboard ? hotKeyModel.SelectKey.ToString() : MouseButtonName(hotKeyModel.MouseButton);
        ProcessScope.SelectedIndex = (int)hotKeyModel.ProcessScope;
        ProcessNames.Text = string.Join(Environment.NewLine, hotKeyModel.ProcessNames);
        IgnoreTextInput.IsChecked = hotKeyModel.IgnoreTextInput;
        Opened += (_, _) => MouseCaptureArea.Focus();
    }

    private void HotKeyEditorWindow_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (_type != HotKeyType.Keyboard || !MouseCaptureArea.IsKeyboardFocusWithin) return;
        Ctrl.IsVisible = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        Alt.IsVisible = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        Shift.IsVisible = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        Win.IsVisible = e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        var key = e.Key == Key.System ? e.PhysicalKey.ToQwertyKey() : e.Key;
        if (key is Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin
            or Key.LeftCtrl or Key.RightCtrl or Key.None) return;
        _selectedKey = (EKey)KeyInterop.VirtualKeyFromKey(key);
        KeyName.Content = _selectedKey.ToString();
        KeyName.IsVisible = true;
        e.Handled = true;
    }

    private void ButtonBase_OnClick(object sender, RoutedEventArgs e)
    {
        if ((_type == HotKeyType.Keyboard && _selectedKey is null or EKey.未设置 or 0) ||
            (_type == HotKeyType.Mouse && _selectedMouseButton is null or 0 or ushort.MaxValue))
        {
            ValidationMessage.Text = "请先录入快捷键。";
            return;
        }
        var scope = (HotKeyProcessScope)ProcessScope.SelectedIndex;
        var processes = (ProcessNames.Text ?? "").Split(['\r', '\n', ',', ';', '，', '；', '、'],
                StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(HotKeyModel.NormalizeProcessName).Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (scope != HotKeyProcessScope.All && processes.Length == 0)
        {
            ValidationMessage.Text = "请至少填写一个进程名。";
            ProcessNames.Focus();
            return;
        }
        _hotKeyModel.IsSelectAlt = Alt.IsVisible;
        _hotKeyModel.IsSelectWin = Win.IsVisible;
        _hotKeyModel.IsSelectShift = Shift.IsVisible;
        _hotKeyModel.IsSelectCtrl = Ctrl.IsVisible;
        _hotKeyModel.SelectKey = _selectedKey ?? EKey.未设置;
        _hotKeyModel.MouseButton = _selectedMouseButton;
        _hotKeyModel.Type = _type;
        _hotKeyModel.PressTimeMillis = (ushort)Slider.Value;
        _hotKeyModel.ProcessScope = scope;
        _hotKeyModel.ProcessNames = processes;
        _hotKeyModel.IgnoreTextInput = IgnoreTextInput.IsChecked == true;
        if (!ServiceManager.Services.GetRequiredService<IHotKetImpl>().Modify(_hotKeyModel))
        {
            ValidationMessage.Text = "快捷键注册失败，可能已被其他程序占用。";
            return;
        }
        Close();
    }

    private void ButtonCancle_OnClick(object sender, RoutedEventArgs e) => Close();

    private void InputElement_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        MouseCaptureArea.Focus();
        if (_type != HotKeyType.Mouse) return;
        var properties = e.GetCurrentPoint(this).Properties;
        _selectedMouseButton = properties.PointerUpdateKind switch
        {
            PointerUpdateKind.LeftButtonPressed => (ushort)MouseHookType.LeftButton,
            PointerUpdateKind.RightButtonPressed => (ushort)MouseHookType.RightButton,
            PointerUpdateKind.MiddleButtonPressed => (ushort)MouseHookType.MiddleButton,
            PointerUpdateKind.XButton1Pressed => (ushort)MouseHookType.XButton1,
            PointerUpdateKind.XButton2Pressed => (ushort)MouseHookType.XButton2,
            _ => _selectedMouseButton
        };
        KeyName.Content = MouseButtonName(_selectedMouseButton);
        KeyName.IsVisible = true;
        e.Handled = true;
    }

    private static string MouseButtonName(ushort? button) => button switch
    {
        (ushort)MouseHookType.LeftButton => "鼠标左键",
        (ushort)MouseHookType.RightButton => "鼠标右键",
        (ushort)MouseHookType.MiddleButton => "鼠标中键",
        (ushort)MouseHookType.XButton1 => "鼠标侧键1",
        (ushort)MouseHookType.XButton2 => "鼠标侧键2",
        _ => "未设置"
    };

    private void KeyBoard_OnClick(object? sender, RoutedEventArgs e)
    {
        _type = HotKeyType.Keyboard;
        KeyName.Content = _selectedKey?.ToString() ?? "未设置";
        MouseCaptureArea.Focus();
    }

    private void Mouse_OnClick(object? sender, RoutedEventArgs e)
    {
        _type = HotKeyType.Mouse;
        Ctrl.IsVisible = Alt.IsVisible = Shift.IsVisible = Win.IsVisible = false;
        KeyName.Content = MouseButtonName(_selectedMouseButton);
        MouseCaptureArea.Focus();
    }

    private void ProcessScope_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ProcessListSection is not null) ProcessListSection.IsVisible = ProcessScope.SelectedIndex > 0;
    }
}
