using System.Collections.ObjectModel;
using System.Net.Http;
using System.Text.Json;
using Avalonia.Controls.Notifications;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Irihi.Avalonia.Shared.Contracts;
using Kitopia.Desktop.Features.CustomScenario.Services;
using Kitopia.Desktop.Features.Services.Interfaces;
using Kitopia.Desktop.Features.Services.Plugin;
using Kitopia.Desktop.Features.Utils;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;

namespace Kitopia.Desktop.Features.CustomScenario.ViewModels;

public partial class ScenarioDetailViewModel : ObservableObject, IDialogContext
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanManage), nameof(HasPendingInformationReview))]
    private ScenarioMarketItem _scenario;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReleases))]
    private ObservableCollection<ScenarioRelease> _releases = [];

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _error;
    [ObservableProperty] private bool _isSaving;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEditingRelease), nameof(EditingReleaseHeader))]
    private ScenarioRelease? _editingRelease;
    [ObservableProperty] private string _editingDetail = "";

    public bool CanManage => Scenario.CanManage;
    public bool HasReleases => Releases.Count > 0;
    public bool HasEditingRelease => EditingRelease is not null;
    public string EditingReleaseHeader => EditingRelease is null ? "" : $"编辑版本说明 · {EditingRelease.DisplayVersion}";
    public bool HasPendingInformationReview => Scenario.Review is { Status: 0 } review && review.Kind != 2;
    public event EventHandler<object?>? RequestClose;

    public ScenarioDetailViewModel(ScenarioMarketItem scenario)
    {
        _scenario = scenario;
    }

    public async Task InitializeAsync()
    {
        await LoadAsync();
    }

    [RelayCommand]
    public void Close() => RequestClose?.Invoke(this, null);

    [RelayCommand]
    private async Task Refresh()
    {
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        IsLoading = true;
        Error = null;
        try
        {
            var scenario = await ScenarioMarketService.GetScenarioAsync(Scenario.Id);
            var releases = await ScenarioMarketService.GetReleasesAsync(Scenario.Id);
            Scenario = scenario;
            Releases = new ObservableCollection<ScenarioRelease>(releases);
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or JsonException)
        {
            Error = exception.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task ImportRelease(ScenarioRelease? release)
    {
        if (release is null || !release.CanDownload) return;
        try
        {
            var imported = await ScenarioMarketService.ImportAsync(Scenario.Id, version: release.Version);
            await ShowToastAsync("情景已导入",
                imported.HasInit ? imported.Name : $"{imported.Name}：{imported.InitError}",
                imported.HasInit ? NotificationType.Success : NotificationType.Warning);
        }
        catch (Exception exception)
        {
            await ShowToastAsync("情景导入失败", exception.Message, NotificationType.Error);
        }
    }

    [RelayCommand]
    private async Task UploadVersion()
    {
        var local = CustomScenarioManger.CustomScenarios.FirstOrDefault(item =>
            string.Equals(item.Uuid, Scenario.SourceUuid, StringComparison.OrdinalIgnoreCase));
        if (local is null)
        {
            await ShowToastAsync("无法发布版本", "本地找不到对应的情景文件，请先在客户端打开该情景。", NotificationType.Warning);
            return;
        }

        await ServiceManager.Services.GetRequiredService<IScenarioUploadService>().ShowAsync(
            local, ServiceManager.Services.GetService<IWindowTool>()?.GetForegroundWindow());
        await LoadAsync();
    }

    [RelayCommand]
    private async Task EditInformation()
    {
        await ServiceManager.Services.GetRequiredService<IScenarioUploadService>().ShowInformationAsync(
            Scenario, ServiceManager.Services.GetService<IWindowTool>()?.GetForegroundWindow());
        await LoadAsync();
    }

    [RelayCommand]
    private async Task CancelInformationReview()
    {
        if (Scenario.Review is not { Status: 0 } review || review.Kind == 2) return;
        try
        {
            await ScenarioMarketService.CancelReviewAsync(Scenario.Id, review.Id);
            await LoadAsync();
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or JsonException)
        {
            Error = exception.Message;
        }
    }

    [RelayCommand]
    private void EditReleaseDetail(ScenarioRelease? release)
    {
        if (release is null || !release.CanEdit) return;
        EditingRelease = release;
        EditingDetail = release.CandidateDetail ?? release.Detail;
        Error = null;
    }

    [RelayCommand]
    private void CancelEditReleaseDetail()
    {
        if (!IsSaving) EditingRelease = null;
    }

    [RelayCommand]
    private async Task SaveReleaseDetail()
    {
        if (EditingRelease is null || IsSaving) return;
        if (string.IsNullOrWhiteSpace(EditingDetail) || EditingDetail.Length > 2000)
        {
            Error = "版本说明不能为空且不能超过 2000 个字符。";
            return;
        }

        IsSaving = true;
        Error = null;
        try
        {
            await ScenarioMarketService.UpdateReleaseDetailAsync(Scenario.Id, EditingRelease.Version, EditingDetail.Trim());
            EditingRelease = null;
            await ShowToastAsync("版本说明已保存",
                Scenario.IsPrivate ? "版本说明已更新。" : "版本说明已提交审核。", NotificationType.Success);
            await LoadAsync();
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or JsonException)
        {
            Error = exception.Message;
        }
        finally
        {
            IsSaving = false;
        }
    }

    [RelayCommand]
    private async Task CancelReleaseReview(ScenarioRelease? release)
    {
        if (release is not { CanCancelReview: true, SubmissionId: { } submissionId }) return;
        try
        {
            await ScenarioMarketService.CancelReviewAsync(Scenario.Id, submissionId);
            await LoadAsync();
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or JsonException)
        {
            Error = exception.Message;
        }
    }

    [RelayCommand]
    private void WithdrawRelease(ScenarioRelease? release)
    {
        if (release is not { CanWithdraw: true }) return;
        ServiceManager.Services.GetRequiredService<IToastService>().Show(new ToastRequest
        {
            Header = "撤回情景版本",
            Text = $"确定撤回 {release.DisplayVersion} 吗？撤回后该版本将不能下载。",
            AutoCloseDelay = null,
            Actions =
            [
                new ToastAction { Text = "撤回", IsPrimary = true, Callback = () => _ = WithdrawReleaseAsync(release) },
                new ToastAction { Text = "取消" }
            ]
        }, ServiceManager.Services.GetService<IWindowTool>()?.GetForegroundWindow());
    }

    private async Task WithdrawReleaseAsync(ScenarioRelease release)
    {
        try
        {
            await ScenarioMarketService.WithdrawReleaseAsync(Scenario.Id, release.Version);
            await LoadAsync();
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or JsonException)
        {
            Error = exception.Message;
        }
    }

    private static Task ShowToastAsync(string header, string text, NotificationType type)
    {
        return ServiceManager.Services.GetRequiredService<IToastService>().Show(header, text, type,
            ServiceManager.Services.GetService<IWindowTool>()?.GetForegroundWindow());
    }
}
