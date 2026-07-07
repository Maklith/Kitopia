namespace Kitopia.Mobile.Services;

public interface IMobilePlatformRuntimeFeatures
{
    string DefaultDeviceName { get; }

    IMobileCommunicationRuntime WrapCommunicationRuntime(IMobileCommunicationRuntime innerRuntime);
}
