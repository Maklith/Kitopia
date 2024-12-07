using System;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Win32.Input;
using CommunityToolkit.Mvvm.Messaging;
using Core.SDKs;
using Core.SDKs.HotKey;
using Core.SDKs.Services;
using Core.SDKs.Services.Config;
using log4net;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using Ursa.Controls;

namespace KitopiaAvalonia.Windows;

public partial class HotKeyEditorWindow : UrsaWindow
{
    private static readonly ILog Log = LogManager.GetLogger(nameof(HotKeyEditorWindow));
    private HotKeyModel? _hotKeyModel;
    private bool isFinnish;
    private EKey? selectedKey;
    private HotKeyType _type = HotKeyType.Keyboard;
    private ushort? selectedMouseButton;
    private bool setSuccess;


    internal HotKeyEditorWindow()
    {
        InitializeComponent();
        _hotKeyModel = new HotKeyModel()
        {
            IsSelectAlt = true,
            IsSelectShift = true,
            SelectKey = EKey.Shift,
            Name = "测试",
            MainName = "测试"
        };
        Name.Text = $"快捷键:{_hotKeyModel.Value.SignName}";
        if (_hotKeyModel.Value.IsSelectAlt) Alt.IsVisible = true;

        if (_hotKeyModel.Value.IsSelectCtrl) Ctrl.IsVisible = true;

        if (_hotKeyModel.Value.IsSelectShift) Shift.IsVisible = true;

        if (_hotKeyModel.Value.IsSelectWin) Win.IsVisible = true;

        selectedKey = _hotKeyModel.Value.SelectKey;
        KeyName.Content = _hotKeyModel.Value.SelectKey.ToString();
    }

    public HotKeyEditorWindow(HotKeyModel hotKeyModel)
    {
        InitializeComponent();
        _hotKeyModel = hotKeyModel;
        Name.Text = $"快捷键:{hotKeyModel.SignName}";
        _type = hotKeyModel.Type;
        switch (hotKeyModel.Type)
        {
            case HotKeyType.Keyboard:
            {
                KeyBoard.IsChecked = true;
                if (hotKeyModel.IsSelectAlt) Alt.IsVisible = true;

                if (hotKeyModel.IsSelectCtrl) Ctrl.IsVisible = true;

                if (hotKeyModel.IsSelectShift) Shift.IsVisible = true;

                if (hotKeyModel.IsSelectWin) Win.IsVisible = true;

                selectedKey = hotKeyModel.SelectKey;
                KeyName.Content = hotKeyModel.SelectKey.ToString();
                break;
            }
            case HotKeyType.Mouse:
            {
                Mouse.IsChecked = true;
                Slider.Value = hotKeyModel.PressTimeMillis;
                selectedMouseButton = hotKeyModel.MouseButton;
                KeyName.Content = hotKeyModel.MouseButton switch
                {
                    0 => "鼠标左键",
                    1 => "鼠标右键",
                    2 => "鼠标中键",
                    _ => $"鼠标按键{hotKeyModel.MouseButton}"
                };
                break;
            }
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private void HotKeyEditorWindow_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (!_hotKeyModel.HasValue) return;

        if (_type != HotKeyType.Keyboard) return;

        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
                Ctrl.IsVisible = true;
            else
                Ctrl.IsVisible = false;

            if (e.KeyModifiers.HasFlag(KeyModifiers.Alt))
                Alt.IsVisible = true;
            else
                Alt.IsVisible = false;

            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                Shift.IsVisible = true;
            else
                Shift.IsVisible = false;

            if (e.KeyModifiers.HasFlag(KeyModifiers.Meta))
                Win.IsVisible = true;
            else
                Win.IsVisible = false;

            if (e.Key == Key.System)
            {
                var key = e.PhysicalKey;
                if (key != PhysicalKey.ShiftLeft && key != PhysicalKey.ShiftRight && key != PhysicalKey.AltLeft &&
                    key != PhysicalKey.AltRight &&
                    key != PhysicalKey.MetaLeft && key != PhysicalKey.MetaRight && key != PhysicalKey.ControlLeft &&
                    key != PhysicalKey.ControlRight)
                {
                    selectedKey = (EKey)KeyInterop.VirtualKeyFromKey((Key)(int)key);
                    KeyName.IsVisible = true;
                    KeyName.Content = selectedKey.ToString();
                }
                else
                {
                    KeyName.IsVisible = false;
                    selectedKey = null;
                }
            }
            else
            {
                var key = e.Key;
                if (key != Key.LeftShift && key != Key.RightShift && key != Key.LeftAlt && key != Key.RightAlt &&
                    key != Key.LWin && key != Key.RWin && key != Key.LeftCtrl && key != Key.RightCtrl)
                {
                    selectedKey = (EKey)KeyInterop.VirtualKeyFromKey(key);
                    KeyName.IsVisible = true;
                    KeyName.Content = selectedKey.ToString();
                }
                else
                {
                    KeyName.IsVisible = false;
                    selectedKey = null;
                }
            }
        }
    }

