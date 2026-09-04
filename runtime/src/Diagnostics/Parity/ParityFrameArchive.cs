using System.Security.Cryptography;
using System.Text.Json;

namespace OpenNV.Runtime.Diagnostics.Parity;

internal sealed class ParityFrameArchive : IDisposable
{
    private readonly string _root;
    private readonly StreamWriter _index;
    private ulong _lastSequence;

    internal ParityFrameArchive(string outputDirectory)
    {
        _root = Path.GetFullPath(outputDirectory);
        if (Path.Exists(_root))
            throw new IOException($"Refusing to overwrite captured frames: {_root}");
        Directory.CreateDirectory(_root);
        _index = new StreamWriter(new FileStream(
            Path.Combine(_root, "frames.jsonl"), FileMode.CreateNew, FileAccess.Write, FileShare.Read))
        { AutoFlush = true };
    }

    internal void Append(
        ParityTelemetryFrame beforeDraw,
        ParityTelemetryFrame afterDraw,
        int engineDrawCount,
        int width,
        int height,
        string nativeFormat,
        byte[] nativePixels,
        byte[] png)
    {
        if (beforeDraw.Engine != ParityEngine.OpenNv || afterDraw.Engine != beforeDraw.Engine ||
            beforeDraw.Sequence != checked(_lastSequence + 1) ||
            afterDraw.Sequence != beforeDraw.Sequence ||
            afterDraw.MonotonicNanoseconds < beforeDraw.MonotonicNanoseconds ||
            width <= 0 || height <= 0 || string.IsNullOrWhiteSpace(nativeFormat) ||
            nativePixels.Length == 0 || png.Length == 0)
            throw new InvalidDataException("Captured viewport frame or draw-boundary telemetry is invalid.");
        var prefix = beforeDraw.Sequence.ToString("D10", System.Globalization.CultureInfo.InvariantCulture);
        var before = Write(prefix + ".before.onvpacket", ParityTelemetryCodec.Encode(beforeDraw));
        var after = Write(prefix + ".after.onvpacket", ParityTelemetryCodec.Encode(afterDraw));
        var pixels = Write(prefix + ".pixels", nativePixels);
        var preview = Write(prefix + ".png", png);
        _index.WriteLine(JsonSerializer.Serialize(new
        {
            schema = "opennv-viewport-frame/v1",
            sequence = beforeDraw.Sequence,
            engineDrawCount,
            stateKey = beforeDraw.StateKey,
            eventOrdinal = beforeDraw.EventOrdinal,
            capturePoint = "RenderingServer.frame_post_draw/ViewportTexture.get_image",
            width,
            height,
            nativeFormat,
            nativePixelBytes = nativePixels.Length,
            beforeDrawNanoseconds = beforeDraw.MonotonicNanoseconds,
            afterDrawNanoseconds = afterDraw.MonotonicNanoseconds,
            observedStateUnchangedAcrossDraw =
                beforeDraw.EventOrdinal == afterDraw.EventOrdinal &&
                beforeDraw.SimulationTick == afterDraw.SimulationTick &&
                ParityTelemetryCodec.EncodeCanonicalState(beforeDraw.StateKey, beforeDraw.Fields).AsSpan()
                    .SequenceEqual(ParityTelemetryCodec.EncodeCanonicalState(afterDraw.StateKey, afterDraw.Fields)),
            retailFrameCorrespondence = "unobserved",
            before,
            after,
            pixels,
            preview,
        }));
        _lastSequence = beforeDraw.Sequence;
    }

    private ArchivedBytes Write(string name, byte[] bytes)
    {
        using var output = new FileStream(
            Path.Combine(_root, name), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        output.Write(bytes);
        return new ArchivedBytes(name, Convert.ToHexString(SHA256.HashData(bytes)));
    }

    public void Dispose() => _index.Dispose();

    private sealed record ArchivedBytes(string File, string Sha256);
}
