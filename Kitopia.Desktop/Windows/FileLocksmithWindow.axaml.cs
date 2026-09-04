using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Kitopia.Desktop.Abstractions.FileSystem;
using Kitopia.Desktop.Features.Services.Interfaces;
using Kitopia.Desktop.Features.ViewModel.Windows;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;

namespace Kitopia.Desktop.Windows;

public partial class FileLocksmithWindow : Window, IFileLocksmithWindow
{
    private static FileLocksmithWindow? _currentInstance;

    public FileLocksmithWindow()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private FileLocksmithWindowViewModel EnsureViewModel()
    {
        if (DataContext is not FileLocksmithWindowViewModel vm)
        {
            vm = new FileLocksmithWindowViewModel(ServiceManager.Services.GetRequiredService<IFileLockService>());
            DataContext = vm;
        }
        return vm;
    }

    public void ShowForScope(string? rootDir = null, IReadOnlyCollection<string>? targetPaths = null)
    {
        if (_currentInstance != null && _currentInstance != this && _currentInstance.IsVisible)
        {
            _currentInstance.ShowForScope(rootDir, targetPaths);
            if (_currentInstance.WindowState == WindowState.Minimized)
            {
                _currentInstance.WindowState = WindowState.Normal;
            }
            _currentInstance.Activate();
            return;
        }

        _currentInstance = this;
        var vm = EnsureViewModel();
        vm.InitializeScope(rootDir, targetPaths);

        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }
        Activate();
    }

    public void Show(List<FileLockInfo> processes)
    {
        if (_currentInstance != null && _currentInstance != this && _currentInstance.IsVisible)
        {
            _currentInstance.Show(processes);
            if (_currentInstance.WindowState == WindowState.Minimized)
            {
                _currentInstance.WindowState = WindowState.Normal;
            }
            _currentInstance.Activate();
            return;
        }

        _currentInstance = this;
        var vm = EnsureViewModel();
        vm.LoadProcesses(processes);

        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }
        Activate();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        if (_currentInstance == this)
        {
            _currentInstance = null;
        }

        if (DataContext is FileLocksmithWindowViewModel vm)
        {
            vm.OnWindowClosing();
        }
    }

    private async void PickFolder_Click(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择要监控的目录",
            AllowMultiple = false
        });

        if (folders != null && folders.Count > 0)
        {
            var path = folders[0].Path.LocalPath;
            if (!string.IsNullOrWhiteSpace(path))
            {
                var vm = EnsureViewModel();
                vm.SetMonitoredFolder(path);
            }
        }
    }

    private void ClearScope_Click(object? sender, RoutedEventArgs e)
    {
        var vm = EnsureViewModel();
        vm.InitializeScope(null, null);
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
