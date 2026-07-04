using Android.Content;
using Android.Net.Wifi;
using Kitopia.DeviceCommunication.Diagnostics;
using Kitopia.Mobile.Services;

namespace Kitopia.Mobile;

public sealed class AndroidPlatformRuntimeFeatures : IMobilePlatformRuntimeFeatures
{
    private const string LogCategory = "AndroidRuntime";

    public IMobileCommunicationRuntime WrapCommunicationRuntime(IMobileCommunicationRuntime innerRuntime)
    {
        return new AndroidMulticastRuntime(innerRuntime);
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
