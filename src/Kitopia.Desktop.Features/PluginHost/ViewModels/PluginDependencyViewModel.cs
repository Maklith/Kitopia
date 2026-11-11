using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Kitopia.Desktop.Features.Services.Plugin;
using Kitopia.Desktop.Features.Search.Semantic;
using Microsoft.Extensions.DependencyInjection;
using PluginCore.Media;

namespace Kitopia.Desktop.Features.ViewModel.Pages.plugin;

public sealed partial class PluginDependencyViewModel : ObservableObject, IDisposable
{
    private readonly Func<bool>? _checkAvailability;
    private bool _disposed;

    public PluginDependencyViewModel(string nameSign, string versionRange, string description,
        string? displayName = null, Func<bool>? checkAvailability = null)
    {
        NameSign = nameSign;
        VersionRange = versionRange;
        Description = description;
        DisplayName = displayName ?? nameSign;
        _checkAvailability = checkAvailability ?? (nameSign switch
        {
            PluginReleaseRules.HostBundledFfmpegPluginNameSign => static () =>
                PluginManager.GetEnablePlugins().TryGetValue(PluginReleaseRules.HostBundledFfmpegPluginNameSign, out var plugin) &&
                plugin.ServiceProvider?.GetService<Ffmpeg>() is { HasRequiredFiles: true },
            PluginReleaseRules.HostBundledSemanticSearchPluginNameSign => static () => EmbeddingGemmaModelPackage.IsComplete(),
            _ => null
        });
        WeakReferenceMessenger.Default.Register<PluginsReloaded>(this, static (recipient, _) =>
            ((PluginDependencyViewModel)recipient).Refresh());
        WeakReferenceMessenger.Default.Register<PluginDownloadProgress>(this, static (recipient, progress) =>
        {
            var item = (PluginDependencyViewModel)recipient;
            if (item.NameSign == progress.PluginInfo.NameSign) item.Refresh();
        });
    }

    public string NameSign { get; }
    public string VersionRange { get; }
    public string Description { get; }
    public string DisplayName { get; }
    public string Name => PluginManager.GetPluginLocalInfoByPlgStr(NameSign)?.PluginBaseInfo.Name ?? DisplayName;
    public PluginDownloadProgress? Download => PluginManager.Downloads.GetValueOrDefault(NameSign);
    public bool IsDownloading => Download is not null;
    public bool IsCompatible => PluginManager.GetPluginLocalInfoByPlgStr(NameSign) is { } installed &&
        PluginDependencyService.VersionInRange(installed.PluginBaseInfo.Version, VersionRange);
    public bool IsEnabled => IsCompatible && PluginManager.GetEnablePlugins().ContainsKey(NameSign);
    public bool IsAvailable => IsEnabled && (_checkAvailability?.Invoke() ?? true);
    public bool CanInstall => !_disposed && !IsAvailable && !IsInstalling && !IsDownloading;
    public string StatusKey => IsAvailable ? "lang.kitopia.plugins.dependency_ready"
        : IsDownloading || IsInstalling ? "lang.kitopia.plugins.dependency_installing"
        : PluginManager.GetPluginLocalInfoByPlgStr(NameSign) is null ? "lang.kitopia.plugins.dependency_missing"
        : !IsCompatible ? "lang.kitopia.plugins.dependency_incompatible"
        : IsEnabled ? "lang.kitopia.plugins.dependency_incomplete" : "lang.kitopia.disabled";
    public string ActionKey => IsEnabled && !IsAvailable ? "lang.kitopia.plugins.reinstall_dependency"
        : IsCompatible ? "lang.kitopia.plugins.enable_dependency" : "lang.kitopia.plugins.install_dependency";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanInstall))]
    [NotifyPropertyChangedFor(nameof(StatusKey))]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand))]
    private bool _isInstalling;
    [ObservableProperty] private string? _error;

    [RelayCommand(CanExecute = nameof(CanInstall), IncludeCancelCommand = true)]
    private async Task InstallAsync(CancellationToken cancellationToken)
    {
        IsInstalling = true;
        Error = null;
        try
        {
            var success = await PluginManager.InstallDependencyAsync(NameSign, VersionRange,
                reinstall: IsEnabled && !IsAvailable, cancellationToken: cancellationToken);
            // An installed but disabled model can also have missing files. Enable first so its
            // availability can be checked, then repair through the same package transaction.
            if (success && IsEnabled && !IsAvailable)
                success = await PluginManager.InstallDependencyAsync(NameSign, VersionRange, true, cancellationToken);
            if (!success || !IsAvailable)
                Error = cancellationToken.IsCancellationRequested ? "lang.kitopia.cancelled" : "lang.kitopia.plugin_installation_failed";
        }
        finally
        {
            IsInstalling = false;
            Refresh();
        }
    }

    public void Refresh()
    {
        if (_disposed) return;
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(Refresh);
            return;
        }
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Download));
        OnPropertyChanged(nameof(IsDownloading));
        OnPropertyChanged(nameof(IsCompatible));
        OnPropertyChanged(nameof(IsEnabled));
        OnPropertyChanged(nameof(IsAvailable));
        OnPropertyChanged(nameof(StatusKey));
        OnPropertyChanged(nameof(ActionKey));
        OnPropertyChanged(nameof(CanInstall));
        InstallCommand.NotifyCanExecuteChanged();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        InstallCommand.Cancel();
        WeakReferenceMessenger.Default.UnregisterAll(this);
    }
}
