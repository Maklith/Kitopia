using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Irihi.Avalonia.Shared.Contracts;
using Kitopia.Desktop.Controls;
using Kitopia.Desktop.Features.CustomScenario;
using Kitopia.Desktop.Features.CustomScenario.Services;
using Kitopia.Desktop.Features.Services.Interfaces;
using Kitopia.Desktop.Features.Services;
using Kitopia.Desktop.Features.Utils;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using Ursa.Controls;

namespace Kitopia.Desktop.Services;

public sealed partial class ScenarioUploadService : ObservableObject, IScenarioUploadService, IDialogContext
{
    private CustomScenario _scenario = null!;
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _version = "1.0.0";
    [ObservableProperty] private string _detail = "";
    [ObservableProperty] private bool _isPublic;
    [ObservableProperty] private string _error = "";
    [ObservableProperty] private bool _needsAuthorization;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanEdit))] private bool _isBusy;
    public bool CanEdit => !IsBusy;
    public event EventHandler<object?>? RequestClose;

    public async Task ShowAsync(CustomScenario scenario, Window? owner)
    {
        _scenario = scenario;
        Name = scenario.Name;
        try
        {
            var own = await ScenarioMarketService.GetScenariosAsync(1, 1, "", own: true, sourceUuid: scenario.Uuid);
            if (own.Items.Count > 0)
            {
                var existing = own.Items[0];
                IsPublic = existing.PublicationStatus != 0;
                if (existing.LatestReleaseStatus is 0 or 4) Version = existing.LatestReleaseVersion ?? "";
                else if (System.Version.TryParse(existing.LatestReleaseVersion?.Split('-', '+')[0], out var previous) && previous.Build < int.MaxValue)
                    Version = $"{previous.Major}.{previous.Minor}.{previous.Build + 1}";
                else Version = "";
            }
        }
        catch (HttpRequestException exception) when (exception.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        { Error = exception.Message; NeedsAuthorization = true; }
        catch (Exception exception) { Error = exception.Message; }
        await OverlayDialog.ShowCustomModal<ScenarioUploadContent, ScenarioUploadService, object>(this, null,
            new OverlayDialogOptions
            {
                TopLevelHashCode = owner?.GetHashCode(), CanLightDismiss = false,
                CanDragMove = false, IsCloseButtonVisible = false, HorizontalAnchor = HorizontalPosition.Center
            });
    }

    [RelayCommand]
    public void Close()
    {
        if (!IsBusy) RequestClose?.Invoke(this, null);
    }

    [RelayCommand]
    private async Task UploadAsync()
    {
        if (IsBusy) return;
        if (string.IsNullOrWhiteSpace(Version) || string.IsNullOrWhiteSpace(Detail))
        {
            Error = "请填写版本号和版本更新内容。";
            return;
        }
        IsBusy = true;
        Error = "";
        NeedsAuthorization = false;
        try
        {
            var result = await ScenarioMarketService.UploadAsync(_scenario, IsPublic, version: Version.Trim(), detail: Detail.Trim());
            IsBusy = false;
            Close();
            await ServiceManager.Services.GetRequiredService<IToastService>().Show("情景版本已上传",
                $"{result.Name} · v{result.LatestReleaseVersion}\n{(result.LatestReleaseStatus == 3 ? "已提交审核。" : "已保存为私有版本。")}");
        }
        catch (HttpRequestException exception) when (exception.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            Error = exception.Message;
            NeedsAuthorization = true;
        }
        catch (Exception exception)
        {
            LogManager.Logger.Error(exception, "上传情景版本失败: {Scenario}", _scenario.Name);
            Error = exception.Message;
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void Authorize() => ServiceManager.Services.GetRequiredService<IAccountService>().OpenBrowserLogin();
}
