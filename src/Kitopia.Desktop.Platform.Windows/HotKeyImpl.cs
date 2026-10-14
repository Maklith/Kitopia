using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Messaging;
using Kitopia.Desktop.Features.Services;
using Kitopia.Desktop.Features.Services.Config;
using Kitopia.Desktop.Features.Services.Interfaces;
using Kitopia.Desktop.Features.Services.HotKey;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using Serilog;
using SharpHook;
using SharpHook.Data;
using Vanara.PInvoke;

namespace Kitopia.Desktop.Platform.Windows;

public class HotKeyImpl : IHotKetImpl
{
    private static readonly ILogger Logger = LogManager.Logger.ForContext<HotKeyImpl>();
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
        public PixelPoint? PressPosition;
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
            foreach (var (_, hotkey) in HotKeys)
            {
                hotkey.Timer?.Stop();
                hotkey.PressPosition = null;
            }
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
                    CanExecuteInWindow(model, foreground)) InvokeHotkey(hotkey);
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
        var position = new PixelPoint(e.Data.X, e.Data.Y);

        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                if (!ConfigManger.Config.mouseCapture || User32.GetForegroundWindow() != foreground) return;
                foreach (var (_, value) in HotKeys)
                {
                    if (value.HotKeyModel.Type != HotKeyType.Mouse || value.Id == -1 ||
                        !value.HotKeyModel.IsEnabled || value.HotKeyModel.MouseButton != dataButton) continue;

                    if (!CanExecuteInWindow(value.HotKeyModel, foreground)) continue;
                    value.PressWindow = foreground;
                    value.PressPosition = position;
                    value.HotKeyModel.TriggerPosition = null;
                    if (value.HotKeyModel.MouseTrigger == MouseHotKeyTrigger.DragRelease) continue;

                    if (value.Timer is null)
                    {
                        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(value.HotKeyModel.PressTimeMillis) };
                        timer.Tick += (_, _) =>
                        {
                            timer.Stop();
                            try
                            {
                                if (value.Id != -1 && value.HotKeyModel.IsEnabled && ConfigManger.Config.mouseCapture &&
                                    User32.GetForegroundWindow() == value.PressWindow &&
                                    CanExecuteInWindow(value.HotKeyModel, value.PressWindow))
                                    InvokeHotkey(value);
                            }
                            catch (Exception exception)
                            {
                                LogManager.Logger.Warning(exception, "执行鼠标快捷键失败");
                            }
                        };
                        value.Timer = timer;
                    }

                    value.Timer.Start();
                }
            }
            catch (Exception exception)
            {
                LogManager.Logger.Warning(exception, "处理鼠标快捷键按下事件失败");
            }
        });
    }

    private static void OnMouseReleased(object? sender, MouseHookEventArgs e)
    {
        var dataButton = (ushort)e.Data.Button;
        if (dataButton == 0) return;
        var foreground = User32.GetForegroundWindow();
        var position = new PixelPoint(e.Data.X, e.Data.Y);

        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                foreach (var (_, value) in HotKeys)
                {
                    var model = value.HotKeyModel;
                    if (model.Type != HotKeyType.Mouse || model.MouseButton != dataButton) continue;
                    value.Timer?.Stop();
                    var pressPosition = value.PressPosition;
                    value.PressPosition = null;
                    if (model.MouseTrigger != MouseHotKeyTrigger.DragRelease || pressPosition is not { } start ||
                        value.Id != 0 || !model.IsEnabled || !ConfigManger.Config.mouseCapture ||
                        (nint)foreground == 0 || foreground != value.PressWindow ||
                        User32.GetForegroundWindow() != foreground || !CanExecuteInWindow(model, foreground)) continue;

                    var deltaX = (long)position.X - start.X;
                    var deltaY = (long)position.Y - start.Y;
                    if (deltaX * deltaX + deltaY * deltaY < (long)model.DragDistancePixels * model.DragDistancePixels)
                        continue;

                    model.TriggerPosition = position;
                    InvokeHotkey(value);
                }
            }
            catch (Exception exception)
            {
                LogManager.Logger.Warning(exception, "处理鼠标快捷键释放事件失败");
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
                InvokeHotkey(hotkey);
        }

        return IntPtr.Zero;
    }

    private static void InvokeHotkey(HotkeyInfo hotkey)
    {
        var model = hotkey.HotKeyModel;
        Logger.Information("快捷键触发：{HotKeyName}，UUID：{HotKeyUuid}，输入类型：{HotKeyType}，触发方式：{Trigger}",
            model.SignName, model.UUID, model.Type,
            model.Type == HotKeyType.Mouse ? model.MouseTrigger.ToString() : "KeyPress");
        hotkey.CallBack(model);
    }

    
    public bool Register(HotKeyModel model, Action<HotKeyModel> callback, bool initHotKey = true)
    {
        if (!Dispatcher.UIThread.CheckAccess())
            return Dispatcher.UIThread.Invoke(() => Register(model, callback, initHotKey));

        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(callback);
        if (HotKeys.TryGetValue(model.UUID, out var existing))
        {
            if (!Modify(model)) return false;
            existing.CallBack = callback;
            return true;
        }

        model.IsEnabled &= initHotKey;
        var registered = TryRegister(model, out var id);
        if (!registered) model.IsEnabled = false;
        HotKeys[model.UUID] = new HotkeyInfo { HotKeyModel = model, Id = id, CallBack = callback };
        WeakReferenceMessenger.Default.Send(new HotKeyChanged(model.UUID, HotKeyChangeKind.Added));
        return registered;
    }

    private static bool TryRegister(HotKeyModel model, out int id)
    {
        id = -1;
        if (!model.IsEnabled) return true;
        if (model.Type == HotKeyType.Mouse)
        {
            if (model.MouseButton is null or 0 or ushort.MaxValue) return false;
            if (model.MouseTrigger == MouseHotKeyTrigger.DragRelease && model.DragDistancePixels == 0) return false;
            id = 0;
            return true;
        }
        if (model.SelectKey is EKey.未设置 or 0) return false;
        if (model.ProcessScope != HotKeyProcessScope.All || model.IgnoreTextInput)
        {
            id = 0;
            return true;
        }

        User32.HotKeyModifiers modifiers = 0;
        if (model.IsSelectAlt) modifiers |= User32.HotKeyModifiers.MOD_ALT;
        if (model.IsSelectCtrl) modifiers |= User32.HotKeyModifiers.MOD_CONTROL;
        if (model.IsSelectShift) modifiers |= User32.HotKeyModifiers.MOD_SHIFT;
        if (model.IsSelectWin) modifiers |= User32.HotKeyModifiers.MOD_WIN;
        var nextId = ++_id;
        if (_globalHotKeyWindow?.TryGetPlatformHandle() is not { } handle ||
            !User32.RegisterHotKey(handle.Handle, nextId, modifiers, (uint)model.SelectKey)) return false;
        id = nextId;
        return true;
    }

    private static bool StopRegistration(HotkeyInfo hotkey)
    {
        if (hotkey.Id > 0 && !User32.UnregisterHotKey(
                _globalHotKeyWindow.TryGetPlatformHandle()!.Handle, hotkey.Id)) return false;
        hotkey.Id = -1;
        hotkey.Timer?.Stop();
        hotkey.Timer = null;
        hotkey.PressPosition = null;
        hotkey.HotKeyModel.TriggerPosition = null;
        return true;
    }

    public bool UnRegister(HotKeyModel model) => UnRegister(model.UUID);

    public bool UnRegister(string uuid)
    {
        if (!Dispatcher.UIThread.CheckAccess())
            return Dispatcher.UIThread.Invoke(() => UnRegister(uuid));
        if (!HotKeys.TryGetValue(uuid, out var hotkey) || !StopRegistration(hotkey)) return false;
        hotkey.HotKeyModel.IsEnabled = false;
        ConfigManger.SaveHotKey(hotkey.HotKeyModel);
        WeakReferenceMessenger.Default.Send(new HotKeyChanged(uuid, HotKeyChangeKind.Updated));
        return true;
    }

    public bool Remove(string uuid)
    {
        if (!Dispatcher.UIThread.CheckAccess())
            return Dispatcher.UIThread.Invoke(() => Remove(uuid));
        if (string.IsNullOrEmpty(uuid) || !HotKeys.TryGetValue(uuid, out var hotkey) ||
            !StopRegistration(hotkey)) return false;
        // Removing a registration (e.g. unloading a plugin) must retain its configured enabled intent.
        HotKeys.TryRemove(uuid, out _);
        WeakReferenceMessenger.Default.Send(new HotKeyChanged(uuid, HotKeyChangeKind.Removed));
        return true;
    }

    public bool RequestUserModify(string uuid)
    {
        if (!Dispatcher.UIThread.CheckAccess())
            return Dispatcher.UIThread.Invoke(() => RequestUserModify(uuid));
        if (!HotKeys.ContainsKey(uuid)) return false;
        ServiceManager.Services.GetService<IHotKeyEditor>()?.EditByUuid(uuid, null);
        return true;
    }

    public bool Modify(HotKeyModel candidate)
    {
        if (!Dispatcher.UIThread.CheckAccess())
            return Dispatcher.UIThread.Invoke(() => Modify(candidate));
        if (!HotKeys.TryGetValue(candidate.UUID, out var hotkey)) return false;
        if (!StopRegistration(hotkey)) return false;
        if (!TryRegister(candidate, out var id))
        {
            if (!TryRegister(hotkey.HotKeyModel, out hotkey.Id))
            {
                hotkey.HotKeyModel.IsEnabled = false;
                ConfigManger.SaveHotKey(hotkey.HotKeyModel);
                WeakReferenceMessenger.Default.Send(new HotKeyChanged(candidate.UUID, HotKeyChangeKind.Updated));
            }
            return false;
        }

        hotkey.Id = id;
        hotkey.HotKeyModel.ApplySettings(candidate);
        ConfigManger.SaveHotKey(hotkey.HotKeyModel);
        WeakReferenceMessenger.Default.Send(new HotKeyChanged(candidate.UUID, HotKeyChangeKind.Updated));
        return true;
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
