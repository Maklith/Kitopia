using Avalonia;
using Avalonia.Controls;
using Core.UI.DeviceCommunication;
using Core.ViewModel.Pages.device;
using Kitopia.DeviceCommunication.Discovery;
using Kitopia.Mobile.Services;

namespace Kitopia.Mobile.Views;

public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();
    }

    public MobileTopLevelContext? TopLevelContext { get; set; }
    public DeviceDiscoveryPageViewModel? DiscoveryPageViewModel { get; set; }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (TopLevelContext is not null)
        {
            TopLevelContext.CurrentTopLevel = TopLevel.GetTopLevel(this);
        }

        WireDevicePage();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        var topLevelContext = TopLevelContext;
        if (topLevelContext is not null && topLevelContext.CurrentTopLevel == TopLevel.GetTopLevel(this))
        {
            topLevelContext.CurrentTopLevel = null;
        }

        UnwireDevicePage();
        base.OnDetachedFromVisualTree(e);
    }

    private void WireDevicePage()
    {
        var page = DevicePage;
        if (page is null) return;

        page.DataContext = DiscoveryPageViewModel;
        page.DeviceSelected += OnDevicePageSelected;
    }

    private void UnwireDevicePage()
    {
        var page = DevicePage;
        if (page is null) return;

        page.DeviceSelected -= OnDevicePageSelected;
    }

    private void OnDevicePageSelected(DiscoveredDevice device)
    {
        if (DataContext is ViewModels.MainViewModel vm)
        {
            vm.DeviceList.SelectedDevice = device;
        }
    }
}
