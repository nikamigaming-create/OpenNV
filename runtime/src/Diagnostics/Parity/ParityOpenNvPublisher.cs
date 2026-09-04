using Godot;

namespace OpenNV.Runtime.Diagnostics.Parity;

internal sealed partial class ParityOpenNvPublisher : Node
{
    private ParitySharedMemoryRing? _ring;
    private Func<ulong, ParityTelemetryFrame>? _capture;
    private ulong _sequence;
    private ParityFrameArchive? _archive;
    private ParityTelemetryFrame? _beforeDraw;

    internal void Configure(
        string channel,
        Func<ulong, ParityTelemetryFrame> capture,
        string? captureDirectory = null)
    {
        if (_ring is not null)
            throw new InvalidOperationException("OpenNV parity publisher is already configured.");
        _capture = capture ?? throw new ArgumentNullException(nameof(capture));
        _ring = ParitySharedMemoryRing.CreateOrOpen(channel);
        ProcessPhysicsPriority = int.MaxValue;
        if (captureDirectory is not null)
        {
            if (DisplayServer.GetName() == "headless")
                throw new InvalidOperationException("Viewport capture requires a rendering display.");
            _archive = new ParityFrameArchive(captureDirectory);
            RenderingServer.FramePreDraw += BeforeDraw;
            RenderingServer.FramePostDraw += AfterDraw;
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        _ = delta;
        if (_ring is null || _capture is null || _archive is not null)
            return;
        var frame = _capture(++_sequence);
        if (frame.Engine != ParityEngine.OpenNv || frame.Sequence != _sequence)
            throw new InvalidOperationException("OpenNV parity publisher received a mismatched frame.");
        _ring.Publish(ParityTelemetryCodec.Encode(frame));
    }

    private void BeforeDraw()
    {
        try
        {
            if (_beforeDraw is not null)
                throw new InvalidOperationException("A viewport draw ended without a captured post-draw boundary.");
            _beforeDraw = _capture!(checked(++_sequence));
        }
        catch (Exception exception)
        {
            FailCapture(exception);
        }
    }

    private void AfterDraw()
    {
        try
        {
            var before = _beforeDraw ??
                throw new InvalidOperationException("Viewport readback has no pre-draw state.");
            var after = _capture!(_sequence);
            using var image = GetViewport().GetTexture().GetImage();
            if (image.IsEmpty())
                throw new InvalidDataException("Rendered viewport readback is empty.");
            // Retain the actual readback format and bytes. The PNG is a preview;
            // no Image.Convert, resize, or color operation alters the native lane.
            _archive!.Append(before, after, Engine.GetFramesDrawn(),
                image.GetWidth(), image.GetHeight(), image.GetFormat().ToString(),
                image.GetData(), image.SavePngToBuffer());
            _ring!.Publish(ParityTelemetryCodec.Encode(before));
            _beforeDraw = null;
        }
        catch (Exception exception)
        {
            FailCapture(exception);
        }
    }

    private void FailCapture(Exception exception)
    {
        RenderingServer.FramePreDraw -= BeforeDraw;
        RenderingServer.FramePostDraw -= AfterDraw;
        GD.PushError($"OPENNV_PARITY_CAPTURE_FAILED {exception}");
        GetTree().Quit(1);
    }

    public override void _ExitTree()
    {
        RenderingServer.FramePreDraw -= BeforeDraw;
        RenderingServer.FramePostDraw -= AfterDraw;
        _archive?.Dispose();
        _archive = null;
        _ring?.Dispose();
        _ring = null;
        _capture = null;
    }
}
