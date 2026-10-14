namespace Kitopia.Mobile.Services;

public interface IMobilePlatformRuntimeFeatures
{
    string DefaultDeviceName { get; }
    string OperatingSystemName { get; }

    IMobileCommunicationRuntime WrapCommunicationRuntime(IMobileCommunicationRuntime innerRuntime);
}
