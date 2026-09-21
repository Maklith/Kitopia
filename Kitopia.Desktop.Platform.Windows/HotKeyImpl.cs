using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Messaging;
using Kitopia.Desktop.Features.Services;
using Kitopia.Desktop.Features.Services.Config;
using Kitopia.Desktop.Features.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using SharpHook;
using SharpHook.Data;
using Vanara.PInvoke;

namespace Kitopia.Desktop.Platform.Windows;

public class HotKeyImpl : IHotKetImpl
{
    private static Avalonia.Controls.Window _globalHotKeyWindow = null!;
    private static readonly ConcurrentDictionary<string, HotkeyInfo> HotKeys = new();
    private SimpleGlobalHook? _inputHook;
    private bool _wndProcHookAttached;
    private static readonly HashSet<ushort> PressedKeys = new();

    public class HotkeyInfo
    {
        public HotKeyModel HotKeyModel;
        // 正数为 Win32 热键 ID，0 为共享钩子处理的快捷键，-1 为停用。
        public int Id;
        public required Action<HotKeyModel> CallBack;
        public DispatcherTimer? Timer;
        public HWND PressWindow;
    }

    private static int _id;

    public void Init() {
        Dispatcher.UIThread.Invoke(() =>
        {
            _globalHotKeyWindow = new Avalonia.Controls.Window
            {
                Height = 1,
                Width = 1,
                
                WindowState= WindowState.Minimized,
                ShowInTaskbar = false
            };
            _globalHotKeyWindow.Show();
            _globalHotKeyWindow.Hide();
            
        });
    }

    public void StartHook()
    {
        if (!_wndProcHookAttached)
        {
            Win32Properties.AddWndProcHookCallback(_globalHotKeyWindow, OnWndProc);
            _wndProcHookAttached = true;
        }

        if (!ConfigManger.Config.mouseCapture)
        {
            foreach (var (_, hotkey) in HotKeys) hotkey.Timer?.Stop();
        }

        // SharpHook 在同一进程中只能运行一个监听器。
        if (_inputHook is not null) return;
        _inputHook = ServiceManager.Services.GetRequiredService<SimpleGlobalHook>();
        _inputHook.MousePressed += OnMousePressed;
        _inputHook.MouseReleased += OnMouseReleased;
        _inputHook.KeyPressed += OnKeyPressed;
        _inputHook.KeyReleased += OnKeyReleased;
        _ = _inputHook.RunAsync().ContinueWith(task =>
            LogManager.Logger.Error(task.Exception, "快捷键监听启动失败"),
            TaskContinuationOptions.OnlyOnFaulted);
    }

    private static void OnKeyPressed(object? sender, KeyboardHookEventArgs e)
    {
        var key = e.Data.RawCode;
        if (PressedKeys.Contains(key))
        {
            e.SuppressEvent = true;
            return;
        }

        var mask = e.RawEvent.Mask;
        foreach (var (_, hotkey) in HotKeys)
        {
            var model = hotkey.HotKeyModel;
            if (hotkey.Id != 0 || !model.IsEnabled || model.Type != HotKeyType.Keyboard ||
                (int)model.SelectKey != key ||
                model.IsSelectCtrl != ((mask & EventMask.Ctrl) != 0) ||
                model.IsSelectAlt != ((mask & EventMask.Alt) != 0) ||
                model.IsSelectShift != ((mask & EventMask.Shift) != 0) ||
                model.IsSelectWin != ((mask & EventMask.Meta) != 0)) continue;
            var foreground = User32.GetForegroundWindow();
            if (!CanExecuteInWindow(model, foreground)) continue;
            PressedKeys.Add(key);
            e.SuppressEvent = true;
            Dispatcher.UIThread.Post(() =>
            {
                if (hotkey.Id == 0 && model.IsEnabled && User32.GetForegroundWindow() == foreground &&
                    CanExecuteInWindow(model, foreground)) hotkey.CallBack(model);
            });
            return;
        }
    }

    private static void OnKeyReleased(object? sender, KeyboardHookEventArgs e)
    {
        if (PressedKeys.Remove(e.Data.RawCode)) e.SuppressEvent = true;
    }

