using System;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Win32.Input;
using CommunityToolkit.Mvvm.Messaging;
using Kitopia.Desktop.Features.Services.Config;
using Kitopia.Desktop.Features.Services.HotKey;
using Kitopia.Desktop.Features.Services.Interfaces;
using Kitopia.Desktop.Features.Utils;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using Ursa.Controls;

namespace Kitopia.Desktop.Windows;

public partial class HotKeyEditorWindow : UrsaWindow
{
    private readonly HotKeyModel? _hotKeyModel;
    private HotKeyType _type;
    private EKey? _selectedKey;
    private ushort? _selectedMouseButton;


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

                _selectedKey = hotKeyModel.SelectKey;
                KeyName.Content = hotKeyModel.SelectKey.ToString();
                break;
            }
            case HotKeyType.Mouse:
            {
                Mouse.IsChecked = true;
                Slider.Value = hotKeyModel.PressTimeMillis;
                _selectedMouseButton = hotKeyModel.MouseButton;
                KeyName.Content = hotKeyModel.MouseButton switch
                {
                    (int)MouseHookType.LeftButton => "鼠标左键",
                    (int)MouseHookType.RightButton => "鼠标右键",
                    (int)MouseHookType.MiddleButton => "鼠标中键",
                    (int)MouseHookType.XButton1 => "鼠标侧键1",
                    (int)MouseHookType.XButton2 => "鼠标侧键2",
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
        if (_hotKeyModel == null) return;

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
                    _selectedKey = (EKey)KeyInterop.VirtualKeyFromKey((Key)(int)key);
                    KeyName.IsVisible = true;
                    KeyName.Content = _selectedKey.ToString();
                }
                else
                {
                    KeyName.IsVisible = false;
                    _selectedKey = null;
                }
            }
            else
            {
                var key = e.Key;
                if (key != Key.LeftShift && key != Key.RightShift && key != Key.LeftAlt && key != Key.RightAlt &&
                    key != Key.LWin && key != Key.RWin && key != Key.LeftCtrl && key != Key.RightCtrl)
                {
                    _selectedKey = (EKey)KeyInterop.VirtualKeyFromKey(key);
                    KeyName.IsVisible = true;
                    KeyName.Content = _selectedKey.ToString();
                }
                else
                {
                    KeyName.IsVisible = false;
                    _selectedKey = null;
                }
            }
        }
    }

    private void HotKeyEditorWindow_OnKeyUp(object sender, KeyEventArgs e)
    {
    }


    private void ButtonBase_OnClick(object sender, RoutedEventArgs e)
    {
        if (_type == HotKeyType.Keyboard && _selectedKey is null) return;

        if (_type == HotKeyType.Mouse && _selectedMouseButton is null) return;

        if (_hotKeyModel is null) return; 
        
        _hotKeyModel.IsSelectAlt = Alt.IsVisible;
        _hotKeyModel.IsSelectWin = Win.IsVisible;
        _hotKeyModel.IsSelectShift = Shift.IsVisible;
        _hotKeyModel.IsSelectCtrl = Ctrl.IsVisible;
        _hotKeyModel.SelectKey = _selectedKey ?? EKey.未设置;
        _hotKeyModel.MouseButton = _selectedMouseButton;
        _hotKeyModel.Type = _hotKeyModel.Type;
        
        if (!ServiceManager.Services.GetService<IHotKetImpl>()!.Modify(_hotKeyModel))
        {
            ServiceManager.Services.GetService<IToastService>()!.Show(new DialogContent
            {
                Title = $"快捷键{_hotKeyModel.Name}设置失败",
                Content = "请重新设置快捷键，按键与系统其他程序冲突",
                CloseButtonText = "关闭"
            }.ToToastRequest(),ServiceManager.Services.GetService<IWindowTool>()!.GetForegroundWindow());
        }
        else
        {
            ConfigManger.RequsetUpdateHotKey(_hotKeyModel);
            ConfigManger.Save();
            
            WeakReferenceMessenger.Default.Send(_hotKeyModel.UUID, "hotkey");//用于情景保存
            Close();
        }
    }

    private void ButtonCancle_OnClick(object sender, RoutedEventArgs e)
    {
      
        Close();
    }

    private void InputElement_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_hotKeyModel == null) return;

        if (_type != HotKeyType.Mouse) return;

        ushort id = 1;
        var pointerPointProperties = e.GetCurrentPoint(this).Properties;
        if (pointerPointProperties.IsLeftButtonPressed) id = (int)MouseHookType.LeftButton;

        if (pointerPointProperties.IsRightButtonPressed) id = (int)MouseHookType.RightButton;

        if (pointerPointProperties.IsMiddleButtonPressed) id = (int)MouseHookType.MiddleButton;

        if (pointerPointProperties.IsXButton1Pressed) id = (int)MouseHookType.XButton1;

        if (pointerPointProperties.IsXButton2Pressed) id = (int)MouseHookType.XButton2;


        _selectedMouseButton = id;
        KeyName.IsVisible = true;
        KeyName.Content = id switch
        {
            (int)MouseHookType.LeftButton => "鼠标左键",
            (int)MouseHookType.RightButton => "鼠标右键",
            (int)MouseHookType.MiddleButton => "鼠标中键",
            (int)MouseHookType.XButton1 => "鼠标侧键1",
            (int)MouseHookType.XButton2 => "鼠标侧键2",
            _ => throw new ArgumentOutOfRangeException()
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
