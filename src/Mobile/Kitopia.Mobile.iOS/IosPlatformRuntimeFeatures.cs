using Kitopia.Mobile.Services;
using UIKit;

namespace Kitopia.Mobile;

public sealed class IosPlatformRuntimeFeatures : IMobilePlatformRuntimeFeatures
{
    public string DefaultDeviceName => ResolveDefaultDeviceName();
    public string OperatingSystemName => "iOS";

    public IMobileCommunicationRuntime WrapCommunicationRuntime(IMobileCommunicationRuntime innerRuntime)
    {
        return innerRuntime;
    }

    private static string ResolveDefaultDeviceName()
    {
        var device = UIDevice.CurrentDevice;
        if (!string.IsNullOrWhiteSpace(device.Name))
        {
            return device.Name.Trim();
        }

        return string.IsNullOrWhiteSpace(device.Model)
            ? DefaultMobilePlatformRuntimeFeatures.Instance.DefaultDeviceName
            : device.Model.Trim();
    }
}