    private static bool CanExecuteInWindow(HotKeyModel model, HWND window)
    {
        if (model.ProcessScope != HotKeyProcessScope.All)
        {
            User32.GetWindowThreadProcessId(window, out var processId);
            try
            {
                using var process = Process.GetProcessById((int)processId);
                if (!model.CanExecuteInProcess(process.ProcessName)) return false;
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or Win32Exception)
            {
                return false;
            }
        }
        if (model.IgnoreTextInput)
        {
            var info = new User32.GUITHREADINFO { cbSize = (uint)Marshal.SizeOf<User32.GUITHREADINFO>() };
            if (!User32.GetGUIThreadInfo(0, ref info)) return false;
            var name = new StringBuilder(128);
            User32.GetClassName(info.hwndFocus, name, name.Capacity);
            if (name.ToString().Contains("Edit", StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }

    private static void OnMousePressed(object? sender, MouseHookEventArgs e)
    {
        var dataButton = (ushort)e.Data.Button;
        if (dataButton == 0) return;
        var foreground = User32.GetForegroundWindow();

        Dispatcher.UIThread.Post(() =>
        {
            if (!ConfigManger.Config.mouseCapture || User32.GetForegroundWindow() != foreground) return;
            foreach (var (_, value) in HotKeys)
            {
                if (value.HotKeyModel.Type != HotKeyType.Mouse || value.Id == -1 ||
                    !value.HotKeyModel.IsEnabled || value.HotKeyModel.MouseButton != dataButton) continue;

                if (!CanExecuteInWindow(value.HotKeyModel, foreground)) continue;
                value.PressWindow = foreground;

                if (value.Timer is null)
                {
                    var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(value.HotKeyModel.PressTimeMillis) };
                    timer.Tick += (_, _) =>
                    {
                        timer.Stop();
                        if (value.Id != -1 && value.HotKeyModel.IsEnabled && ConfigManger.Config.mouseCapture &&
                            User32.GetForegroundWindow() == value.PressWindow &&
                            CanExecuteInWindow(value.HotKeyModel, value.PressWindow))
                            value.CallBack.Invoke(value.HotKeyModel);
                    };
                    value.Timer = timer;
                }

                value.Timer.Start();
            }
        });
    }

    private static void OnMouseReleased(object? sender, MouseHookEventArgs e)
    {
        var dataButton = (ushort)e.Data.Button;
        if (dataButton == 0) return;

        Dispatcher.UIThread.Post(() =>
        {
            foreach (var (_, value) in HotKeys)
            {
                if (value.HotKeyModel.Type == HotKeyType.Mouse && value.HotKeyModel.MouseButton == dataButton)
                    value.Timer?.Stop();
            }
        });
    }

    private static IntPtr OnWndProc(IntPtr hwnd, uint msg, IntPtr wparam, IntPtr lparam, ref bool handled)
    {
        if (msg == (uint)User32.WindowMessage.WM_HOTKEY)
        {
            var int32 = wparam.ToInt32();
            var hotkey = HotKeys.FirstOrDefault(entry => entry.Value.Id == int32 && entry.Value.HotKeyModel.IsEnabled).Value;
            if (hotkey is not null && CanExecuteInWindow(hotkey.HotKeyModel, User32.GetForegroundWindow()))
                hotkey.CallBack.Invoke(hotkey.HotKeyModel);
        }

        return IntPtr.Zero;
    }

    
    public bool Register(HotKeyModel hotKeyModel, Action<HotKeyModel> rallBack,bool initHotKey=true)
    {
        if (!hotKeyModel.IsEnabled)//没有激活直接返回注册完成
        {
            HotKeys[hotKeyModel.UUID] = new HotkeyInfo
            {
                HotKeyModel = hotKeyModel,
                Id = -1,
                CallBack = rallBack
            };
            return true;
        }

        switch (hotKeyModel.Type)
        {
            case HotKeyType.Keyboard:
            {
                if (hotKeyModel.ProcessScope != HotKeyProcessScope.All || hotKeyModel.IgnoreTextInput)
                {
                    var enabled = initHotKey && hotKeyModel.SelectKey is not (EKey.未设置 or 0);
                    hotKeyModel.IsEnabled = enabled;
                    HotKeys[hotKeyModel.UUID] = new HotkeyInfo
                    {
                        HotKeyModel = hotKeyModel, Id = enabled ? 0 : -1, CallBack = rallBack
                    };
                    return enabled;
                }
                User32.HotKeyModifiers hotkeyModifiers = 0;
                if (hotKeyModel.IsSelectAlt) hotkeyModifiers |= User32.HotKeyModifiers.MOD_ALT;

                if (hotKeyModel.IsSelectCtrl) hotkeyModifiers |= User32.HotKeyModifiers.MOD_CONTROL;

                if (hotKeyModel.IsSelectShift) hotkeyModifiers |= User32.HotKeyModifiers.MOD_SHIFT;

                if (hotKeyModel.IsSelectWin) hotkeyModifiers |= User32.HotKeyModifiers.MOD_WIN;

                _id++;
                var registerHotKey = false;
                if (initHotKey)
                {
                    Dispatcher.UIThread.Invoke(() =>
                    {
                        registerHotKey = User32.RegisterHotKey(_globalHotKeyWindow.TryGetPlatformHandle()!.Handle, _id,
                            hotkeyModifiers,
                            (uint)hotKeyModel.SelectKey);
                    });
                }
              
                if (registerHotKey)
                {
                    if (HotKeys.TryGetValue(hotKeyModel.UUID, out var hotKeyModel1) && hotKeyModel1.Id == -1)
                    {
                        hotKeyModel1.Id = _id;
                        hotKeyModel1.HotKeyModel = hotKeyModel;
                    }
                    else
                    {
                        HotKeys.TryAdd(hotKeyModel.UUID, new HotkeyInfo
                        {
                            HotKeyModel = hotKeyModel,
                            Id = _id,
                            CallBack = rallBack
                        });
                    }
                }
                else
                {
                    hotKeyModel.IsEnabled = false;
                    HotKeys[hotKeyModel.UUID] = new HotkeyInfo
                    {
                        HotKeyModel = hotKeyModel,
                        Id = -1,
                        CallBack = rallBack
                    };
                }

                return registerHotKey;
            }
            case HotKeyType.Mouse:
            {
                hotKeyModel.IsEnabled = true;
                if (HotKeys.TryGetValue(hotKeyModel.UUID, out var hotKeyModel1) && hotKeyModel1.Id == -1)
                {
                    hotKeyModel1.Id = 0;
                    hotKeyModel1.HotKeyModel = hotKeyModel;
                }
                else
                {
                    HotKeys.TryAdd(hotKeyModel.UUID, new HotkeyInfo
                    {
                        HotKeyModel = hotKeyModel,
                        Id = 0,
                        CallBack = rallBack
                    });
                }

                return true;
            }
        }
        hotKeyModel.IsEnabled = false;
        return false;
    }

    public bool UnRegister(HotKeyModel hotKeyModel)
    {
        return UnRegister(hotKeyModel.UUID);
    }

    public bool UnRegister(string uuid)
    {
        if (HotKeys.TryGetValue(uuid, out var hotkey))
        {
            var unregistered = hotkey.Id <= 0 || Dispatcher.UIThread.Invoke(() =>
                User32.UnregisterHotKey(_globalHotKeyWindow.TryGetPlatformHandle()!.Handle, hotkey.Id));
            if (!unregistered) return false;

            hotkey.Id = -1;
            hotkey.Timer?.Stop();
            hotkey.Timer = null;
            hotkey.HotKeyModel.IsEnabled = false;
            ConfigManger.RequsetUpdateHotKey(hotkey.HotKeyModel);
            ConfigManger.Save();
            WeakReferenceMessenger.Default.Send(hotkey.HotKeyModel.UUID, "hotkey");
            return true;
        }

        return false;
    }

    public bool Remove(string uuid) {
        if (UnRegister(uuid))
        {
            HotKeys.TryRemove(uuid, out _);
            WeakReferenceMessenger.Default.Send("", "hotkey");
            return true;
        }

        return false;
    }

    public bool RequestUserModify(string uuid)
    {
        if (HotKeys.ContainsKey(uuid))
        {
            ServiceManager.Services.GetService<IHotKeyEditor>()?.EditByUuid(uuid, null);
            return true;
        }

        return false;
    }

    public bool Modify(HotKeyModel hotKeyModel)
    {
        if (HotKeys.ContainsKey(hotKeyModel.UUID))
        {
            var rallback = HotKeys[hotKeyModel.UUID].CallBack;
            var enabled = hotKeyModel.IsEnabled;
            if (!UnRegister(hotKeyModel.UUID)) return false;
            hotKeyModel.IsEnabled = enabled;
            var registered = Register(hotKeyModel, rallback);
            ConfigManger.RequsetUpdateHotKey(hotKeyModel);
            ConfigManger.Save();
            WeakReferenceMessenger.Default.Send(hotKeyModel.UUID, "hotkey");
            return registered;
        }

        return false;
    }

    public HotKeyModel? GetByUuid(string uuid)
    {
        if (HotKeys.ContainsKey(uuid)) return HotKeys[uuid].HotKeyModel;

        return null;
    }

    public bool IsActive(string uuid)
    {
        if (HotKeys.TryGetValue(uuid, out var hotkeyInfo))
            return hotkeyInfo.HotKeyModel.IsEnabled;

        return false;
    }

    public IEnumerable<HotKeyModel> GetAllRegistered()
    {
        return HotKeys.Select(entry => entry.Value.HotKeyModel);
    }
}
