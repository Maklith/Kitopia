using System;
using Avalonia;
using Avalonia.Controls;
using Kitopia.Desktop.Features.Services.Interfaces;
using Kitopia.Desktop.Features.ViewModel.Account;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;

namespace Kitopia.Desktop.Controls;

public partial class AccountCardControl : UserControl
{
    public AccountCardControl()
    {
        EnsureDataContext();
        InitializeComponent();
    }

    private void EnsureDataContext()
    {
        if (DataContext is AccountCardViewModel)
        {
            return;
        }

        if (Design.IsDesignMode)
        {
            return;
        }

        if (ServiceManager.Services != null)
        {
            var vm = ServiceManager.Services.GetService<AccountCardViewModel>()
                     ?? (ServiceManager.Services.GetService<IAccountService>() is { } accountService
                         ? new AccountCardViewModel(accountService)
                         : null);

            if (vm != null)
            {
                DataContext = vm;
            }
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (DataContext is not AccountCardViewModel)
        {
            EnsureDataContext();
        }
        base.OnAttachedToVisualTree(e);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext != null && DataContext is not AccountCardViewModel)
        {
            EnsureDataContext();
        }
    }
}
