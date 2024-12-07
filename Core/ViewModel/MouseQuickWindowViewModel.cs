using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Core.SDKs.Services;
using Core.SDKs.Services.Config;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;

namespace Core.ViewModel;

public struct SelectedItem
{
    public FileType type { get; set; }

    public object obj { get; set; }
}

public partial class MouseQuickWindowViewModel : ObservableRecipient
{
    [ObservableProperty] private ObservableCollection<SearchViewItem> _items = new();

    [ObservableProperty] private SelectedItem selectedItem;

    public MouseQuickWindowViewModel()
    {
        foreach (var configMouseQuickItem in ConfigManger.Config.mouseQuickItems)
            if (ServiceManager.Services.GetService<SearchWindowViewModel>()!._collection.TryGetValue(
                    configMouseQuickItem, out var item))
                Items.Add(item);

        if (Items.Count() < 9)
            //for (var i = 0; i < 12; i++)
        {
            Items.Add(new SearchViewItem()
            {
                ItemDisplayName = "添加",
                FileType = FileType.None,
                IconSymbol = 0xF136,
                OnlyKey = "Add",
                Icon = null,
                IsVisible = true
            });
        }
    }

    [RelayCommand]
    public void Excute(SearchViewItem? searchViewItem)
    {
        if (searchViewItem.OnlyKey == "Add")
            ServiceManager.Services.GetService<ISearchItemChooseService>()!.Choose((item) =>
            {
                Dispatcher.UIThread.InvokeAsync(() => { Items.Add(item); });
                ConfigManger.Config.mouseQuickItems.Add(item.OnlyKey);
                ConfigManger.Save();
            });
        else
            ServiceManager.Services.GetService<ISearchItemTool>()!.OpenFile(searchViewItem);
    }

    [RelayCommand]
    public void Remove(SearchViewItem searchViewItem)
    {
        Items.Remove(searchViewItem);
        ConfigManger.Config.mouseQuickItems.Remove(searchViewItem.OnlyKey);
        ConfigManger.Save();
    }
}