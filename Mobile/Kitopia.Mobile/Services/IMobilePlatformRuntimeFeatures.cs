namespace Kitopia.Mobile.Services;

public interface IMobilePlatformRuntimeFeatures
{
    IMobileCommunicationRuntime WrapCommunicationRuntime(IMobileCommunicationRuntime innerRuntime);
}
