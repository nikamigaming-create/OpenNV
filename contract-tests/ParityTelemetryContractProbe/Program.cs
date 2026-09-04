using OpenNV.Runtime.Diagnostics.Parity;
using System.Diagnostics;
using System.Security.Cryptography;

var fields = new ParityTelemetryField[]
{
    ParityTelemetryField.Float64(ParityCategory.Camera, 2, 75.0),
    ParityTelemetryField.Utf8(ParityCategory.World, 1, "GoodspringsGeneralStore"),
    ParityTelemetryField.UInt64(ParityCategory.Quest, 10, 50),
};
var retail = new ParityTelemetryFrame(
    ParityEngine.Retail,
    400,
    1200,
    8_000_000_000,
    20,
    "cell:00104c1c/event:dialogue:00012345",
    fields);
var openNv = retail with
{
    Engine = ParityEngine.OpenNv,
    Sequence = 398,
    MonotonicNanoseconds = 8_000_100_000,
    Fields = fields.Reverse().ToArray(),
};

var encoded = ParityTelemetryCodec.Encode(retail);
var decoded = ParityTelemetryCodec.Decode(encoded);
if (decoded.Engine != retail.Engine || decoded.Sequence != retail.Sequence ||
    decoded.StateKey != retail.StateKey || decoded.Fields.Count != fields.Length)
    throw new InvalidOperationException("Parity telemetry did not round-trip its envelope and state.");
var exact = ParityFrameComparator.Compare(retail, openNv);
if (!exact.ComparableState || !exact.ExactStateMatch || exact.Deltas.Count != 0 ||
    exact.MonotonicNanosecondsDelta != 100_000)
    throw new InvalidOperationException("Parity telemetry did not isolate envelope timing from exact state bytes.");

var changed = openNv with
{
    Fields =
    [
        ParityTelemetryField.Float64(ParityCategory.Camera, 2, 74.5),
        ParityTelemetryField.Utf8(ParityCategory.World, 1, "GoodspringsGeneralStore"),
        ParityTelemetryField.UInt64(ParityCategory.Quest, 10, 50),
        ParityTelemetryField.UInt64(ParityCategory.Effect, 90, 1),
    ],
};
var mismatch = ParityFrameComparator.Compare(retail, changed);
if (mismatch.ExactStateMatch || mismatch.FirstStateByteOffset is null ||
    mismatch.Deltas.Count != 2 ||
    mismatch.Deltas.Single(delta => delta.Category == ParityCategory.Camera).NumericDelta != -0.5 ||
    mismatch.Deltas.Single(delta => delta.Category == ParityCategory.Effect).Kind !=
        ParityDeltaKind.MissingRetail)
    throw new InvalidOperationException("Parity semantic delta expansion is incomplete.");

var corrupt = encoded.ToArray();
corrupt[^1] ^= byte.MaxValue;
try
{
    _ = ParityTelemetryCodec.Decode(corrupt);
    throw new InvalidOperationException("Parity telemetry accepted corrupt bytes.");
}
catch (InvalidDataException)
{
}

if (OperatingSystem.IsWindows())
{
    var channel = "probe_" + Guid.NewGuid().ToString("N");
    using var writer = ParitySharedMemoryRing.CreateOrOpen(channel, 4, 64 * 1024);
    using var reader = ParitySharedMemoryRing.CreateOrOpen(channel, 4, 64 * 1024);
    var ringSequence = writer.Publish(encoded);
    if (!reader.TryReadLatest(out var observedSequence, out var observed) ||
        observedSequence != ringSequence || !observed.AsSpan().SequenceEqual(encoded))
        throw new InvalidOperationException("Parity shared-memory ring lost exact packet bytes.");
    for (var index = 0; index < 5; index++)
        writer.Publish(encoded);
    if (!reader.TryRead(3, out var retained) || !retained.AsSpan().SequenceEqual(encoded))
        throw new InvalidOperationException("Parity shared-memory ring did not retain an addressable sequence.");
    try
    {
        _ = reader.TryRead(2, out _);
        throw new InvalidOperationException("Parity shared-memory ring concealed an overrun.");
    }
    catch (InvalidDataException)
    {
    }
}

var clips = new ParityClipBuffer(2, 2, 4096);
clips.Push(1, 1, [1, 2, 3]);
clips.Push(2, 2, [4, 5, 6]);
if (!clips.Trigger("camera-byte-drift", 2))
    throw new InvalidOperationException("Parity clip trigger was not armed.");
