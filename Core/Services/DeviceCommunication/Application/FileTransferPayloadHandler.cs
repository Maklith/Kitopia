using System.IO.Pipelines;
using Core.Services.DeviceCommunication.Messages.Chat;
using Core.Services.DeviceCommunication.Sessions;

namespace Core.Services.DeviceCommunication.Application;

public sealed class FileTransferPayloadHandler
{
    private readonly IIncomingMessageSink _incomingMessageSink;
    private readonly IFileTransferSessionStore _fileTransferSessionStore;

    public FileTransferPayloadHandler(
        IIncomingMessageSink incomingMessageSink,
        IFileTransferSessionStore fileTransferSessionStore)
    {
        _incomingMessageSink = incomingMessageSink;
        _fileTransferSessionStore = fileTransferSessionStore;
    }

    public async ValueTask HandleAsync(FileChatMessage message, PipeReader payload, CancellationToken cancellationToken)
    {
        if (!_fileTransferSessionStore.TryGet(message.ChannelId, out var session) ||
            session.State != FileTransferState.Accepted ||
            string.IsNullOrWhiteSpace(session.SavePath))
        {
            await DrainPayloadAsync(message, payload, cancellationToken);
            return;
        }

        var totalBytes = Math.Max(0L, message.Length ?? 0L);
        var directory = Path.GetDirectoryName(session.SavePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        long receivedBytes = 0;
        long lastReportedBytes = 0;
        const int progressStepBytes = 1024 * 1024;

        async ValueTask ReportProgressAsync(int written)
        {
            if (written <= 0)
            {
                return;
            }

            receivedBytes += written;
            if (receivedBytes - lastReportedBytes < progressStepBytes)
            {
                return;
            }

            var progressTotal = totalBytes > 0 ? totalBytes : Math.Max(receivedBytes, 1L);
            await _incomingMessageSink.PublishEventAsync(
                new IncomingMessageEvent(message, IncomingMessageEventType.TransferProgress, message.ChannelId,
                    receivedBytes, progressTotal),
                cancellationToken);
            lastReportedBytes = receivedBytes;
        }

        try
        {
            await using var fileStream = new FileStream(
                session.SavePath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                useAsync: true);

            await using var progressStream = new ProgressReportingWriteStream(fileStream, ReportProgressAsync);
            await payload.CopyToAsync(progressStream, cancellationToken);

            if (receivedBytes > 0 && receivedBytes != lastReportedBytes)
            {
                var finalProgressTotal = totalBytes > 0 ? totalBytes : receivedBytes;
                await _incomingMessageSink.PublishEventAsync(
                    new IncomingMessageEvent(message, IncomingMessageEventType.TransferProgress, message.ChannelId,
                        receivedBytes, finalProgressTotal),
                    cancellationToken);
            }

            await fileStream.FlushAsync(cancellationToken);
            _fileTransferSessionStore.TryUpdateState(message.ChannelId, FileTransferState.Accepted, FileTransferState.Completed);
            _fileTransferSessionStore.TryRemove(message.ChannelId, out _);

            await _incomingMessageSink.PublishEventAsync(
                new IncomingMessageEvent(
                    new FileCompleteChatMessage(message.ConversationId, message.ChannelId),
                    IncomingMessageEventType.TransferCompleted,
                    message.ChannelId,
                    receivedBytes,
                    Math.Max(receivedBytes, totalBytes)),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _fileTransferSessionStore.TryRemove(message.ChannelId, out _);
            await _incomingMessageSink.PublishEventAsync(
                new IncomingMessageEvent(
                    new FileCancelChatMessage(message.ConversationId, message.ChannelId, "cancelled"),
                    IncomingMessageEventType.TransferCancelled,
                    message.ChannelId,
                    receivedBytes,
                    Math.Max(receivedBytes, totalBytes),
                    "cancelled"),
                CancellationToken.None);
            throw;
        }
        catch
        {
            _fileTransferSessionStore.TryRemove(message.ChannelId, out _);
            await _incomingMessageSink.PublishEventAsync(
                new IncomingMessageEvent(
                    new FileRejectChatMessage(message.ConversationId, message.ChannelId, "receive_failed"),
                    IncomingMessageEventType.TransferRejected,
                    message.ChannelId,
                    receivedBytes,
                    Math.Max(receivedBytes, totalBytes),
                    "receive_failed"),
                CancellationToken.None);
            throw;
        }
    }

    private async ValueTask DrainPayloadAsync(
        FileChatMessage message,
        PipeReader payload,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var readResult = await payload.ReadAsync(cancellationToken);
            var buffer = readResult.Buffer;
            payload.AdvanceTo(buffer.End);
            if (readResult.IsCompleted)
            {
                break;
            }
        }

        await _incomingMessageSink.PublishEventAsync(
            new IncomingMessageEvent(
                new FileRejectChatMessage(message.ConversationId, message.ChannelId, "missing_accept_session"),
                IncomingMessageEventType.TransferRejected,
                message.ChannelId,
                Reason: "missing_accept_session"),
            cancellationToken);
    }

    private sealed class ProgressReportingWriteStream : Stream
    {
        private readonly Stream _inner;
        private readonly Func<int, ValueTask> _onWrite;

        public ProgressReportingWriteStream(Stream inner, Func<int, ValueTask> onWrite)
        {
            _inner = inner;
            _onWrite = onWrite;
        }

        public override bool CanRead => false;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => _inner.CanWrite;
        public override long Length => _inner.Length;
        public override long Position { get => _inner.Position; set => _inner.Position = value; }
        public override void Flush() => _inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count)
        {
            _inner.Write(buffer, offset, count);
            if (count > 0)
            {
                _onWrite(count).AsTask().GetAwaiter().GetResult();
            }
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await _inner.WriteAsync(buffer, cancellationToken);
            if (!buffer.IsEmpty)
            {
                await _onWrite(buffer.Length);
            }
        }

        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => _inner.SetLength(value);
    }
}
