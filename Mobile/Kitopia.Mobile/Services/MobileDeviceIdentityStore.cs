using System.Text.Json;
using Kitopia.DeviceCommunication.Discovery;
using Kitopia.DeviceCommunication.Identity;

namespace Kitopia.Mobile.Services;

public sealed class MobileDeviceIdentityStore : IDeviceIdentityStore
{
    private readonly string _identityFilePath;
    private readonly object _sync = new();

    public MobileDeviceIdentityStore()
    {
        _identityFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Kitopia.Mobile",
            "device-identity.json");
    }

    public bool TryGetIdentity(out DeviceIdentity identity)
    {
        lock (_sync)
        {
            if (!TryReadIdentityFile(out var payload) ||
                !DeviceDiscoverySignature.TryDerivePublicKey(payload.PrivateKey, out var publicKey))
            {
                identity = default!;
                return false;
            }

            identity = new DeviceIdentity(
                publicKey,
                payload.PrivateKey,
                DeviceDiscoverySignature.ComputePublicKeyHash(publicKey));
            return true;
        }
    }

    public DeviceIdentity EnsureIdentity()
    {
        lock (_sync)
        {
            if (TryGetIdentity(out var existing))
            {
                return existing;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(_identityFilePath)!);
            var keyPair = DeviceDiscoverySignature.CreateKeyPair();
            var payload = new IdentityFilePayload { PrivateKey = keyPair.PrivateKey };
            File.WriteAllText(_identityFilePath, JsonSerializer.Serialize(payload));

            return new DeviceIdentity(
                keyPair.PublicKey,
                keyPair.PrivateKey,
                DeviceDiscoverySignature.ComputePublicKeyHash(keyPair.PublicKey));
        }
    }

    private bool TryReadIdentityFile(out IdentityFilePayload payload)
    {
        payload = new IdentityFilePayload();
        if (!File.Exists(_identityFilePath))
        {
            return false;
        }

        try
        {
            payload = JsonSerializer.Deserialize<IdentityFilePayload>(File.ReadAllText(_identityFilePath)) ?? new IdentityFilePayload();
            return !string.IsNullOrWhiteSpace(payload.PrivateKey);
        }
        catch
        {
            return false;
        }
    }

    private sealed class IdentityFilePayload
    {
        public string PrivateKey { get; set; } = string.Empty;
    }
}
