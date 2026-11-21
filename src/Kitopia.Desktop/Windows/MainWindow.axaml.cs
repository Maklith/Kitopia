using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
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
    private static readonly Animation PageEntranceAnimation = new()
    {
        Duration = TimeSpan.FromMilliseconds(200),
        Easing = new CubicEaseOut(),
        FillMode = FillMode.None,
        Children =
        {
            new KeyFrame
            {
                Cue = new Cue(0d),
                Setters =
                {
                    new Setter(OpacityProperty, 0d),
                    new Setter(TranslateTransform.YProperty, 10d)
                }
            },
            new KeyFrame
            {
                Cue = new Cue(1d),
                Setters =
                {
                    new Setter(OpacityProperty, 1d),
                    new Setter(TranslateTransform.YProperty, 0d)
                }
            }
        }
    };

    private CancellationTokenSource? _pageTransitionCts;

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
        Closed += (_, _) => CancelPageTransition();
        PageContentHost.PropertyChanged += OnPageContentHostPropertyChanged;

        IsVisible = false;
    }

    private void OnPageContentHostPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != ContentPresenter.ContentProperty) return;
        CancelPageTransition();
        if (e.NewValue is null || !IsVisible) return;

        var cts = new CancellationTokenSource();
        _pageTransitionCts = cts;
        _ = RunPageTransitionAsync(cts);
    }

    private async Task RunPageTransitionAsync(CancellationTokenSource cts)
    {
        try
        {
            await PageEntranceAnimation.RunAsync(PageContentHost, cts.Token);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(_pageTransitionCts, cts))
            {
                _pageTransitionCts = null;
            }
            cts.Dispose();
        }
    }

    private void CancelPageTransition()
    {
        var cts = _pageTransitionCts;
        _pageTransitionCts = null;
        if (cts is null) return;
        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
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
        CancelPageTransition();
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
