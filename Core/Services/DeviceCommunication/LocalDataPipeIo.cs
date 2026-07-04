using System.IO.Pipelines;
using SharedLocalDataPipeIo = Kitopia.DeviceCommunication.Transport.LocalDataPipeIo;

namespace Core.Services.DeviceCommunication;

internal static class LocalDataPipeIo
{
    public static Task<byte[]?> ReadExactlyOrEndAsync(
        PipeReader payloadReader,
        int byteCount,
        CancellationToken cancellationToken)
    {
        return SharedLocalDataPipeIo.ReadExactlyOrEndAsync(payloadReader, byteCount, cancellationToken);
    }

    public static Task<byte[]> ReadUpToAsync(
        PipeReader payloadReader,
        int maxByteCount,
        CancellationToken cancellationToken)
    {
        return SharedLocalDataPipeIo.ReadUpToAsync(payloadReader, maxByteCount, cancellationToken);
    }

    public static Task<byte[]> ReadExactlyAsync(
        PipeReader payloadReader,
        int byteCount,
        CancellationToken cancellationToken)
    {
        return SharedLocalDataPipeIo.ReadExactlyAsync(payloadReader, byteCount, cancellationToken);
    }

    public static Task CopyExactlyToStreamAsync(
        PipeReader payloadReader,
        Stream stream,
        int byteCount,
        CancellationToken cancellationToken)
    {
        return SharedLocalDataPipeIo.CopyExactlyToStreamAsync(payloadReader, stream, byteCount, cancellationToken);
    }

    public static Task DrainExactlyAsync(
        PipeReader payloadReader,
        int byteCount,
        CancellationToken cancellationToken)
    {
        return SharedLocalDataPipeIo.DrainExactlyAsync(payloadReader, byteCount, cancellationToken);
    }
}