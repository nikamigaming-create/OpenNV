namespace OpenNV.Runtime.Diagnostics.Parity;

internal readonly record struct ParityJoinKey(string StateKey, ulong EventOrdinal);

internal sealed record ParityJoinedFrame(
    ParityTelemetryFrame Retail,
    ParityTelemetryFrame OpenNv,
    ParityComparison Comparison);

internal sealed class ParityLiveJoiner
{
    private readonly int _maximumPendingFrames;
    private readonly Dictionary<ParityJoinKey, Queue<ParityTelemetryFrame>> _retail = [];
    private readonly Dictionary<ParityJoinKey, Queue<ParityTelemetryFrame>> _openNv = [];
    private ulong _lastRetailSequence;
    private ulong _lastOpenNvSequence;

    internal ParityLiveJoiner(int maximumPendingFrames = 256)
    {
        if (maximumPendingFrames is < 2 or > 65536)
            throw new ArgumentOutOfRangeException(nameof(maximumPendingFrames));
        _maximumPendingFrames = maximumPendingFrames;
    }

    internal int PendingRetailFrames => Count(_retail);

    internal int PendingOpenNvFrames => Count(_openNv);

    internal ParityJoinedFrame? Push(ParityTelemetryFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var own = frame.Engine switch
        {
            ParityEngine.Retail => _retail,
            ParityEngine.OpenNv => _openNv,
            _ => throw new InvalidDataException("Live parity join received an unknown engine."),
        };
        var other = frame.Engine == ParityEngine.Retail ? _openNv : _retail;
        ValidateSequence(frame);
        var key = new ParityJoinKey(frame.StateKey, frame.EventOrdinal);
        if (other.TryGetValue(key, out var matches) && matches.Count > 0)
        {
            var matched = matches.Dequeue();
            if (matches.Count == 0)
                other.Remove(key);
            var retail = frame.Engine == ParityEngine.Retail ? frame : matched;
            var openNv = frame.Engine == ParityEngine.OpenNv ? frame : matched;
            return new ParityJoinedFrame(
                retail,
                openNv,
                ParityFrameComparator.Compare(retail, openNv));
        }
        if (!own.TryGetValue(key, out var pending))
        {
            pending = new Queue<ParityTelemetryFrame>();
            own.Add(key, pending);
        }
        pending.Enqueue(frame);
        if (PendingRetailFrames + PendingOpenNvFrames > _maximumPendingFrames)
            throw new InvalidDataException(
                $"Live parity join exceeded {_maximumPendingFrames} unmatched frames: " +
                $"retail={PendingRetailFrames}, OpenNV={PendingOpenNvFrames}.");
        return null;
    }

    private void ValidateSequence(ParityTelemetryFrame frame)
    {
        ref var prior = ref frame.Engine == ParityEngine.Retail
            ? ref _lastRetailSequence
            : ref _lastOpenNvSequence;
        var expected = checked(prior + 1);
        if (frame.Sequence != expected)
            throw new InvalidDataException(
                $"{frame.Engine} parity producer sequence gap: expected {expected}, " +
                $"received {frame.Sequence}.");
        prior = frame.Sequence;
    }

    private static int Count(
        IReadOnlyDictionary<ParityJoinKey, Queue<ParityTelemetryFrame>> queues) =>
        queues.Values.Sum(queue => queue.Count);
}
