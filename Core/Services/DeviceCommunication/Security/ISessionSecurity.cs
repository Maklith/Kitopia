using System.Buffers;

namespace Core.Services.DeviceCommunication.Security;

public interface ISessionSecurity
{
    ValueTask<SecurityContext> AuthenticateAsync(
        string remoteIdentityPublicKey,
        CancellationToken cancellationToken = default);

    ValueTask ProtectAsync(
        SecurityContext context,
        ReadOnlyMemory<byte> framePayload,
        IBufferWriter<byte> output,
        CancellationToken cancellationToken = default);

    ValueTask UnprotectAsync(
        SecurityContext context,
        ReadOnlyMemory<byte> framePayload,
        IBufferWriter<byte> output,
        CancellationToken cancellationToken = default);
}
