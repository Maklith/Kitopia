using System.ComponentModel;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kitopia.Desktop.Features.Indexing;
using Kitopia.Desktop.Features.Services.Interfaces;
using Kitopia.Desktop.Features.Utils;
using Kitopia.Feature.Localization;
using PluginCore;

namespace Kitopia.Desktop.Features.ViewModel.Pages;

public partial class IndexStatusPageViewModel : ObservableObject, IDisposable
{
    private readonly IIndexService _indexService;
    private readonly IIndexMaintenanceService _maintenanceService;
    private readonly INavigationService _navigationService;
    private readonly IToastService _toastService;
    private IndexStatusSnapshot? _pendingStatus;
    private CancellationTokenSource? _operationCancellation;
    private string? _preparingOperation;
    private string? _operationError;
    private int _statusUpdateQueued;
    private bool _disposed;

    [ObservableProperty] private IndexStatusSnapshot _status;

    public IndexStatusPageViewModel(IIndexService indexService, IIndexMaintenanceService maintenanceService,
        INavigationService navigationService, IToastService toastService)
    {
        _indexService = indexService;
        _maintenanceService = maintenanceService;
        _navigationService = navigationService;
        _toastService = toastService;
        Status = indexService.GetStatus();
        indexService.StatusChanged += OnIndexStatusChanged;
        Lang.Current.PropertyChanged += OnLanguageChanged;
    }

