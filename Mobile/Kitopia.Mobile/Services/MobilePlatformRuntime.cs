namespace Kitopia.Mobile.Services;

public static class MobilePlatformRuntime
{
    public static IMobilePlatformRuntimeFeatures Current { get; set; } = DefaultMobilePlatformRuntimeFeatures.Instance;
}