    private void HotKeyEditorWindow_OnKeyUp(object sender, KeyEventArgs e)
    {
    }


    private void ButtonBase_OnClick(object sender, RoutedEventArgs e)
    {
        if (_type == HotKeyType.Keyboard && selectedKey is null) return;

        if (_type == HotKeyType.Mouse && selectedMouseButton is null) return;

        var hotKeyModel = new HotKeyModel(_hotKeyModel.Value.UUID)
        {
            MainName = _hotKeyModel.Value.MainName,
            Name = _hotKeyModel.Value.Name,
            IsSelectAlt = Alt.IsVisible,
            IsSelectWin = Win.IsVisible,
            IsSelectShift = Shift.IsVisible,
            IsSelectCtrl = Ctrl.IsVisible,
            SelectKey = selectedKey ?? EKey.未设置,
            MouseButton = selectedMouseButton,
            Type = _type
        };
        if (!HotKeyManager.HotKetImpl.Modify(hotKeyModel))
        {
            ServiceManager.Services.GetService<IContentDialog>().ShowDialogAsync(this, new DialogContent()
            {
                Title = $"快捷键{hotKeyModel.SignName}设置失败",
                Content = "请重新设置快捷键，按键与系统其他程序冲突",
                CloseButtonText = "关闭"
            });
        }
        else
        {
            ConfigManger.RequsetUpdateHotKey(hotKeyModel);
            ConfigManger.Save();

            isFinnish = true;
            WeakReferenceMessenger.Default.Send(_hotKeyModel.Value.UUID, "hotkey");
            Close();
        }
    }

    private void ButtonCancle_OnClick(object sender, RoutedEventArgs e)
    {
        isFinnish = true;
        setSuccess = true;
        Close();
    }

    private void InputElement_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!_hotKeyModel.HasValue) return;

        if (_type != HotKeyType.Mouse) return;

        ushort id = 0;
        var pointerPointProperties = e.GetCurrentPoint(this).Properties;
        if (pointerPointProperties.IsLeftButtonPressed) id = 0;

        if (pointerPointProperties.IsRightButtonPressed) id = 1;

        if (pointerPointProperties.IsMiddleButtonPressed) id = 2;

        if (pointerPointProperties.IsXButton1Pressed) id = 3;

        if (pointerPointProperties.IsXButton2Pressed) id = 4;

        selectedMouseButton = id;
        KeyName.IsVisible = true;
        KeyName.Content = id switch
        {
            0 => "鼠标左键",
            1 => "鼠标右键",
            2 => "鼠标中键",
            _ => $"鼠标按键{id}"
        };
    }

    private void KeyBoard_OnClick(object? sender, RoutedEventArgs e)
    {
        _type = HotKeyType.Keyboard;
        KeyName.IsVisible = false;
    }

    private void Mouse_OnClick(object? sender, RoutedEventArgs e)
    {
        _type = HotKeyType.Mouse;
        Ctrl.IsVisible = false;
        Alt.IsVisible = false;
        Shift.IsVisible = false;
        Win.IsVisible = false;
        KeyName.IsVisible = false;
    }
}