using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Core.Services.Interfaces;
using System.Collections.Generic;
using Core.ViewModel.Windows;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;

namespace KitopiaAvalonia.Windows;

public partial class FileLocksmithWindow : Window, IFileLocksmithWindow
{
    public FileLocksmithWindow()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public void Show(List<LockingProcessInfo> processes)
    {
        
        var vm = DataContext as FileLocksmithWindowViewModel;
        if (vm == null)
        {
            DataContext=new FileLocksmithWindowViewModel(ServiceManager.Services.GetService<IFileLocksmith>()!);
            vm = DataContext as FileLocksmithWindowViewModel;
        }
        
        vm?.LoadProcesses(processes);
        this.Show();
        this.Activate();
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
