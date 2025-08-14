using System;
using Core.Services;
using Core.ViewModel.Windows;
using KitopiaAvalonia.Windows;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;

namespace KitopiaAvalonia.Services;

public class SearchItemChooseService : ISearchItemChooseService
{
    public void Choose(Action<SearchViewItem> action)
    {
        ServiceManager.Services.GetService<SearchWindowViewModel>()!.SetSelectMode(true, action);
        ServiceManager.Services.GetService<SearchWindow>()!.Show();

        ServiceManager.Services.GetService<SearchWindow>()!.Focus();
        ServiceManager.Services.GetService<SearchWindow>()!.tx.Focus();
    }
}