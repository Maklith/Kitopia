using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Kitopia.Desktop.Features.Services.Interfaces;
using Kitopia.Desktop.Features.ViewModel.Windows;
using Kitopia.Desktop.Abstractions.FileSystem;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;

namespace Kitopia.Desktop.Windows;

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

    public void Show(List<FileLockInfo> processes)
    {
        
        var vm = DataContext as FileLocksmithWindowViewModel;
        if (vm == null)
        {
            DataContext = new FileLocksmithWindowViewModel(ServiceManager.Services.GetRequiredService<IFileLockService>());
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
