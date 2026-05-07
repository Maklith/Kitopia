using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Messaging;
using Core.Services.Config;
using Core.Services.Interfaces;
using Core.Utils;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using SharpHook;
using SharpHook.Reactive;
using Vanara.PInvoke;
using Timer = System.Timers.Timer;

namespace Core.Window;

public class HotKeyImpl : IHotKetImpl
{
    private static Avalonia.Controls.Window _globalHotKeyWindow = null!;
    private static ObservableDictionary<string, HotkeyInfo> HotKeys { get; set; }= new();
    private static readonly SimpleReactiveGlobalHook Hook = new(GlobalHookType.Mouse);

    public class HotkeyInfo
    {
        public HotKeyModel HotKeyModel;
        public int Id;
        public required Action<HotKeyModel> CallBack;
        public Timer? Timer;
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
            Win32Properties.AddWndProcHookCallback(_globalHotKeyWindow, OnWndProc);
        });
    }

    public void StartHook()
    {
        Hook.MousePressed.Subscribe(OnMousePressed);
        Hook.MouseReleased.Subscribe(OnMouseReleased);
        Hook.RunAsync();
    }

    private static void OnMousePressed(MouseHookEventArgs e)
    {
        foreach (var (_, value) in HotKeys)
        {
            if (value.HotKeyModel.Type != HotKeyType.Mouse) continue;
            if (value.Id == -1) continue;
            var dataButton = (int)e.Data.Button;
            if (dataButton == 0) continue;

            if (value.HotKeyModel.MouseButton == dataButton - 1)
            {
                if (value.Timer is null)
                {
                    value.Timer = new Timer(value.HotKeyModel.PressTimeMillis)
                    {
                        AutoReset = false
                    };
                    value.Timer.Elapsed += (_, _) =>
                    {
                        ThreadPool.QueueUserWorkItem(_ => { value.CallBack.Invoke(value.HotKeyModel); });
                    };
                }

                value.Timer.Start();
            }
        }
    }

    private static void OnMouseReleased(MouseHookEventArgs e)
    {
        foreach (var (_, value) in HotKeys)
        {
            if (value.HotKeyModel.Type != HotKeyType.Mouse) continue;
            if (value.Id == -1) continue;
            var dataButton = (int)e.Data.Button;
            if (dataButton == 0) continue;

            if (value.HotKeyModel.MouseButton == dataButton - 1) value.Timer?.Stop();
        }
    }

    private static IntPtr OnWndProc(IntPtr hwnd, uint msg, IntPtr wparam, IntPtr lparam, ref bool handled)
    {
        if (msg == (uint)User32.WindowMessage.WM_HOTKEY)
        {
            var int32 = wparam.ToInt32();
            var keyValuePair = HotKeys.First(e => e.Value.Id == int32);
            keyValuePair.Value.CallBack.Invoke(keyValuePair.Value.HotKeyModel);
        }

        return IntPtr.Zero;
    }

    
    public bool Register(HotKeyModel hotKeyModel, Action<HotKeyModel> rallBack,bool initHotKey=true)
    {
        if (!hotKeyModel.IsEnabled)
        {
            HotKeys.Add(hotKeyModel.UUID, new HotkeyInfo
            {
                HotKeyModel = hotKeyModel,
                Id = -1,
                CallBack = rallBack
            });
            return true;
        }

        switch (hotKeyModel.Type)
        {
            case HotKeyType.Keyboard:
            {
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
                        HotKeys.Add(hotKeyModel.UUID, new HotkeyInfo
                        {
                            HotKeyModel = hotKeyModel,
                            Id = _id,
                            CallBack = rallBack
                        });
                    }
                }
                else
                {
                    HotKeys.Add(hotKeyModel.UUID, new HotkeyInfo
                    {
                        HotKeyModel = hotKeyModel,
                        Id = -1,
                        CallBack = rallBack
                    });
                }

                return registerHotKey;
            }
            case HotKeyType.Mouse:
            {
                hotKeyModel.IsEnabled = true;
                if (HotKeys.TryGetValue(hotKeyModel.UUID, out var hotKeyModel1) && hotKeyModel1.Id == -1)
                {
                    hotKeyModel1.Id = 1;
                    hotKeyModel1.HotKeyModel = hotKeyModel;
                }
                else
                {
                    HotKeys.Add(hotKeyModel.UUID, new HotkeyInfo
                    {
                        HotKeyModel = hotKeyModel,
                        Id = 1,
                        CallBack = rallBack
                    });
                }

                return true;
            }
        }

        return false;
    }

    public bool Del(HotKeyModel hotKeyModel)
    {
        return UnRegister(hotKeyModel.UUID);
    }

    public bool UnRegister(string uuid)
    {
        if (HotKeys.TryGetValue(uuid, out var hotkey))
        {
            hotkey.HotKeyModel.IsEnabled = false;
            WeakReferenceMessenger.Default.Send(hotkey.HotKeyModel.UUID, "hotkey");
            ConfigManger.RequsetUpdateHotKey(hotkey.HotKeyModel);
            ConfigManger.Save();
            switch (hotkey.HotKeyModel.Type)
            {
                case HotKeyType.Keyboard:
                {
                    var unregisterHotKey =
                        User32.UnregisterHotKey(_globalHotKeyWindow.TryGetPlatformHandle()!.Handle, hotkey.Id);
                    HotKeys[uuid].Id = -1;
                    return unregisterHotKey;
                }
                case HotKeyType.Mouse:
                {
                    hotkey.Timer?.Stop();
                    hotkey.Id = -1;
                    break;
                }
            }
        }

        return false;
    }

    public bool Remove(string uuid) {
        if (UnRegister(uuid))
        {
            HotKeys.Remove(uuid);
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
            Del(hotKeyModel);
            hotKeyModel.IsEnabled = true;
            ConfigManger.RequsetUpdateHotKey(hotKeyModel);
            ConfigManger.Save();
            if (!Register(hotKeyModel, rallback)) return false;

            return true;
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
        return HotKeys.Values.Select(x => x.HotKeyModel);
    }
}