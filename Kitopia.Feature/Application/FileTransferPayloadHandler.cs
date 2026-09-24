using System.IO.Pipelines;
using Kitopia.Feature.DeviceCommunication.Messages.Chat;
using Kitopia.Feature.DeviceCommunication.Sessions;

namespace Kitopia.Feature.DeviceCommunication.Application;

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
        if (!_fileTransferSessionStore.TryBeginReceive(message.ChannelId, message.ConversationId,
                message.Length, out var session, out var receiveCancellationToken))
        {
            await _incomingMessageSink.PublishEventAsync(
                new FileTransferUpdatedEvent(
                    message.ConversationId,
                    message.ChannelId,
                    FileTransferDirection.Download,
                    FileTransferStatus.Failed,
                    message.FileName,
                    null,
                    message.Length,
                    "invalid_accept_session",
                    DateTimeOffset.UtcNow),
                cancellationToken);
            throw new InvalidDataException("File payload has no matching accepted offer.");
        }

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, receiveCancellationToken);
        var receiveToken = linkedCancellation.Token;
        var totalBytes = session.SizeBytes;
        string? temporaryPath = null;

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
                new FileTransferUpdatedEvent(
                    message.ConversationId,
                    message.ChannelId,
                    FileTransferDirection.Download,
                    FileTransferStatus.InProgress,
                    message.FileName,
                    receivedBytes,
                    progressTotal,
                    null,
                    DateTimeOffset.UtcNow),
                receiveToken);
            lastReportedBytes = receivedBytes;
        }

        try
        {
            Stream fileStream;
            if (session.OpenWriteStreamAsync is { } openWriteStreamAsync)
            {
                fileStream = await openWriteStreamAsync(receiveToken);
                if (fileStream.CanSeek)
                {
                    fileStream.SetLength(0);
                }
            }
            else
            {
                var savePath = session.SavePath ?? throw new InvalidOperationException("Missing file save target.");
                var directory = Path.GetDirectoryName(savePath);
                directory = string.IsNullOrWhiteSpace(directory) ? Directory.GetCurrentDirectory() : directory;
                Directory.CreateDirectory(directory);
                temporaryPath = Path.Combine(directory, $".{Path.GetFileName(savePath)}.{Guid.NewGuid():N}.tmp");
                fileStream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    64 * 1024, useAsync: true);
            }

            await using (fileStream)
            {
                await using var progressStream = new ProgressReportingWriteStream(fileStream, totalBytes, ReportProgressAsync);
                await payload.CopyToAsync(progressStream, receiveToken);
                if (receivedBytes != totalBytes)
                {
                    throw new InvalidDataException("File payload length differs from its accepted offer.");
                }

                if (receivedBytes > 0 && receivedBytes != lastReportedBytes)
                {
                    var finalProgressTotal = totalBytes > 0 ? totalBytes : receivedBytes;
                    await _incomingMessageSink.PublishEventAsync(
                        new FileTransferUpdatedEvent(
                            message.ConversationId,
                            message.ChannelId,
                            FileTransferDirection.Download,
                            FileTransferStatus.InProgress,
                            message.FileName,
                            receivedBytes,
                            finalProgressTotal,
                            null,
                            DateTimeOffset.UtcNow),
                        receiveToken);
                }

                await fileStream.FlushAsync(receiveToken);
            }

            receiveToken.ThrowIfCancellationRequested();
            if (temporaryPath is not null)
            {
                if (File.Exists(session.SavePath))
                {
                    File.Replace(temporaryPath, session.SavePath, null);
                }
                else
                {
                    File.Move(temporaryPath, session.SavePath!);
                }
            }

            _fileTransferSessionStore.TryUpdateState(message.ChannelId, FileTransferState.Receiving, FileTransferState.Completed);
            _fileTransferSessionStore.TryRemove(message.ChannelId, out _);

            await _incomingMessageSink.PublishEventAsync(
                new FileTransferUpdatedEvent(
                    message.ConversationId,
                    message.ChannelId,
                    FileTransferDirection.Download,
                    FileTransferStatus.Completed,
                    message.FileName,
                    receivedBytes,
                    Math.Max(receivedBytes, totalBytes),
                    null,
                    DateTimeOffset.UtcNow),
                CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            _fileTransferSessionStore.TryRemove(message.ChannelId, out _);
            await _incomingMessageSink.PublishEventAsync(
                new FileTransferUpdatedEvent(
                    message.ConversationId,
                    message.ChannelId,
                    FileTransferDirection.Download,
                    FileTransferStatus.Cancelled,
                    message.FileName,
                    receivedBytes,
                    Math.Max(receivedBytes, totalBytes),
                    "cancelled",
                    DateTimeOffset.UtcNow),
                CancellationToken.None);
            throw;
        }
        catch
        {
            _fileTransferSessionStore.TryRemove(message.ChannelId, out _);
            await _incomingMessageSink.PublishEventAsync(
                new FileTransferUpdatedEvent(
                    message.ConversationId,
                    message.ChannelId,
                    FileTransferDirection.Download,
                    FileTransferStatus.Failed,
                    message.FileName,
                    receivedBytes,
                    Math.Max(receivedBytes, totalBytes),
                    "receive_failed",
                    DateTimeOffset.UtcNow),
                CancellationToken.None);
            throw;
        }
        finally
        {
            session.ReceiveCancellation?.Dispose();
            if (temporaryPath is not null)
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
    }

    private sealed class ProgressReportingWriteStream : Stream
    {
        private readonly Stream _inner;
        private readonly long _maximumBytes;
        private readonly Func<int, ValueTask> _onWrite;
        private long _writtenBytes;

        public ProgressReportingWriteStream(Stream inner, long maximumBytes, Func<int, ValueTask> onWrite)
        {
            _inner = inner;
            _maximumBytes = maximumBytes;
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
            if (count > _maximumBytes - _writtenBytes)
            {
                throw new InvalidDataException("File payload exceeds its accepted offer size.");
            }

            _inner.Write(buffer, offset, count);
            _writtenBytes += count;
            if (count > 0)
            {
                _onWrite(count).AsTask().GetAwaiter().GetResult();
            }
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (buffer.Length > _maximumBytes - _writtenBytes)
            {
                throw new InvalidDataException("File payload exceeds its accepted offer size.");
            }

            await _inner.WriteAsync(buffer, cancellationToken);
            _writtenBytes += buffer.Length;
            if (!buffer.IsEmpty)
            {
                await _onWrite(buffer.Length);
            }
        }

        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => _inner.SetLength(value);
    }
}
