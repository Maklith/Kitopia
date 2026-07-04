namespace Kitopia.DeviceCommunication.Identity;

public sealed record DeviceIdentity(
    string PublicKey,
    string PrivateKey,
    string IdHash);
