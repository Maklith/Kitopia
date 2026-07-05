using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Core.Services.DeviceCommunication;
using Core.Services.DeviceCommunication.Application;
using Kitopia.DeviceCommunication.Discovery;
using Core.Services.Interfaces;
using Core.ViewModel.Windows;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;

namespace KitopiaAvalonia.Windows;

public partial class LanFileShareWindow : Window, ILanFileShareWindow
{
    public LanFileShareWindow()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public void Show(IReadOnlyCollection<string> filePaths)
    {
        if (DataContext is not LanFileShareWindowViewModel vm)
        {
            var deviceDiscoveryService = ServiceManager.Services.GetService<IDeviceDiscoveryService>();
            var messageAppService = ServiceManager.Services.GetService<IMessageAppService>();
            var toastService = ServiceManager.Services.GetService<IToastService>();
            if (deviceDiscoveryService is null || messageAppService is null || toastService is null)
            {
                return;
            }

            vm = new LanFileShareWindowViewModel(deviceDiscoveryService, messageAppService, toastService);
            DataContext = vm;
        }

        vm.SetSelectedFiles(filePaths);
        base.Show();
        Activate();
    }

    protected override void OnClosed(EventArgs e)
    {
        if (DataContext is IDisposable disposable)
        {
            disposable.Dispose();
        }

        base.OnClosed(e);
    }
}
