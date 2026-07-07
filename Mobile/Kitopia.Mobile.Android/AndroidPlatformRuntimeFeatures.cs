using Android.Content;
using Android.Net.Wifi;
using Android.OS;
using Android.Provider;
using Kitopia.DeviceCommunication.Diagnostics;
using Kitopia.Mobile.Services;

namespace Kitopia.Mobile;

public sealed class AndroidPlatformRuntimeFeatures : IMobilePlatformRuntimeFeatures
{
    private const string LogCategory = "AndroidRuntime";

    public string DefaultDeviceName => ResolveDefaultDeviceName();

    public IMobileCommunicationRuntime WrapCommunicationRuntime(IMobileCommunicationRuntime innerRuntime)
    {
        return new AndroidMulticastRuntime(innerRuntime);
    }

    private static string ResolveDefaultDeviceName()
    {
        var configuredName = ReadGlobalSetting("device_name")
            ?? ReadGlobalSetting("bluetooth_name")
            ?? ReadSecureSetting("bluetooth_name")
            ?? ReadSystemSetting("bluetooth_name");
        if (!string.IsNullOrWhiteSpace(configuredName))
        {
            return configuredName.Trim();
        }

        var manufacturer = Build.Manufacturer?.Trim();
        var model = Build.Model?.Trim();
        if (string.IsNullOrWhiteSpace(manufacturer))
        {
            return string.IsNullOrWhiteSpace(model)
                ? DefaultMobilePlatformRuntimeFeatures.Instance.DefaultDeviceName
                : model;
        }

        if (string.IsNullOrWhiteSpace(model))
        {
            return manufacturer;
        }

        return model.StartsWith(manufacturer, StringComparison.OrdinalIgnoreCase)
            ? model
            : $"{manufacturer} {model}";
    }

    private static string? ReadGlobalSetting(string name)
    {
        try
        {
            var resolver = Android.App.Application.Context.ContentResolver;
            return resolver is null ? null : Settings.Global.GetString(resolver, name);
        }
        catch (Exception ex)
        {
            DeviceCommunicationDiagnostics.Warning(LogCategory, $"Failed to read Android global setting {name}: {ex.Message}");
            return null;
        }
    }

    private static string? ReadSecureSetting(string name)
    {
        try
        {
            var resolver = Android.App.Application.Context.ContentResolver;
            return resolver is null ? null : Settings.Secure.GetString(resolver, name);
        }
        catch (Exception ex)
        {
            DeviceCommunicationDiagnostics.Warning(LogCategory, $"Failed to read Android secure setting {name}: {ex.Message}");
            return null;
        }
    }

    private static string? ReadSystemSetting(string name)
    {
        try
        {
            var resolver = Android.App.Application.Context.ContentResolver;
            return resolver is null ? null : Settings.System.GetString(resolver, name);
        }
        catch (Exception ex)
        {
            DeviceCommunicationDiagnostics.Warning(LogCategory, $"Failed to read Android system setting {name}: {ex.Message}");
            return null;
        }
    }

    private sealed class AndroidMulticastRuntime : IMobileCommunicationRuntime
    {
        private readonly IMobileCommunicationRuntime _innerRuntime;
        private WifiManager.MulticastLock? _multicastLock;

        public AndroidMulticastRuntime(IMobileCommunicationRuntime innerRuntime)
        {
            _innerRuntime = innerRuntime;
        }

        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            EnsureMulticastLock();
            _multicastLock?.Acquire();
            DeviceCommunicationDiagnostics.Info(
                LogCategory,
                _multicastLock?.IsHeld == true
                    ? "Multicast lock acquired."
                    : "Multicast lock unavailable; discovery may be limited on this device.");
            await _innerRuntime.StartAsync(cancellationToken);
        }

        public async Task StopAsync()
        {
            try
            {
                await _innerRuntime.StopAsync();
            }
            finally
            {
                if (_multicastLock?.IsHeld == true)
                {
                    _multicastLock.Release();
                    DeviceCommunicationDiagnostics.Info(LogCategory, "Multicast lock released.");
                }
            }
        }

        private void EnsureMulticastLock()
        {
            if (_multicastLock is not null)
            {
                return;
            }

            var wifiManager = Android.App.Application.Context.GetSystemService(Context.WifiService) as WifiManager;
            if (wifiManager is null)
            {
                DeviceCommunicationDiagnostics.Warning(LogCategory, "WifiManager unavailable; cannot create multicast lock.");
                return;
            }

            var multicastLock = wifiManager?.CreateMulticastLock("kitopia-mobile-discovery");
            if (multicastLock is null)
            {
                DeviceCommunicationDiagnostics.Warning(LogCategory, "CreateMulticastLock returned null.");
                return;
            }

            multicastLock.SetReferenceCounted(false);
            _multicastLock = multicastLock;
            DeviceCommunicationDiagnostics.Info(LogCategory, "Prepared multicast lock.");
        }
    }
}
