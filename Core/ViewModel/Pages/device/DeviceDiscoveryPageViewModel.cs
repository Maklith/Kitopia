using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls.Notifications;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Core.Services;
using Core.Services.Config;
using Core.Services.DeviceCommunication;
using Core.Services.DeviceCommunication.Discovery;
using Core.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using Serilog;
using Serilog.Core;

namespace Core.ViewModel.Pages.device;

public partial class DeviceDiscoveryPageViewModel : ObservableObject
{
    private const string LargeTestFileSizeMbEnv = "KITOPIA_TEST_FILE_SIZE_MB";
    private const long DefaultLargeTestFileSizeMb = 2048;

    private static readonly ILogger Logger = LogManager.Logger.ForContext<DeviceDiscoveryPageViewModel>();
    private readonly IDeviceDiscoveryService _deviceDiscoveryService;

    public ObservableCollection<DeviceModel> DiscoveredDevices => _deviceDiscoveryService.Devices;

    public DeviceDiscoveryPageViewModel(IDeviceDiscoveryService deviceDiscoveryService)
    {
        _deviceDiscoveryService = deviceDiscoveryService;
    }

    [RelayCommand]
    private void SaveCustomName(DeviceModel? device)
    {
        if (device is null || string.IsNullOrWhiteSpace(device.Id))
        {
            return;
        }

        var name = device.CustomName?.Trim() ?? string.Empty;
        device.CustomName = string.IsNullOrEmpty(name) ? string.Empty : name;
        ConfigManger.Config.deviceCustomNames[device.Id] = device.CustomName;
        ConfigManger.Save("KitopiaConfig");
    }

    [RelayCommand]
    private void ClearCustomName(DeviceModel? device)
    {
        if (device is null || string.IsNullOrWhiteSpace(device.Id))
        {
            return;
        }

        device.CustomName = string.Empty;
        ConfigManger.Config.deviceCustomNames.Remove(device.Id);
        ConfigManger.Save("KitopiaConfig");
    }

    [RelayCommand]
    private async Task TestConnection(DeviceModel? device)
    {
        if (device is null || string.IsNullOrWhiteSpace(device.Id))
        {
            return;
        }

        var listener = ServiceManager.Services.GetService<ILocalDataListener>();
        var streamControl = ServiceManager.Services.GetService<ILocalDataStreamControl>();
        var toastService = ServiceManager.Services.GetService<IToastService>();
        if (listener is null || streamControl is null)
        {
            toastService?.Show("设备通信", "本地通信服务未初始化", NotificationType.Error);
            return;
        }

        var protocol = device.SupportQuic && device.QuicPort > 0
            ? LocalDataTransportProtocol.Quic
            : LocalDataTransportProtocol.Tcp;
        var targetPort = protocol == LocalDataTransportProtocol.Quic ? device.QuicPort : device.TcpPort;
        if (targetPort <= 0)
        {
            toastService?.Show("设备通信", "目标端口无效，无法测试连接", NotificationType.Warning);
            return;
        }

        var payloadText = $"KITOPIA_FILE_TEST|{Environment.MachineName}|{DateTimeOffset.UtcNow:O}";
        var testFileName = $"kitopia-large-connection-test-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}.bin";
        var testFilePath = Path.Combine(Path.GetTempPath(), testFileName);
        var testFileSizeBytes = ResolveLargeTestFileSizeBytes();

        try
        {
            await CreateLargeTestFileAsync(testFilePath, payloadText, testFileSizeBytes);
            var endpoint = new IPEndPoint(device.Address, targetPort);
            var sendContext = new LocalDataSendContext(listener, protocol, endpoint, device.Id);
            await using (var fileStream = File.OpenRead(testFilePath))
            {
                await streamControl.SendFileAsync(
                    sendContext,
                    fileStream,
                    testFileName);
            }

            toastService?.Show("设备通信", $"测试文件已发送到 {device.DisplayName} ({protocol})", NotificationType.Success);
        }
        catch (Exception ex) when (protocol == LocalDataTransportProtocol.Quic && device.TcpPort > 0)
        {
            var quicEndpoint = new IPEndPoint(device.Address, device.QuicPort);
            Logger.Warning(ex, "Device test file send failed over QUIC. DeviceId={DeviceId}, Address={Address}",
                device.Id, quicEndpoint);

            try
            {
                var tcpEndpoint = new IPEndPoint(device.Address, device.TcpPort);
                var sendContext = new LocalDataSendContext(listener, LocalDataTransportProtocol.Tcp, tcpEndpoint,
                    device.Id);
                await using (var fileStream = File.OpenRead(testFilePath))
                {
                    await streamControl.SendFileAsync(
                        sendContext,
                        fileStream,
                        testFileName);
                }

                toastService?.Show("设备通信", $"QUIC失败，已通过 TCP 发送测试文件到 {device.DisplayName}", NotificationType.Warning);
            }
            catch (Exception fallbackEx)
            {
                Logger.Warning(fallbackEx,
                    "Device test file fallback send failed over TCP. DeviceId={DeviceId}, Address={Address}",
                    device.Id, new IPEndPoint(device.Address, device.TcpPort));
                toastService?.Show("设备通信", $"测试连接失败: {fallbackEx.Message}", NotificationType.Error);
            }
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Device test file send failed. DeviceId={DeviceId}, Address={Address}, Protocol={Protocol}",
                device.Id, new IPEndPoint(device.Address, targetPort), protocol);
            toastService?.Show("设备通信", $"测试连接失败: {ex.Message}", NotificationType.Error);
        }
        finally
        {
            try
            {
                if (File.Exists(testFilePath))
                {
                    File.Delete(testFilePath);
                }
            }
            catch (Exception cleanupEx)
            {
                Logger.Debug(cleanupEx, "Cleanup test file failed. Path={Path}", testFilePath);
            }
        }
    }

    private static long ResolveLargeTestFileSizeBytes()
    {
        try
        {
            var text = Environment.GetEnvironmentVariable(LargeTestFileSizeMbEnv);
            if (long.TryParse(text, out var mb) && mb > 0)
            {
                return checked(mb * 1024L * 1024L);
            }
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Resolve large test file size from env failed. Env={Env}", LargeTestFileSizeMbEnv);
        }

        return DefaultLargeTestFileSizeMb * 1024L * 1024L;
    }

    private static async Task CreateLargeTestFileAsync(string filePath, string marker, long targetSizeBytes)
    {
        var markerBytes = Encoding.UTF8.GetBytes(marker + Environment.NewLine);
        await using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        await fileStream.WriteAsync(markerBytes);
        if (fileStream.Length < targetSizeBytes)
        {
            fileStream.SetLength(targetSizeBytes);
        }

        await fileStream.FlushAsync();
    }
}


