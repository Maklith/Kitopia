using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Threading;
using Kitopia.Desktop.Features.Services;
using Kitopia.Desktop.Features.Search.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using Serilog;
using SharpHook;
using SharpHook.Data;
using Vanara.PInvoke;
using MouseQuickWindowViewModel = Kitopia.Desktop.Features.Search.ViewModels.MouseQuickWindowViewModel;

namespace Kitopia.Desktop.Windows;

public partial class MouseQuickWindow : Window
{
    private static readonly ILogger Logger = LogManager.Logger.ForContext<MouseQuickWindow>();

    public MouseQuickWindow()
    {
        InitializeComponent();
    }

    private void WindowBase_OnDeactivated(object? sender, EventArgs e)
    {
        if (IsVisible) Close();
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        User32.GetCursorPos(out var pos);
        var hmonitor = User32.MonitorFromPoint(pos, User32.MonitorFlags.MONITOR_DEFAULTTOPRIMARY);
        var monitorInfo = new User32.MONITORINFO();
        monitorInfo.cbSize = 40;
        User32.GetMonitorInfo(hmonitor, ref monitorInfo);
        var windowinfo = new User32.WINDOWINFO();
        windowinfo.cbSize = (uint)Marshal.SizeOf(windowinfo);
        User32.GetWindowInfo(TryGetPlatformHandle().Handle, ref windowinfo);

        int Left, Top;
        if (pos.X + windowinfo.rcClient.Width < monitorInfo.rcMonitor.Right)
            Left = pos.X;
        else
            Left = pos.X - windowinfo.rcClient.Width;


        if (pos.Y + windowinfo.rcClient.Height < monitorInfo.rcMonitor.Bottom)
            Top = pos.Y;
        else
            Top = pos.Y - windowinfo.rcClient.Height;

        Position = new PixelPoint(Left, Top);

        var clipboardService = ServiceManager.Services.GetService<IClipboardService>();
        string? text = clipboardService?.GetText();


        var eventSimulator = new EventSimulator();
        eventSimulator.SimulateKeyPress(KeyCode.VcLeftControl);
        eventSimulator.SimulateKeyPress(KeyCode.VcC);
        Task.Delay(100).GetAwaiter().GetResult();
        eventSimulator.SimulateKeyRelease(KeyCode.VcC);
        eventSimulator.SimulateKeyRelease(KeyCode.VcLeftControl);

        Dispatcher.UIThread.InvokeAsync(() =>
        {
            Task.Delay(800);
            var s = clipboardService?.GetText();
            if (s != text)
            {
                ((MouseQuickWindowViewModel)DataContext).SelectedItem = new SelectedItem
                    { type = FileType.文本, obj = s };

                Logger.Information(s);
            }

            if (text != null) clipboardService?.SetText(text);
        });


        User32.SetForegroundWindow(TryGetPlatformHandle().Handle);
    }
}
