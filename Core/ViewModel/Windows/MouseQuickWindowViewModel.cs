using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Core.Services.Config;
using Core.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;

namespace Core.ViewModel.Windows;

public struct SelectedItem
{
    public FileType type { get; set; }

    public object obj { get; set; }
}

public partial class MouseQuickWindowViewModel : ObservableRecipient
{
    [ObservableProperty] private ObservableCollection<SearchViewItem> _items = new();

    [ObservableProperty] private SelectedItem _selectedItem;

    public MouseQuickWindowViewModel()
    {
        foreach (var configMouseQuickItem in ConfigManger.Config.mouseQuickItems)
            if (ServiceManager.Services.GetService<SearchWindowViewModel>()!.Index.TryGetValue(
                    configMouseQuickItem, out var entry))
                Items.Add(entry.ToSearchViewItem());

        if (Items.Count<SearchViewItem>() < 9)
            //for (var i = 0; i < 12; i++)
            Items.Add(new SearchViewItem
            {
                ItemDisplayName = "添加",
                FileType = FileType.None,
                IconSymbol = 0xF136,
                OnlyKey = "Add",
                Icon = null,
                IsVisible = true
            });
    }

    [RelayCommand]
    public void Excute(SearchViewItem? searchViewItem)
    {
        if (searchViewItem.OnlyKey == "Add")
        {
            ServiceManager.Services.GetService<ISearchItemChooseService>()!.Choose(item =>
            {
                Dispatcher.UIThread.InvokeAsync(() => { Items.Add(item); });
                ConfigManger.Config.mouseQuickItems.Add(item.OnlyKey);
                ConfigManger.Save();
            });
        }
        else
        {
            ServiceManager.Services.GetService<ISearchItemTool>()!.OpenFile(searchViewItem);
            WeakReferenceMessenger.Default.Send("a", "SearchWindowClose");
        }
    }

    [RelayCommand]
    public void Remove(SearchViewItem searchViewItem)
    {
        Items.Remove(searchViewItem);
        ConfigManger.Config.mouseQuickItems.Remove(searchViewItem.OnlyKey);
        ConfigManger.Save();
    }
}