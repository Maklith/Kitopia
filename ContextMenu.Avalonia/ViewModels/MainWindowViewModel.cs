using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ContextMenu.Avalonia.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private const string AppName = "Kitopia";
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
            // 0. Resolve install path
            var settings = LoadSettings();
            KitopiaPath = GetAppRootPath();

            if (string.IsNullOrWhiteSpace(KitopiaPath))
            {
                StatusMessage = "未找到 Kitopia 安装位置，请手动设置。";
                return;
            }
            // install path search is removed; config path is fixed under AppData

            // 2. Locate Config
            var configPath = GetConfigPath();
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
    private void BrowsePath()
    {
        StatusMessage = $"配置路径已固定: {GetConfigPath()}";
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
                    ExternalConfigPath = GetConfigPath()
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

    private static string GetConfigPath()
    {
        var configsDirectory = Path.Combine(GetAppRootPath(), "configs");
        Directory.CreateDirectory(configsDirectory);
        return Path.Combine(configsDirectory, ConfigFileName);
    }

    private static string GetAppRootPath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            localAppData = AppDomain.CurrentDomain.BaseDirectory;
        }

        var root = Path.Combine(localAppData, AppName);
        Directory.CreateDirectory(root);
        return root;
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
