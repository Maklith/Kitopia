using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Kitopia.Mobile.Services;
using Kitopia.Mobile.Views;

namespace Kitopia.Mobile;

public partial class App : Avalonia.Application
{
    private MobileCommunicationBootstrapper? _bootstrapper;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        _bootstrapper = new MobileCommunicationBootstrapper();
        var mainViewModel = _bootstrapper.MainViewModel;
        var mainView = CreateMainView(mainViewModel);

        if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            singleView.MainView = mainView;
            _ = ResumeAsync();
        }
        else if (ApplicationLifetime is IActivityApplicationLifetime activityLifetime)
        {
            activityLifetime.MainViewFactory = () => CreateMainView(mainViewModel);
            _ = ResumeAsync();
        }
        else if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new Window
            {
                Width = 1100,
                Height = 760,
                Content = mainView
            };
            desktop.Exit += (_, _) => _ = PauseAsync();
            _ = ResumeAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }

    public Task ResumeAsync(CancellationToken cancellationToken = default)
    {
        return _bootstrapper?.MainViewModel.StartAsync(cancellationToken) ?? Task.CompletedTask;
    }

    public Task PauseAsync()
    {
        // Ignore the transient pause caused by our own file picker / save dialog (Android runs it
        // as a separate Activity). Tearing the host down here would clear the discovered-device list
        // and make the in-flight send/accept fail. OnResume will be a no-op because the host stays started.
        if (_bootstrapper?.TopLevelContext.SuppressPause == true)
        {
            return Task.CompletedTask;
        }

        return _bootstrapper?.MainViewModel.StopAsync() ?? Task.CompletedTask;
    }

    private MainView CreateMainView(object mainViewModel)
    {
        return new MainView
        {
            DataContext = mainViewModel,
            TopLevelContext = _bootstrapper?.TopLevelContext
        };
    }
}
