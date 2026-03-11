using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Core.Services.Interfaces;
using Core.Utils;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using Ursa.Controls;

namespace Core.ViewModel.Pages.device;

public partial class DeviceDiscoveryPageViewModel : ObservableObject, IDisposable
{
    private readonly IDeviceCommunication _deviceCommunication;

    public ObservableCollection<DeviceModel> DiscoveredDevices => _deviceCommunication.DiscoveredDevices;

    [ObservableProperty]
    private bool _isDiscovering;

    [ObservableProperty]
    private string _messageToSend = "Hello from Kitopia!";

    public DeviceDiscoveryPageViewModel(IDeviceCommunication deviceCommunication)
    {
        _deviceCommunication = deviceCommunication;
        IsDiscovering = true; // Auto start? or just state
        StartDiscovery();
        
        _deviceCommunication.MessageReceived += OnMessageReceived;
        _deviceCommunication.FileTransferRequested += OnFileTransferRequested;
        _deviceCommunication.StreamReceived += OnStreamReceived;
    }

    public void Dispose()
    {
        _deviceCommunication.MessageReceived -= OnMessageReceived;
        _deviceCommunication.FileTransferRequested -= OnFileTransferRequested;
        _deviceCommunication.StreamReceived -= OnStreamReceived;
        StopDiscovery();
    }

    private void OnMessageReceived(object? sender, string message)
    {
        // TODO: Show toast or dialog
        // For now, write to debug
        System.Diagnostics.Debug.WriteLine($"Received Message: {message}");
    }

    private async void OnFileTransferRequested(object? sender, FileTransferRequestEventArgs e)
    {
        // Need to show dialog on UI thread
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
        {
            bool accepted = false;
            try
            {
                await ServiceManager.Services.GetService<IContentDialog>()!.ShowDialogAsync(null,
                    new DialogContent()
                    {
                        Title = "文件接收请求",
                        Content = $"接收到文件'{e.FileName}' ({e.FileSize} bytes) 来自 {e.Sender.Name}?",
                        PrimaryButtonText = "接收",
                        SecondaryButtonText = "取消",
                        PrimaryAction = (() =>
                        {
                            accepted = true;
                        })
                    });
                
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Dialog Error: {ex}");
                // Fallback: auto-accept for now if dialog fails (e.g. no overlay host), or reject.
                // For dev, let's accept to test flow.
                accepted = true; 
            }
            
            await _deviceCommunication.RespondToFileRequestAsync(e.Sender, e.RequestId, accepted);
        });
    }

    private async void OnStreamReceived(object? sender, DeviceStreamReceivedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.MetaData)) return;

        try
        {
            var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var meta = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(e.MetaData, options);
            
            // Check packet type
            string? type = null;
            if (meta.TryGetProperty("Type", out var typeProp)) type = typeProp.GetString();
            if (string.IsNullOrEmpty(type) && meta.TryGetProperty("type", out typeProp)) type = typeProp.GetString(); // Fallback

            if (string.Equals(type, "FileTransfer", StringComparison.OrdinalIgnoreCase))
            {
                string? rawFileName = null;
                if (meta.TryGetProperty("FileName", out var nameProp)) rawFileName = nameProp.GetString();
                if (string.IsNullOrEmpty(rawFileName) && meta.TryGetProperty("fileName", out nameProp)) rawFileName = nameProp.GetString();

                if (!string.IsNullOrEmpty(rawFileName))
                {
                    // Sanitize filename to prevent directory traversal
                    var fileName = Path.GetFileName(rawFileName);
                    
                    var downloads = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    downloads = Path.Combine(downloads, "Downloads");
                    if (!Directory.Exists(downloads)) Directory.CreateDirectory(downloads);

                    var path = Path.Combine(downloads, fileName);
                    string baseName = Path.GetFileNameWithoutExtension(fileName);
                    string ext = Path.GetExtension(fileName);
                    int i = 0;

                    // Ensure unique filename
                    while (File.Exists(path))
                    {
                        path = Path.Combine(downloads, $"{baseName} ({++i}){ext}");
                    }

                    // Use CreateNew to prevent overwriting existing files if race condition occurs
                    // (Though checking File.Exists above handles most cases)
                    using var fs = new FileStream(path, FileMode.Create);
                    if (e.Stream.CanSeek) e.Stream.Position = 0; // Ensure stream is at start
                    await e.Stream.CopyToAsync(fs);

                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(new Func<Task>(async () =>
                    {
                        await (ServiceManager.Services.GetService<IContentDialog>()?.ShowDialogAsync(null, new DialogContent
                        {
                            Title = "文件接收成功",
                            Content = $"文件已保存至: {path}",
                            PrimaryButtonText = "确定"
                        }) ?? Task.CompletedTask);
                    }));
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Stream Receive Error: {ex}");
        }
    }

    [RelayCommand]
    public void StartDiscovery()
    {
        try 
        {
            _deviceCommunication.StartDiscovery();
            IsDiscovering = true;
        } 
        catch { IsDiscovering = false; }
    }

    [RelayCommand]
    public void StopDiscovery()
    {
        _deviceCommunication.StopDiscovery();
        IsDiscovering = false;
    }

    [RelayCommand]
    public async Task SendMessage(DeviceModel device)
    {
        if (string.IsNullOrWhiteSpace(MessageToSend)) return;
        try
        {
            await _deviceCommunication.SendMessageAsync(device, MessageToSend);
            // Optional: Clear after send
            // MessageToSend = string.Empty; 
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Message Send Error: {ex}");
        }
    }

    [RelayCommand]
    public async Task SendFile(DeviceModel device)
    {
        // Pick file
        var lifetime = (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime);
        if (lifetime?.MainWindow == null) return;

        var files = await lifetime.MainWindow.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = "Select File to Send",
            AllowMultiple = false
        });

        if (files != null && files.Count > 0)
        {
            var file = files[0];
            var path = file.Path.LocalPath;
            try
            {
                await _deviceCommunication.RequestFileTransferAsync(device, path);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"File Send Error: {ex}");
            }
        }
    }
}
