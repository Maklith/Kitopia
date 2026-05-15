using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Core.ViewModel.Windows;
using KitopiaAvalonia.Windows;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;

namespace KitopiaAvalonia.Controls;

public class SearchItemShow : Button
{
    public static readonly StyledProperty<SearchViewItem> SearchViewItemProperty =
        AvaloniaProperty.Register<SearchItemShow, SearchViewItem>(nameof(SearchViewItem));

    public static readonly AvaloniaProperty IsSelectedProperty =
        AvaloniaProperty.Register<SearchItemShow, bool>(nameof(IsSelected));


    public static readonly StyledProperty<string> OnlyKeyProperty =
        AvaloniaProperty.Register<SearchItemShow, string>(nameof(OnlyKey), "");


    [Bindable(true)]
    [Category("SearchViewItem")]
    public SearchViewItem SearchViewItem
    {
        get => (SearchViewItem)GetValue(SearchViewItemProperty);
        set => SetValue(SearchViewItemProperty, value);
    }

    [Bindable(true)]
    [Category("IsSelected")]
    public bool IsSelected
    {
        get => (bool)GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }

    [Bindable(true)]
    [Category("OnlyKey")]
    public string OnlyKey
    {
        get => (string)GetValue(OnlyKeyProperty);
        set => SetValue(OnlyKeyProperty, value);
    }

    public SearchItemShow()
    {
       
        Command = new RelayCommand(ChosseCommand);
    }

    static SearchItemShow()
    {
        OnlyKeyProperty.Changed.AddClassHandler<SearchItemShow>(OnOnlyKeyChanged);
    }

    private void ChosseCommand()
    {
        ServiceManager.Services.GetService<SearchWindowViewModel>()!.SetSelectMode(true,
            item => { Dispatcher.UIThread.Post(() => { OnlyKey = item.OnlyKey; }); });
        ServiceManager.Services.GetService<SearchWindow>()!.Show();

        var windowToolServiceWindow = ServiceManager.Services.GetService<IWindowTool>();
        windowToolServiceWindow.SetForegroundWindow(
            ServiceManager.Services.GetService<SearchWindow>()!.TryGetPlatformHandle()
                .Handle);
        ServiceManager.Services.GetService<SearchWindow>()!.tx.Focus();
    }
    
    private static void OnOnlyKeyChanged(SearchItemShow searchItemShow, AvaloniaPropertyChangedEventArgs e)
    {
        var value = (string)e.NewValue;
        if (value is null) return;

        var searchViewItems = ServiceManager.Services.GetService<SearchWindowViewModel>()!.IndexCollection;
        //var enumerable = searchViewItems.Where(e => e.Key.Contains("TopSAP")).ToList();
        if (searchViewItems.TryGetValue(value,
                out var searchViewItem))
        {
            searchItemShow.SearchViewItem = searchViewItem;
            searchItemShow.IsSelected = true;
        }
        else
        {
            searchItemShow.SearchViewItem = null;
            searchItemShow.IsSelected = false;
        }
    }
}