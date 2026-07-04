namespace Kitopia.Mobile.Services;

public sealed class DefaultMobilePlatformRuntimeFeatures : IMobilePlatformRuntimeFeatures
{
    public static DefaultMobilePlatformRuntimeFeatures Instance { get; } = new();

    private DefaultMobilePlatformRuntimeFeatures()
    {
    }

    public IMobileCommunicationRuntime WrapCommunicationRuntime(IMobileCommunicationRuntime innerRuntime)
    {
        return innerRuntime;
    }
}