clips.Push(3, 3, [7, 8, 9]);
clips.Push(4, 4, [10, 11, 12]);
if (!clips.TryTakeCompleted(out var clip) || clip.Frames.Count != 4 ||
    clip.DivergenceSequence != 2 || clip.Frames.Any(frame => frame.Sha256.Length != 32))
    throw new InvalidOperationException("Parity divergence clip did not retain its pre/post window.");

var videoArguments = ParityEvidenceClipWriter.Arguments(
    "D:\\retail",
    "D:\\opennv",
    "D:\\result.mp4",
    60);
if (!videoArguments.Contains("[0:v]setpts=PTS-STARTPTS[left];[1:v]setpts=PTS-STARTPTS[right];[left][right]hstack=inputs=2[v]") ||
    videoArguments[^1] != "D:\\result.mp4")
    throw new InvalidOperationException("Parity video plan is not retail-left/OpenNV-right.");

var traceRoot = Path.Combine(Path.GetTempPath(), "opennv-parity-" + Guid.NewGuid().ToString("N"));
var tracePath = Path.Combine(traceRoot, "retail.onvtrace");
try
{
    using (var trace = new ParityTraceWriter(tracePath))
    {
        trace.Append(encoded);
        trace.Append(ParityTelemetryCodec.Encode(openNv));
    }
    var packets = ParityTraceReader.ReadAll(tracePath);
    if (packets.Count != 2 || !packets[0].AsSpan().SequenceEqual(encoded))
        throw new InvalidOperationException("Parity trace did not preserve exact packet bytes.");
}
finally
{
    if (Directory.Exists(traceRoot))
        Directory.Delete(traceRoot, recursive: true);
}

var configuredFfmpeg = Environment.GetEnvironmentVariable("OPENNV_FFMPEG");
var encodedVideo = false;
if (!string.IsNullOrWhiteSpace(configuredFfmpeg) && File.Exists(configuredFfmpeg))
{
    var mediaRoot = Path.Combine(Path.GetTempPath(), "opennv-parity-media-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(mediaRoot);
    try
    {
        var retailPng = Path.Combine(mediaRoot, "retail.png");
        var openNvPng = Path.Combine(mediaRoot, "opennv.png");
        GeneratePng(configuredFfmpeg, "red", retailPng);
        GeneratePng(configuredFfmpeg, "blue", openNvPng);
        var retailBytes = File.ReadAllBytes(retailPng);
        var openNvBytes = File.ReadAllBytes(openNvPng);
        var retailClip = Clip("synthetic-color-divergence", retailBytes);
        var openNvClip = Clip("synthetic-color-divergence", openNvBytes);
        var report = ParityEvidenceClipWriter.WriteSideBySide(
            configuredFfmpeg,
            Path.Combine(mediaRoot, "clip"),
            retailClip,
            openNvClip,
            30);
        if (report.Probe.Codec != "h264" || report.Probe.Width != 32 ||
            report.Probe.Height != 16 || report.Frames != 4 ||
            report.VideoSha256.Length != 64)
            throw new InvalidOperationException("Parity video evidence did not pass its media contract.");
        encodedVideo = true;
    }
    finally
    {
        Directory.Delete(mediaRoot, recursive: true);
    }
}

Console.WriteLine(
    "OPENNV_PARITY_TELEMETRY_PASS canonical=1 exact-bytes=1 semantic-deltas=2 shared-memory=1 " +
    $"overrun=fail-closed trace=2 clip-window=4 video-plan=1 video-encoded={(encodedVideo ? 1 : 0)}");

static ParityEvidenceClip Clip(string reason, byte[] png)
{
    var frames = Enumerable.Range(0, 4)
        .Select(index => new ParityCapturedFrame(
            index * 33_333_333L,
            (ulong)(index + 1),
            png,
            SHA256.HashData(png)))
        .ToArray();
    return new ParityEvidenceClip(reason, 2, frames);
}

static void GeneratePng(string ffmpeg, string color, string output)
{
    var start = new ProcessStartInfo
    {
        FileName = ffmpeg,
        UseShellExecute = false,
        RedirectStandardError = true,
        RedirectStandardOutput = true,
        CreateNoWindow = true,
    };
    foreach (var argument in new[]
    {
        "-hide_banner", "-loglevel", "error", "-y",
        "-f", "lavfi", "-i", $"color=c={color}:s=16x16:d=0.04",
        "-frames:v", "1", output,
    })
        start.ArgumentList.Add(argument);
    using var process = Process.Start(start) ??
        throw new InvalidOperationException("Could not start ffmpeg synthetic parity frame generation.");
    var error = process.StandardError.ReadToEnd();
    process.WaitForExit();
    if (process.ExitCode != 0 || !File.Exists(output))
        throw new InvalidOperationException(
            $"Synthetic parity frame generation failed with {process.ExitCode}: {error}");
}
