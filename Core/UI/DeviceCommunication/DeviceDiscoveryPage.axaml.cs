using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Controls.Notifications;
using Core.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Kitopia.DeviceCommunication.Discovery;
using PluginCore;

namespace Core.UI.DeviceCommunication;

public partial class DeviceDiscoveryPage : UserControl
{
    public DeviceDiscoveryPage()
    {
        InitializeComponent();
    }

    public event Action<DiscoveredDevice>? DeviceSelected;

    private void OnDeviceCardPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border { DataContext: DiscoveredDevice device })
        {
            DeviceSelected?.Invoke(device);
        }
    }

    private async void OnEditCustomNameClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { DataContext: DiscoveredDevice device }) return;

        var parentWindow = ServiceManager.Services.GetService<IWindowTool>()?.GetForegroundWindow()
                           ?? TopLevel.GetTopLevel(this) as Window;

        if (parentWindow is not null)
        {
            await ShowEditDialog(parentWindow, device);
        }
        else
        {
            var toast = ServiceManager.Services.GetService<IToastService>()!;
            toast.Show("修改备注名", string.IsNullOrEmpty(device.CustomName) ? "未设置" : device.CustomName, NotificationType.Information);
        }
    }

    private async Task ShowEditDialog(Window parentWindow, DiscoveredDevice device)
    {
        var textBox = new TextBox { Watermark = "备注名", Text = device.CustomName };
        string? result = null;
        bool confirmed = false;

        var okButton = new Button { Content = "确定" };
        var cancelButton = new Button { Content = "取消" };

        var dialog = new Window
        {
            Title = "修改备注名",
            Width = 320,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(16),
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = "请输入新的备注名:", FontSize = 14 },
                    textBox,
                    new StackPanel
                    {
                        Orientation = Avalonia.Layout.Orientation.Horizontal,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                        Spacing = 8,
                        Children = { cancelButton, okButton }
                    }
                }
            }
        };

        okButton.Click += (_, _) => { result = textBox.Text; confirmed = true; dialog.Close(); };
        cancelButton.Click += (_, _) => dialog.Close();

        await dialog.ShowDialog(parentWindow);

        if (confirmed && result is not null)
        {
            device.CustomName = result;
            if (DataContext is ViewModel.Pages.device.DeviceDiscoveryPageViewModel vm)
            {
                vm.SaveCustomNameCommand.Execute(device);
            }
        }
    }
}
