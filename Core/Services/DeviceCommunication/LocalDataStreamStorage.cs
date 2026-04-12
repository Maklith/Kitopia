using System.IO.Pipelines;
using System.Net;
using System.Text;

namespace Core.Services.DeviceCommunication;

internal static class LocalDataStreamStorage
{
    public static async Task SaveLegacyPayloadAsync(
        LocalDataTransportProtocol protocol,
        IPEndPoint remoteEndPoint,
        byte[] prefixPayload,
        PipeReader payloadReader,
        CancellationToken cancellationToken)
    {
        var filePath = BuildLegacyFilePath(protocol, remoteEndPoint);
        await using var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.Read);
        await stream.WriteAsync(prefixPayload, cancellationToken);
        await payloadReader.CopyToAsync(stream, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    public static Task SaveMessageAsync(
        LocalDataTransportProtocol protocol,
        IPEndPoint remoteEndPoint,
        string message,
        CancellationToken cancellationToken)
    {
        var filePath = BuildMessageFilePath(protocol, remoteEndPoint);
        return File.WriteAllTextAsync(filePath, message, Encoding.UTF8, cancellationToken);
    }

    public static Task SaveCommandJsonAsync(
        LocalDataTransportProtocol protocol,
        IPEndPoint remoteEndPoint,
        string json,
        CancellationToken cancellationToken)
    {
        var filePath = BuildCommandFilePath(protocol, remoteEndPoint);
        return File.WriteAllTextAsync(filePath, json, Encoding.UTF8, cancellationToken);
    }

    public static async Task SavePayloadToFileAsync(
        PipeReader payloadReader,
        int payloadLength,
        string filePath,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.Read);
        await LocalDataPipeIo.CopyExactlyToStreamAsync(payloadReader, stream, payloadLength, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    public static string BuildLegacyFilePath(LocalDataTransportProtocol protocol, IPEndPoint remoteEndPoint)
    {
        var remoteAddress = remoteEndPoint.Address.ToString().Replace(':', '_');
        var fileName = $"recv-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{protocol}-{remoteAddress}-{remoteEndPoint.Port}.bin";
        return Path.Combine(Core.Utils.KitopiaPaths.ReceivedFilesDirectory, fileName);
    }

    public static string BuildTransferFilePath(
        LocalDataTransportProtocol protocol,
        IPEndPoint remoteEndPoint,
        Guid channelId,
        string? targetFileName)
    {
        var remoteAddress = remoteEndPoint.Address.ToString().Replace(':', '_');
        var fileName = string.IsNullOrWhiteSpace(targetFileName)
            ? $"transfer-{channelId:N}.bin"
            : SanitizeFileName(targetFileName);
        var file = $"{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{protocol}-{remoteAddress}-{remoteEndPoint.Port}-{fileName}";
        return Path.Combine(Core.Utils.KitopiaPaths.ReceivedFilesDirectory, file);
    }

    public static string BuildMessageFilePath(LocalDataTransportProtocol protocol, IPEndPoint remoteEndPoint)
    {
        var remoteAddress = remoteEndPoint.Address.ToString().Replace(':', '_');
        var fileName = $"message-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{protocol}-{remoteAddress}-{remoteEndPoint.Port}.txt";
        return Path.Combine(Core.Utils.KitopiaPaths.ReceivedFilesDirectory, fileName);
    }

    public static string BuildCommandFilePath(LocalDataTransportProtocol protocol, IPEndPoint remoteEndPoint)
    {
        var remoteAddress = remoteEndPoint.Address.ToString().Replace(':', '_');
        var fileName = $"command-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{protocol}-{remoteAddress}-{remoteEndPoint.Port}.json";
        return Path.Combine(Core.Utils.KitopiaPaths.ReceivedFilesDirectory, fileName);
    }

    public static string BuildMessagePayloadFilePath(LocalDataTransportProtocol protocol, IPEndPoint remoteEndPoint)
    {
        var remoteAddress = remoteEndPoint.Address.ToString().Replace(':', '_');
        var fileName = $"message-payload-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{protocol}-{remoteAddress}-{remoteEndPoint.Port}.bin";
        return Path.Combine(Core.Utils.KitopiaPaths.ReceivedFilesDirectory, fileName);
    }

    public static string BuildCommandPayloadFilePath(LocalDataTransportProtocol protocol, IPEndPoint remoteEndPoint)
    {
        var remoteAddress = remoteEndPoint.Address.ToString().Replace(':', '_');
        var fileName = $"command-payload-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{protocol}-{remoteAddress}-{remoteEndPoint.Port}.bin";
        return Path.Combine(Core.Utils.KitopiaPaths.ReceivedFilesDirectory, fileName);
    }

    private static string SanitizeFileName(string fileName)
    {
        var safeName = Path.GetFileName(fileName);
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            safeName = safeName.Replace(invalid, '_');
        }

        return string.IsNullOrWhiteSpace(safeName) ? "unnamed.bin" : safeName;
    }
}
