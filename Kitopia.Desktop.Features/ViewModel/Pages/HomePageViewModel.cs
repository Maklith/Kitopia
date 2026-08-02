using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kitopia.Desktop.Features.Services.Interfaces;
using Kitopia.Desktop.Features.Services.Plugin;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;

namespace Kitopia.Desktop.Features.ViewModel.Pages;

public partial class HomePageViewModel : ObservableRecipient
{
    private static readonly IReadOnlyDictionary<string, int> CategoryOrders = new Dictionary<string, int>
    {
        ["搜索与窗口"] = 0,
        ["截图与图像"] = 1,
        ["文件与设备"] = 2,
        ["自动化与管理"] = 3
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
                "功能执行失败",
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