    public bool IsIndexingActive => _operationCancellation is not null || Status.IsRebuilding;

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(Lang.Language)) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed) return;
            OnPropertyChanged(nameof(FileProgressText));
            OnPropertyChanged(nameof(ActiveOperationText));
            OnPropertyChanged(nameof(CurrentItemText));
            OnPropertyChanged(nameof(LatestErrorText));
            OnPropertyChanged(nameof(IndexControlToolTip));
        });
    }

    public bool IsProgressIndeterminate => IsIndexingActive && Status.TotalFileItems == 0;

    public int ProgressMaximum => Math.Max(Status.TotalFileItems, 1);

    public int ProgressValue => Math.Clamp(Status.CompletedFileItems, 0, ProgressMaximum);

    public string FileProgressText => Status.TotalFileItems > 0
        ? Lang.Format("lang.kitopia.value_value_files_completed", Status.CompletedFileItems, Status.TotalFileItems)
        : IsIndexingActive ? Lang.Get("lang.kitopia.preparing_file_list") : Lang.Get("lang.kitopia.file_indexing_has_not_started");

    public string ActiveOperationText
    {
        get
        {
            var operation = _preparingOperation ?? Status.CurrentOperation ?? Lang.Get("lang.kitopia.indexing_idle");
            return Status.IsPaused ? Lang.Format("lang.kitopia.paused_value", Lang.Get(operation)) : Lang.Get(operation);
        }
    }

    public string CurrentItemText => string.IsNullOrWhiteSpace(Status.CurrentItem)
        ? IsIndexingActive ? Lang.Get("lang.kitopia.waiting_for_next_item") : Lang.Get("lang.kitopia.none")
        : Status.CurrentItem;

    public string LatestErrorText => Lang.Get(_operationError ?? Status.LastError ?? "lang.kitopia.none");

    public string IndexControlGlyph => Status.IsRebuilding && !Status.IsPaused ?  "\uf5a1": "\uedb5";

    public string IndexControlToolTip => Status.IsRebuilding
        ? Status.IsPaused ? Lang.Get("lang.kitopia.resume_indexing") : Lang.Get("lang.kitopia.pause_indexing")
        : Lang.Get("lang.kitopia.start_indexing");

    [RelayCommand]
    private void OpenEverythingSettings() => _navigationService.Navigate("settings/field/useEverything");

    [RelayCommand]
    private void OpenManagedIndexSettings() => _navigationService.Navigate("settings/field/managedIndexDirectories");

    [RelayCommand(CanExecute = nameof(CanToggleIndexing))]
    private Task ToggleIndexingAsync()
    {
        if (!Status.IsRebuilding)
        {
            return StartIndexingAsync();
        }

        if (Status.IsPaused)
        {
            _indexService.ResumeIndexing();
        }
        else
        {
            _indexService.PauseIndexing();
        }

        return Task.CompletedTask;
    }

    [RelayCommand(CanExecute = nameof(CanCancelIndexing))]
    private void CancelIndexing()
    {
        _operationCancellation?.Cancel();
        _indexService.CancelIndexing();
    }

    [RelayCommand(CanExecute = nameof(CanResetAndRebuild))]
    private void ConfirmResetAndRebuild()
    {
        var dialog = new DialogContent
        {
            Title = Lang.Get("lang.kitopia.reset_the_file_index"),
            Content = Lang.Get("lang.kitopia.remove_the_index_embeddings_and_fingerprints_then_rescan_your_files_and_indexing_settings_will_be_kept"),
            PrimaryButtonText = Lang.Get("lang.kitopia.reset_and_rebuild"),
            SecondaryButtonText = Lang.Get("lang.kitopia.cancel"),
            PrimaryAction = async () => await ResetAndRebuildAsync()
        };
        _ = _toastService.Show(dialog.ToToastRequest(NotificationType.Warning));
    }

    private bool CanToggleIndexing() => !IsIndexingActive || Status.IsRebuilding;

    private bool CanCancelIndexing() => IsIndexingActive;

    private bool CanResetAndRebuild() => _operationCancellation is null;

    private Task StartIndexingAsync() => RunOperationAsync(
        "lang.kitopia.stopping_background_indexing",
        async cancellationToken =>
        {
            _indexService.CancelIndexing();
            await _maintenanceService.StopBackgroundIndexingAsync();
            cancellationToken.ThrowIfCancellationRequested();
            SetPreparingOperation("lang.kitopia.refreshing_file_sources");
            await RefreshAllFileSourcesAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            SetPreparingOperation("lang.kitopia.waiting_for_indexing_tasks");
            await _indexService.IndexIncrementalAsync(IndexRebuildScope.Files, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        });

    private Task ResetAndRebuildAsync() => RunOperationAsync(
        "lang.kitopia.stopping_background_indexing",
        async cancellationToken =>
        {
            _indexService.CancelIndexing();
            await _maintenanceService.StopBackgroundIndexingAsync();
            cancellationToken.ThrowIfCancellationRequested();
            SetPreparingOperation("lang.kitopia.clearing_all_file_index");
            await _indexService.ResetAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            SetPreparingOperation("lang.kitopia.rescanning_file_sources");
            await RefreshAllFileSourcesAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            SetPreparingOperation("lang.kitopia.rebuilding_file_index");
            await _indexService.RebuildAsync(IndexRebuildScope.All, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        });

    private async Task RunOperationAsync(string preparingOperation, Func<CancellationToken, Task> operation)
    {
        if (_operationCancellation is not null)
        {
            return;
        }

        var cancellation = new CancellationTokenSource();
        _operationCancellation = cancellation;
        _operationError = null;
        SetPreparingOperation(preparingOperation);
        NotifyOperationStateChanged();
        try
        {
            await operation(cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            _operationError = "索引任务已取消";
        }
        catch (Exception exception)
        {
            _operationError = exception.Message;
            _ = _toastService.Show(Lang.Get("lang.kitopia.indexing_failed"), exception.Message, NotificationType.Error);
        }
        finally
        {
            if (ReferenceEquals(_operationCancellation, cancellation))
            {
                _operationCancellation = null;
            }

            cancellation.Dispose();
            _preparingOperation = null;
            NotifyOperationStateChanged();
        }
    }

    private async Task RefreshAllFileSourcesAsync(CancellationToken cancellationToken)
    {
        await _maintenanceService.RefreshManagedFilesAsync(cancellationToken);
        await _maintenanceService.RefreshEverythingFilesAsync(cancellationToken);
    }

    private void OnIndexStatusChanged(object? sender, IndexStatusSnapshot _)
    {
        if (_disposed)
        {
            return;
        }

        // StatusChanged can be raised by the indexing loop and the asynchronous count
        // publisher concurrently. Always queue the service's newest snapshot so a late
        // notification cannot replace a newer progress value in the page.
        Volatile.Write(ref _pendingStatus, _indexService.GetStatus());
        if (Interlocked.Exchange(ref _statusUpdateQueued, 1) != 0)
        {
            return;
        }

        Dispatcher.UIThread.Post(ApplyPendingStatus);
    }

    private void ApplyPendingStatus()
    {
        if (_disposed)
        {
            Volatile.Write(ref _statusUpdateQueued, 0);
            return;
        }

        var appliedStatus = Volatile.Read(ref _pendingStatus);
        if (appliedStatus is not null)
        {
            Status = appliedStatus;
        }

        Volatile.Write(ref _statusUpdateQueued, 0);
        if (ReferenceEquals(appliedStatus, Volatile.Read(ref _pendingStatus))
            || Interlocked.Exchange(ref _statusUpdateQueued, 1) != 0)
        {
            return;
        }

        Dispatcher.UIThread.Post(ApplyPendingStatus);
    }

    partial void OnStatusChanged(IndexStatusSnapshot value) => NotifyOperationStateChanged();

    private void SetPreparingOperation(string operation)
    {
        _preparingOperation = operation;
        OnPropertyChanged(nameof(ActiveOperationText));
    }

    private void NotifyOperationStateChanged()
    {
        OnPropertyChanged(nameof(IsIndexingActive));
        OnPropertyChanged(nameof(IsProgressIndeterminate));
        OnPropertyChanged(nameof(ProgressMaximum));
        OnPropertyChanged(nameof(ProgressValue));
        OnPropertyChanged(nameof(FileProgressText));
        OnPropertyChanged(nameof(ActiveOperationText));
        OnPropertyChanged(nameof(CurrentItemText));
        OnPropertyChanged(nameof(LatestErrorText));
        OnPropertyChanged(nameof(IndexControlGlyph));
        OnPropertyChanged(nameof(IndexControlToolTip));
        ToggleIndexingCommand.NotifyCanExecuteChanged();
        CancelIndexingCommand.NotifyCanExecuteChanged();
        ConfirmResetAndRebuildCommand.NotifyCanExecuteChanged();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _indexService.StatusChanged -= OnIndexStatusChanged;
        Lang.Current.PropertyChanged -= OnLanguageChanged;
        _disposed = true;
    }
}
