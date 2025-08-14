using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Core.Services;
using Serilog;
using Ursa.Controls;

namespace KitopiaAvalonia.Windows;

public partial class MainWindow : UrsaWindow
{
    private static ILogger Log = LogManager.Logger.ForContext<MainWindow>();

    public MainWindow()
    {
        InitializeComponent();

        Dispatcher.UIThread.UnhandledException += (sender, e) =>
        {
            e.Handled = true;
            Log.Fatal(e.Exception, "");
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