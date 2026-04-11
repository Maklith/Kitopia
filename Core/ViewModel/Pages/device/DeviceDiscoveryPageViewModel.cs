using System;
using System.Collections.ObjectModel;
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
        var toastService = ServiceManager.Services.GetService<IToastService>();
        if (listener is null)
        {
            toastService?.Show("设备通信", "本地通信服务未初始化。", NotificationType.Error);
            return;
        }

        var protocol = device.SupportQuic && device.QuicPort > 0
            ? LocalDataTransportProtocol.Quic
            : LocalDataTransportProtocol.Udp;
        var targetPort = protocol == LocalDataTransportProtocol.Quic ? device.QuicPort : device.UdpPort;
        if (targetPort <= 0)
        {
            toastService?.Show("设备通信", "目标端口无效，无法测试连接。", NotificationType.Warning);
            return;
        }

        var payloadText = $"KITOPIA_TEST|{Environment.MachineName}|{DateTimeOffset.UtcNow:O}";
        var payload = Encoding.UTF8.GetBytes(payloadText);

        try
        {
            var endpoint = new IPEndPoint(device.Address, targetPort);
            await listener.SendAsync(protocol, payload, endpoint, device.Id);
            toastService?.Show("设备通信", $"测试消息已发送到 {device.DisplayName} ({protocol})。", NotificationType.Success);
        }
        catch (Exception ex) when (protocol == LocalDataTransportProtocol.Quic && device.UdpPort > 0)
        {
            var quicEndpoint = new IPEndPoint(device.Address, device.QuicPort);
            Logger.Warning(ex, "Device test message send failed over QUIC. DeviceId={DeviceId}, Address={Address}",
                device.Id, quicEndpoint);

            try
            {
                var udpEndpoint = new IPEndPoint(device.Address, device.UdpPort);
                await listener.SendAsync(LocalDataTransportProtocol.Udp, payload, udpEndpoint, device.Id);
                toastService?.Show("设备通信", $"QUIC失败，已通过 UDP 发送到 {device.DisplayName}。", NotificationType.Warning);
            }
            catch (Exception fallbackEx)
            {
                Logger.Warning(fallbackEx,
                    "Device test message fallback send failed over UDP. DeviceId={DeviceId}, Address={Address}",
                    device.Id, new IPEndPoint(device.Address, device.UdpPort));
                toastService?.Show("设备通信", $"测试连接失败: {fallbackEx.Message}", NotificationType.Error);
            }
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Device test message send failed. DeviceId={DeviceId}, Address={Address}, Protocol={Protocol}",
                device.Id, new IPEndPoint(device.Address, targetPort), protocol);
            toastService?.Show("设备通信", $"测试连接失败: {ex.Message}", NotificationType.Error);
        }
    }
}
