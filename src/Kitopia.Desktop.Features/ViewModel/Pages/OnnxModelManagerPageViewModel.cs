using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Avalonia.Threading;
using Kitopia.Desktop.Abstractions.Shell;
using Kitopia.Desktop.Features.Services.Interfaces;
using Kitopia.Desktop.Features.Services.Plugin;

namespace Kitopia.Desktop.Features.ViewModel.Pages;

public sealed partial class OnnxModelManagerPageViewModel : ObservableObject
{
    private readonly IConfigService _configService;
    private readonly IDesktopShell _shell;
    private readonly IOnnxRuntimeProbe _runtimeProbe;

    public OnnxModelManagerPageViewModel(IConfigService configService, IDesktopShell shell, IOnnxRuntimeProbe runtimeProbe)
    {
        _configService = configService;
        _shell = shell;
        _runtimeProbe = runtimeProbe;
        WeakReferenceMessenger.Default.Register<PluginsReloaded>(this, static (recipient, _) =>
        {
            var viewModel = (OnnxModelManagerPageViewModel)recipient;
            Dispatcher.UIThread.Post(async () =>
            {
                if (viewModel.RefreshCommand.IsRunning)
                {
                    viewModel.RefreshCommand.Cancel();
                    await viewModel.RefreshCommand.ExecutionTask!;
                }
                await viewModel.RefreshCommand.ExecuteAsync(false);
            });
        });
    }

    [ObservableProperty] private IReadOnlyList<OnnxRuntimeStatus> _runtimes = [];
    [ObservableProperty] private IReadOnlyList<OnnxModelRuntimeSelection> _models = [];

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task RefreshAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        Runtimes = PluginOverall.AllTargetDevices
            .Concat(_configService.Config.OnnxTargetDevices.Values)
            .Append("CPU")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(device => device != "CPU")
            .Select(device => new OnnxRuntimeStatus(device))
            .ToArray();
        Models = PluginOverall.AllOnnxModelInfos
            .Select(model => new OnnxModelRuntimeSelection(model, Runtimes, _configService)).ToArray();

        foreach (var status in Runtimes)
        {
            if (cancellationToken.IsCancellationRequested) return;
            var factory = PluginOverall.GetOnnxRuntime(status.Device);
            try
            {
                if (factory is null)
                {
                    status.IsAvailable = false;
                    status.Error = "lang.kitopia.onnx.runtime_not_installed";
                }
                else
                {
                    status.IsAvailable = await _runtimeProbe.CheckAsync(status.Device, cancellationToken, forceRefresh);
                    if (cancellationToken.IsCancellationRequested) return;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                status.IsAvailable = false;
                // Keep strings only: retaining plugin exception objects prevents assembly unloading.
                status.Error = exception.GetBaseException().Message;
            }
            finally
            {
                status.IsChecking = false;
            }
        }
    }

    [RelayCommand]
    private void OpenDocumentation(OnnxRuntimeStatus runtime) => _shell.Open(runtime.DocumentationUrl);

    [RelayCommand]
    private void OpenDrivers(OnnxRuntimeStatus runtime) => _shell.Open(runtime.DriverUrl!);
}
