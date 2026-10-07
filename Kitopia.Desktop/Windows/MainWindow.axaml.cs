using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Kitopia.Desktop.Features.Services;
using Kitopia.Feature.Avalonia.DeviceCommunication.ViewModels;
using Kitopia.Feature.Avalonia.DeviceCommunication.Views;
using Serilog;
using Ursa.Controls;

namespace Kitopia.Desktop.Windows;

public partial class MainWindow : UrsaWindow
{
    private static readonly ILogger Logger = LogManager.Logger.ForContext<MainWindow>();

    public MainWindow()
    {
        InitializeComponent();

        Dispatcher.UIThread.UnhandledException += (sender, e) =>
        {
            e.Handled = true;
            Logger.Fatal(e.Exception, "");
        };
        Opened += FirstOpenEventHandler;
        Activated += OnActivationChanged;
        Deactivated += OnActivationChanged;

        IsVisible = false;
    }

    private void OnActivationChanged(object? sender, EventArgs e)
    {
        if (this.GetVisualDescendants().OfType<DeviceCommunicationPage>().FirstOrDefault()?.DataContext
            is DeviceCommunicationPageViewModel viewModel)
        {
            viewModel.SyncDisplayContext();
        }
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
