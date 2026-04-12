using System.Buffers;
using System.IO.Pipelines;

namespace Core.Services.DeviceCommunication;

internal sealed class ReadOnlyMemoryPipeReader : PipeReader
{
    private readonly ReadOnlySequence<byte> _sequence;
    private SequencePosition _consumed;
    private bool _isCompleted;

    public ReadOnlyMemoryPipeReader(ReadOnlyMemory<byte> payload)
    {
        _sequence = new ReadOnlySequence<byte>(payload);
        _consumed = _sequence.Start;
    }

    public override void AdvanceTo(SequencePosition consumed)
    {
        AdvanceTo(consumed, consumed);
    }

    public override void AdvanceTo(SequencePosition consumed, SequencePosition examined)
    {
        if (_isCompleted)
        {
            return;
        }

        _consumed = consumed;
    }

    public override void CancelPendingRead()
    {
    }

    public override void Complete(Exception? exception = null)
    {
        _isCompleted = true;
        _consumed = _sequence.End;
    }

    public override ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return ValueTask.FromCanceled<ReadResult>(cancellationToken);
        }

        return new ValueTask<ReadResult>(CreateReadResult());
    }

    public override bool TryRead(out ReadResult result)
    {
        result = CreateReadResult();
        return true;
    }

    private ReadResult CreateReadResult()
    {
        if (_isCompleted)
        {
            return new ReadResult(ReadOnlySequence<byte>.Empty, isCanceled: false, isCompleted: true);
        }

        var remaining = _sequence.Slice(_consumed, _sequence.End);
        return new ReadResult(remaining, isCanceled: false, isCompleted: true);
    }
}
