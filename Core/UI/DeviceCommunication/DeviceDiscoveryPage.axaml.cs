using Avalonia.Controls;
using Avalonia.Input;
using PluginCore;

namespace Core.UI.DeviceCommunication;

public partial class DeviceDiscoveryPage : UserControl
{
    public DeviceDiscoveryPage()
    {
        InitializeComponent();
    }

    public event Action<DeviceModel>? DeviceSelected;

    private void OnDeviceCardPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border { DataContext: DeviceModel device })
        {
            DeviceSelected?.Invoke(device);
        }
    }
}
