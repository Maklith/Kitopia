using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kitopia.Desktop.Features.Indexing;
using Kitopia.Desktop.Features.Services.Interfaces;

namespace Kitopia.Desktop.Features.ViewModel.Pages;

public partial class IndexStatusPageViewModel : ObservableObject, IDisposable
{
    private readonly IIndexService _indexService;
    private readonly IIndexMaintenanceService _maintenanceService;
    private readonly INavigationService _navigationService;
    private bool _disposed;

    [ObservableProperty] private IndexStatusSnapshot _status;

    public IndexStatusPageViewModel(IIndexService indexService, IIndexMaintenanceService maintenanceService,
        INavigationService navigationService)
    {
        _indexService = indexService;
        _maintenanceService = maintenanceService;
        _navigationService = navigationService;
        Status = indexService.GetStatus();
        indexService.StatusChanged += OnStatusChanged;
    }

    [RelayCommand]
    private void OpenEverythingSettings() => _navigationService.Navigate("settings/field/useEverything");

    [RelayCommand]
    private void OpenManagedIndexSettings() => _navigationService.Navigate("settings/field/managedIndexDirectories");

    [RelayCommand]
    private Task RebuildPinyinAsync() => _indexService.RebuildAsync(IndexRebuildScope.Pinyin);

    [RelayCommand]
    private async Task RebuildDocumentsAsync()
    {
        await RefreshAllFileSourcesAsync();
        await _indexService.RebuildAsync(IndexRebuildScope.Documents);
    }

    [RelayCommand]
    private async Task RebuildImagesAsync()
    {
        await RefreshAllFileSourcesAsync();
        await _indexService.RebuildAsync(IndexRebuildScope.Images);
    }

    [RelayCommand]
    private async Task RebuildAllAsync()
    {
        await RefreshAllFileSourcesAsync();
        await _indexService.RebuildAsync(IndexRebuildScope.All);
    }

    private async Task RefreshAllFileSourcesAsync()
    {
        await _maintenanceService.RefreshManagedFilesAsync();
        await _maintenanceService.RefreshEverythingFilesAsync();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _indexService.StatusChanged -= OnStatusChanged;
        _disposed = true;
    }

    private void OnStatusChanged(object? sender, IndexStatusSnapshot status) => Status = status;
}
