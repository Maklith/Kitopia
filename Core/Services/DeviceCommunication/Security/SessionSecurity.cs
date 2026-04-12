using System.Buffers;

namespace Core.Services.DeviceCommunication.Security;

public sealed class SessionSecurity : ISessionSecurity
{
    public ValueTask<SecurityContext> AuthenticateAsync(
        string remoteIdentityPublicKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(remoteIdentityPublicKey))
        {
            throw new InvalidOperationException("Remote identity public key is required.");
        }

        return ValueTask.FromResult(new SecurityContext(Guid.NewGuid().ToString("N"), remoteIdentityPublicKey));
    }

    public ValueTask ProtectAsync(
        SecurityContext context,
        ReadOnlyMemory<byte> framePayload,
        IBufferWriter<byte> output,
        CancellationToken cancellationToken = default)
    {
        framePayload.Span.CopyTo(output.GetSpan(framePayload.Length));
        output.Advance(framePayload.Length);
        return ValueTask.CompletedTask;
    }

    public ValueTask UnprotectAsync(
        SecurityContext context,
        ReadOnlyMemory<byte> framePayload,
        IBufferWriter<byte> output,
        CancellationToken cancellationToken = default)
    {
        framePayload.Span.CopyTo(output.GetSpan(framePayload.Length));
        output.Advance(framePayload.Length);
        return ValueTask.CompletedTask;
    }
}
