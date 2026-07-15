using System;
using Kitopia.Desktop.Features.Services.Interfaces;
using Kitopia.Desktop.Features.Search.ViewModels;
using Kitopia.Desktop.Windows;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;

namespace Kitopia.Desktop.Services;

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
