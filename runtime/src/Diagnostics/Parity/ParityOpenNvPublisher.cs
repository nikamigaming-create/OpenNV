using Godot;

namespace OpenNV.Runtime.Diagnostics.Parity;

internal sealed partial class ParityOpenNvPublisher : Node
{
    private ParitySharedMemoryRing? _ring;
    private Func<ulong, ParityTelemetryFrame>? _capture;
    private ulong _sequence;

    internal void Configure(
        string channel,
        Func<ulong, ParityTelemetryFrame> capture)
    {
        if (_ring is not null)
            throw new InvalidOperationException("OpenNV parity publisher is already configured.");
        _capture = capture ?? throw new ArgumentNullException(nameof(capture));
        _ring = ParitySharedMemoryRing.CreateOrOpen(channel);
        ProcessPhysicsPriority = int.MaxValue;
    }

    public override void _PhysicsProcess(double delta)
    {
        _ = delta;
        if (_ring is null || _capture is null)
            return;
        var frame = _capture(++_sequence);
        if (frame.Engine != ParityEngine.OpenNv || frame.Sequence != _sequence)
            throw new InvalidOperationException("OpenNV parity publisher received a mismatched frame.");
        _ring.Publish(ParityTelemetryCodec.Encode(frame));
    }

    public override void _ExitTree()
    {
        _ring?.Dispose();
        _ring = null;
        _capture = null;
    }
}
