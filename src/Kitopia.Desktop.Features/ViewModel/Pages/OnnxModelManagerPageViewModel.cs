using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kitopia.Desktop.Abstractions.Shell;
using Kitopia.Desktop.Features.Services.Interfaces;
using Kitopia.Desktop.Features.Services.Plugin;

namespace Kitopia.Desktop.Features.ViewModel.Pages;

public sealed partial class OnnxModelManagerPageViewModel(
    IConfigService configService, IDesktopShell shell, IOnnxRuntimeProbe runtimeProbe) : ObservableObject
{
    [ObservableProperty] private IReadOnlyList<OnnxRuntimeStatus> _runtimes = [];
    [ObservableProperty] private IReadOnlyList<OnnxModelRuntimeSelection> _models = [];

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task RefreshAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        Runtimes = PluginOverall.AllTargetDevices
            .Concat(configService.Config.OnnxTargetDevices.Values)
            .Append("CPU")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(device => device != "CPU")
            .Select(device => new OnnxRuntimeStatus(device))
            .ToArray();
        Models = PluginOverall.AllOnnxModelInfos
            .Select(model => new OnnxModelRuntimeSelection(model, Runtimes, configService)).ToArray();

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
                    status.IsAvailable = await runtimeProbe.CheckAsync(status.Device, cancellationToken, forceRefresh);
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
    private void OpenDocumentation(OnnxRuntimeStatus runtime) => shell.Open(runtime.DocumentationUrl);

    [RelayCommand]
    private void OpenDrivers(OnnxRuntimeStatus runtime) => shell.Open(runtime.DriverUrl!);
}
