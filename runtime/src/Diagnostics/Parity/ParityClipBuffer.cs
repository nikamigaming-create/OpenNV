namespace OpenNV.Runtime.Diagnostics.Parity;

internal sealed record ParityCapturedFrame(
    long MonotonicNanoseconds,
    ulong TelemetrySequence,
    byte[] EncodedFrame,
    byte[] Sha256);

internal sealed record ParityEvidenceClip(
    string Reason,
    ulong DivergenceSequence,
    IReadOnlyList<ParityCapturedFrame> Frames);

internal sealed class ParityClipBuffer
{
    private readonly int _preFrames;
    private readonly int _postFrames;
    private readonly int _maximumFrameBytes;
    private readonly Queue<ParityCapturedFrame> _rolling = new();
    private List<ParityCapturedFrame>? _active;
    private string? _reason;
    private ulong _divergenceSequence;
    private int _remainingPostFrames;
    private ParityEvidenceClip? _completed;

    internal ParityClipBuffer(int preFrames, int postFrames, int maximumFrameBytes)
    {
        if (preFrames < 1 || postFrames < 1 || maximumFrameBytes < 1024)
            throw new ArgumentOutOfRangeException(nameof(preFrames));
        _preFrames = preFrames;
        _postFrames = postFrames;
        _maximumFrameBytes = maximumFrameBytes;
    }

    internal void Push(long monotonicNanoseconds, ulong sequence, ReadOnlySpan<byte> encodedFrame)
    {
        if (monotonicNanoseconds < 0 || encodedFrame.Length == 0 ||
            encodedFrame.Length > _maximumFrameBytes)
            throw new InvalidDataException("Parity video frame is invalid.");
        var bytes = encodedFrame.ToArray();
        var frame = new ParityCapturedFrame(
            monotonicNanoseconds,
            sequence,
            bytes,
            System.Security.Cryptography.SHA256.HashData(bytes));
        if (_active is not null)
        {
            _active.Add(frame);
            _remainingPostFrames--;
            if (_remainingPostFrames == 0)
            {
                _completed = new ParityEvidenceClip(
                    _reason!,
                    _divergenceSequence,
                    _active.ToArray());
                _active = null;
                _reason = null;
            }
        }
        _rolling.Enqueue(frame);
        while (_rolling.Count > _preFrames)
            _rolling.Dequeue();
    }

    internal bool Trigger(string reason, ulong divergenceSequence)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Parity clip reason is required.", nameof(reason));
        if (_active is not null || _completed is not null)
            return false;
        _active = _rolling.ToList();
        _reason = reason;
        _divergenceSequence = divergenceSequence;
        _remainingPostFrames = _postFrames;
        return true;
    }

    internal bool TryTakeCompleted(out ParityEvidenceClip clip)
    {
        if (_completed is null)
        {
            clip = null!;
            return false;
        }
        clip = _completed;
        _completed = null;
        return true;
    }
}
