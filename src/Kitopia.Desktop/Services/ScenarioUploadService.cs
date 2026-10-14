using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Irihi.Avalonia.Shared.Contracts;
using Kitopia.Feature.Localization;
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
    private CustomScenario? _scenario;
    private ScenarioMarketItem? _marketItem;
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private string _version = "1.0.0";
    [ObservableProperty] private string _detail = "";
    [ObservableProperty] private ObservableCollection<string> _tags = [];
    [ObservableProperty] private string _tagInput = "";
    [ObservableProperty] private bool _isPublic;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DialogTitle), nameof(IsRelease), nameof(CanChangeVisibility))]
    private bool _isInformationEdit;
    [ObservableProperty] private string _error = "";
    [ObservableProperty] private bool _needsAuthorization;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanEdit), nameof(CanChangeVisibility))] private bool _isBusy;
    public bool CanEdit => !IsBusy;
    public bool CanChangeVisibility => CanEdit && (IsInformationEdit || _marketItem?.PublicationStatus != 2);
    public bool HasTags => Tags.Count > 0;
    public string DialogTitle => IsInformationEdit ? Lang.Get("lang.kitopia.edit_scenario_information") : Lang.Get("lang.kitopia.publish_scenario_version");
    public bool IsRelease => !IsInformationEdit;
    public event EventHandler<object?>? RequestClose;

    public async Task ShowAsync(CustomScenario scenario, Window? owner)
    {
        _scenario = scenario;
        _marketItem = null;
        IsInformationEdit = false;
        Name = scenario.Name;
        Description = scenario.Description;
        Version = "1.0.0";
        Detail = "";
        IsPublic = false;
        Tags.Clear();
        TagInput = "";
        OnPropertyChanged(nameof(HasTags));
        Error = "";
        NeedsAuthorization = false;
        try
        {
            var own = await ScenarioMarketService.GetScenariosAsync(1, 1, "", own: true, sourceUuid: scenario.Uuid);
            if (own.Items.Count > 0)
            {
                var existing = own.Items[0];
                _marketItem = existing;
                IsPublic = existing.PublicationStatus != 0;
                var candidate = existing.Review is { Status: 0 } review && review.Kind != 2 ? review : null;
                Name = candidate?.Name ?? existing.Name;
                Description = candidate?.Description ?? existing.Description;
                foreach (var tag in candidate?.Tags ?? existing.Tags ?? []) Tags.Add(tag);
                if (existing.LatestReleaseStatus is 0 or 4) Version = existing.LatestReleaseVersion ?? "";
                else if (System.Version.TryParse(existing.LatestReleaseVersion?.Split('-', '+')[0], out var previous) && previous.Build < int.MaxValue)
                    Version = $"{previous.Major}.{previous.Minor}.{previous.Build + 1}";
                else Version = "";
            }
        }
        catch (HttpRequestException exception) when (exception.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        { Error = exception.Message; NeedsAuthorization = true; }
        catch (Exception exception) { Error = exception.Message; }
        OnPropertyChanged(nameof(HasTags));
        await OverlayDialog.ShowCustomModal<ScenarioUploadContent, ScenarioUploadService, object>(this, null,
            new OverlayDialogOptions
            {
                TopLevelHashCode = owner?.GetHashCode(), CanLightDismiss = false,
                CanDragMove = false, IsCloseButtonVisible = false, HorizontalAnchor = HorizontalPosition.Center
            });
    }

    public async Task ShowInformationAsync(ScenarioMarketItem item, Window? owner)
    {
        _scenario = null;
        _marketItem = item;
        IsInformationEdit = true;
        Name = item.Name;
        Description = item.Description;
        Version = "";
        Detail = "";
        IsPublic = item.PublicationStatus != 0;
        Tags.Clear();
        if (item.Review is { Status: 0 } review && review.Kind != 2)
        {
            Name = review.Name ?? Name;
            Description = review.Description ?? Description;
            foreach (var tag in review.Tags ?? []) Tags.Add(tag);
        }
        else
        {
            foreach (var tag in item.Tags ?? []) Tags.Add(tag);
        }
        TagInput = "";
        OnPropertyChanged(nameof(HasTags));
        Error = "";
        NeedsAuthorization = false;
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
    private void AddTag()
    {
        var tag = TagInput.Trim();
        if (tag.Length == 0) return;
        if (tag.Length > 20)
        {
            Error = Lang.Get("lang.kitopia.tags_must_not_exceed_20_characters");
            return;
        }
        if (!tag.All(char.IsLetterOrDigit))
        {
            Error = Lang.Get("lang.kitopia.tags_may_contain_only_letters_digits_or_chinese_characters");
            return;
        }
        if (Tags.Count >= 10)
        {
            Error = Lang.Get("lang.kitopia.up_to_10_tags_allowed");
            return;
        }
        if (Tags.Any(item => string.Equals(item, tag, StringComparison.OrdinalIgnoreCase)))
        {
            TagInput = "";
            return;
        }
        Tags.Add(tag);
        TagInput = "";
        Error = "";
        OnPropertyChanged(nameof(HasTags));
    }

    [RelayCommand]
    private void RemoveTag(string tag)
    {
        if (Tags.Remove(tag)) OnPropertyChanged(nameof(HasTags));
    }

    [RelayCommand]
    private async Task UploadAsync()
    {
        if (IsBusy) return;
        if (IsInformationEdit)
        {
            if (_marketItem is null || string.IsNullOrWhiteSpace(Name))
            {
                Error = Lang.Get("lang.kitopia.enter_a_scenario_name");
                return;
            }
        }
        else if (_scenario is null || string.IsNullOrWhiteSpace(Version) || string.IsNullOrWhiteSpace(Detail))
        {
            Error = Lang.Get("lang.kitopia.enter_a_version_number_and_release_notes");
            return;
        }
        IsBusy = true;
        Error = "";
        NeedsAuthorization = false;
        try
        {
            ScenarioMarketItem result;
            if (IsInformationEdit)
            {
                result = await ScenarioMarketService.UpdateInformationAsync(_marketItem!.Id, Name.Trim(),
                    Description.Trim(), Tags, IsPublic);
            }
            else
            {
                result = await ScenarioMarketService.UploadAsync(_scenario!, IsPublic, version: Version.Trim(),
                    detail: Detail.Trim(), tags: Tags, scenarioId: _marketItem?.Id);
            }
            IsBusy = false;
            Close();
            await ServiceManager.Services.GetRequiredService<IToastService>().Show(
                IsInformationEdit ? Lang.Get("lang.kitopia.scenario_information_saved") : Lang.Get("lang.kitopia.scenario_version_uploaded"),
                IsInformationEdit ? (IsPublic ? Lang.Get("lang.kitopia.public_information_submitted_for_review") : Lang.Get("lang.kitopia.scenario_information_saved_privately")) :
                    $"{result.Name} · v{result.LatestReleaseVersion}\n{(result.LatestReleaseStatus == 3 ? Lang.Get("lang.kitopia.submitted_for_review") : Lang.Get("lang.kitopia.saved_as_a_private_version"))}");
        }
        catch (HttpRequestException exception) when (exception.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            Error = exception.Message;
            NeedsAuthorization = true;
        }
        catch (Exception exception)
        {
            LogManager.Logger.Error(exception, "上传情景版本失败: {Scenario}", _scenario?.Name ?? Name);
            Error = exception.Message;
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void Authorize() => ServiceManager.Services.GetRequiredService<IAccountService>().OpenBrowserLogin();
}
