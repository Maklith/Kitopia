using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kitopia.Feature.Localization;
using Kitopia.Desktop.Features.Services.Plugin;
using PluginCore;

namespace Kitopia.Desktop.Features.ViewModel.Pages;

public partial class HomePageViewModel : ObservableRecipient
{
    private static readonly IReadOnlyDictionary<string, int> CategoryOrders = new Dictionary<string, int>
    {
        ["lang.kitopia.search_and_windows"] = 0,
        ["lang.kitopia.screenshots_and_images"] = 1,
        ["lang.kitopia.files_and_devices"] = 2,
        ["lang.kitopia.automation_and_management"] = 3
    };

    public ObservableCollection<FeatureCategory> FeatureCategories { get; } = [];

    public HomePageViewModel()
    {
        PluginOverall.Features.CollectionChanged += OnFeaturesChanged;
        RefreshFeatures();
    }

    [RelayCommand]
    private async Task ExecuteFeatureAsync(FeatureInfo? feature)
    {
        if (feature is null)
        {
            return;
        }

        try
        {
            await feature.ExecuteAsync(CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            _ = GetToastService()?.Show(
                Lang.Get("lang.kitopia.action_failed"),
                exception.InnerException?.Message ?? exception.Message,
                NotificationType.Error);
        }
    }

    private void OnFeaturesChanged(object? sender, NotifyCollectionChangedEventArgs eventArgs)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            RefreshFeatures();
            return;
        }

        Dispatcher.UIThread.Post(RefreshFeatures);
    }

    private void RefreshFeatures()
    {
        var categories = PluginOverall.AllFeatures
            .GroupBy(feature => feature.Category)
            .OrderBy(group => CategoryOrders.GetValueOrDefault(group.Key, int.MaxValue))
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new FeatureCategory
            {
                Name = group.Key,
                Features = group
                    .OrderBy(feature => feature.Order)
                    .ThenBy(feature => feature.Name, StringComparer.Ordinal)
                    .ToList()
            })
            .ToList();

        FeatureCategories.Clear();
        foreach (var category in categories)
        {
            FeatureCategories.Add(category);
        }
    }

    private static IToastService? GetToastService()
    {
        return ServiceManager.Services?.GetService(typeof(IToastService)) as IToastService;
    }
    
}
