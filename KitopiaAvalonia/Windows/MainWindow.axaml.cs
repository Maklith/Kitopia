using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Core.SDKs.CustomScenario;
using Core.SDKs.Services;
using Core.SDKs.Services.Config;
using Core.SDKs.Services.MQTT;
using Core.SDKs.Services.Plugin;
using Core.ViewModel;
using log4net;
using Ursa.Controls;
using HotKeyManager = Core.SDKs.HotKey.HotKeyManager;

namespace KitopiaAvalonia;

public partial class MainWindow : UrsaWindow
{
    private static readonly ILog log = LogManager.GetLogger(nameof(MainWindow));

    public MainWindow()
    {
        InitializeComponent();

        Dispatcher.UIThread.UnhandledException += (sender, e) =>
        {
            e.Handled = true;
            log.Fatal(e.Exception);
        };
        Opened += FirstOpenEventHandler;

        IsVisible = false;
    }


    private void Window_OnClosing(object? sender, WindowClosingEventArgs e)
    {
        IsVisible = false;
        e.Cancel = true;
    }


    private void FirstOpenEventHandler(object? o, EventArgs args)
    {
        Dispatcher.UIThread.InvokeAsync(() => { IsVisible = false; });
        Opened -= FirstOpenEventHandler;
    }


    private void TitleBarHost_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        BeginMoveDrag(e);
    }
}