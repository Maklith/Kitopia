using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Windows.Storage;

namespace ContextMenu.Avalonia.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private const string SettingsFileName = "ContextMenuSettings.json";
    private const string ConfigFileName = "KitopiaContextMenu.json";

    [ObservableProperty] private string _statusMessage = "初始化中...";
    [ObservableProperty] private string _kitopiaPath = string.Empty;
    
    public ObservableCollection<ContextMenuItemViewModel> Items { get; } = new();

    public MainWindowViewModel()
    {
        InitializeAsync();
    }

    private async void InitializeAsync()
    {
        await LoadDataAsync();
    }

    private async Task LoadDataAsync()
    {
        try
        {
            // 0. Check for Saved Manual Path
            var settings = LoadSettings();
            string? installPath = null;
            
            if (!string.IsNullOrEmpty(settings.ExternalConfigPath))
            {
                // Try to infer install path from config path (..\configs\file.json)
                var configDir = Path.GetDirectoryName(settings.ExternalConfigPath);
                if (configDir != null)
                {
                    var inferredPath = Path.GetDirectoryName(configDir); // Parent of configs
                    if (inferredPath != null && Directory.Exists(inferredPath))
                    {
                        installPath = inferredPath;
                    }
                }
            }

            // 1. If no manual path, Find Kitopia Path from Registry
            if (string.IsNullOrEmpty(installPath))
            {
                installPath = FindKitopiaInstallPath();
            }

            if (string.IsNullOrEmpty(installPath))
            {
                StatusMessage = "未找到 Kitopia 安装位置，请手动设置。";
                return;
            }
            KitopiaPath = installPath;

            // 2. Locate Config
            var configPath = Path.Combine(installPath, "configs", ConfigFileName);
            if (!File.Exists(configPath))
            {
                StatusMessage = $"未找到配置文件: {configPath}";
                return;
            }

            // 3. Load Config Items
            var json = await File.ReadAllTextAsync(configPath);
            var config = JsonSerializer.Deserialize<ContextMenuConfigModel>(json);
            
            if (config?.Items == null)
            {
                StatusMessage = "解析配置文件失败。";
                return;
            }

            Items.Clear();
            foreach (var item in config.Items)
            {
                bool isVisible = true;
                if (settings.Visibility.TryGetValue(item.Title, out bool savedVis))
                {
                    isVisible = savedVis;
                }
                
                var vm = new ContextMenuItemViewModel(item, isVisible);
                vm.PropertyChanged += (s, e) => 
                {
                    if (e.PropertyName == nameof(ContextMenuItemViewModel.IsVisible))
                    {
                        SaveSettings();
                    }
                };
                Items.Add(vm);
            }
            
            // 5. Save the path to settings immediately so DLL can find it
            if (settings.ExternalConfigPath != configPath)
            {
                settings.ExternalConfigPath = configPath;
                SaveSettings(settings);
            }

            StatusMessage = "加载成功。";
        }
        catch (Exception ex)
        {
            StatusMessage = $"错误: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task BrowsePath()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var folders = await desktop.MainWindow.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "选择 Kitopia 安装文件夹",
                AllowMultiple = false
            });

            if (folders.Count > 0)
            {
                var path = folders[0].Path.LocalPath;
                
                // Verify it looks like Kitopia (check for executable or configs folder)
                if (File.Exists(Path.Combine(path, "Kitopia.StoreCompanion.exe")) || 
                    File.Exists(Path.Combine(path, "KitopiaAvalonia.exe")) ||
                    Directory.Exists(Path.Combine(path, "configs")))
                {
                    KitopiaPath = path;
                    
                    // Update settings with new config path
                    var settings = LoadSettings();
                    settings.ExternalConfigPath = Path.Combine(KitopiaPath, "configs", ConfigFileName);
                    SaveSettings(settings);
                    
                    // Reload
                    await LoadDataAsync();
                }
                else
                {
                    StatusMessage = "选择的文件夹似乎不是有效的 Kitopia 安装目录。";
                }
            }
        }
    }

    private string? FindKitopiaInstallPath()
    {
        try
        {
            // Check HKLM and HKCU Uninstall keys
            string[] roots = { 
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", 
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall" 
            };
            
            foreach (var root in roots)
            {
                using var key = Registry.LocalMachine.OpenSubKey(root);
                if (key != null) 
                {
                    SearchRegistryKey(key, out var path);
                    if (!string.IsNullOrEmpty(path)) return path;
                }
            }
            
            // Try HKCU
             using var cuKey = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
             if (cuKey != null) 
             {
                 SearchRegistryKey(cuKey, out var path);
                 if (!string.IsNullOrEmpty(path)) return path;
             }

        }
        catch { }
        return null;
        
        void SearchRegistryKey(RegistryKey root, out string? foundPath)
        {
            foundPath = null;
            foreach (var subKeyName in root.GetSubKeyNames())
            {
                using var subKey = root.OpenSubKey(subKeyName);
                if (subKey == null) continue;
                
                var displayName = subKey.GetValue("DisplayName") as string;
                if (displayName != null && displayName.Contains("Kitopia") && !displayName.Contains("Packing")) 
                {
                    var installLocation = subKey.GetValue("InstallLocation") as string;
                    if (string.IsNullOrEmpty(installLocation))
                    {
                        installLocation = subKey.GetValue("Path") as string; // ModernInstaller uses "Path"
                    }
                    
                    if (!string.IsNullOrEmpty(installLocation) && Directory.Exists(installLocation))
                    {
                        foundPath = installLocation;
                        return;
                    }
                }
            }
        }
    }

    private ContextMenuSettings LoadSettings()
    {
        try
        {
            var path = GetSettingsPath();
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<ContextMenuSettings>(json) ?? new ContextMenuSettings();
            }
        }
        catch { }
        return new ContextMenuSettings();
    }

    private void SaveSettings(ContextMenuSettings? settings = null)
    {
        try
        {
            if (settings == null)
            {
                settings = new ContextMenuSettings
                {
                    ExternalConfigPath = Path.Combine(KitopiaPath, "configs", ConfigFileName)
                };
                foreach (var item in Items)
                {
                    settings.Visibility[item.Title] = item.IsVisible;
                }
            }

            var path = GetSettingsPath();
            var dir = Path.GetDirectoryName(path);
            if (dir != null && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            StatusMessage = $"保存设置失败: {ex.Message}";
        }
    }

    private string GetSettingsPath()
    {
        // Try Package LocalState first
        try
        {
             return Path.Combine(ApplicationData.Current.LocalFolder.Path, SettingsFileName);
        }
        catch
        {
            // Fallback for unpackaged debug
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, SettingsFileName);
        }
    }
}

public partial class ContextMenuItemViewModel : ObservableObject
{
    public string Title { get; }
    public string Command { get; }
    
    [ObservableProperty] private bool _isVisible;

    public ContextMenuItemViewModel(ContextMenuItem item, bool isVisible)
    {
        Title = item.Title;
        Command = item.Command;
        IsVisible = isVisible;
    }
}

public class ContextMenuConfigModel
{
    public List<ContextMenuItem> Items { get; set; } = new();
}

public class ContextMenuItem
{
    public string Title { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;
}

public class ContextMenuSettings
{
    public string ExternalConfigPath { get; set; } = string.Empty;
    public Dictionary<string, bool> Visibility { get; set; } = new();
}
