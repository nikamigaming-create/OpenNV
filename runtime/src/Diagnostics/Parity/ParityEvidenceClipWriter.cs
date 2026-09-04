using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace OpenNV.Runtime.Diagnostics.Parity;

internal static class ParityEvidenceClipWriter
{
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];

    internal static ParityClipReport WriteSideBySide(
        string ffmpegPath,
        string outputRoot,
        ParityEvidenceClip retail,
        ParityEvidenceClip openNv,
        int framesPerSecond)
    {
        if (!File.Exists(ffmpegPath) || Directory.Exists(outputRoot) || File.Exists(outputRoot) ||
            framesPerSecond is < 1 or > 240 ||
            retail.Frames.Count == 0 || retail.Frames.Count != openNv.Frames.Count ||
            retail.DivergenceSequence != openNv.DivergenceSequence)
            throw new InvalidDataException("Parity clip pair is invalid or would overwrite evidence.");
        var root = Path.GetFullPath(outputRoot);
        var retailRoot = Path.Combine(root, "retail");
        var openNvRoot = Path.Combine(root, "opennv");
        Directory.CreateDirectory(retailRoot);
        Directory.CreateDirectory(openNvRoot);
        try
        {
            WriteFrames(retailRoot, retail.Frames);
            WriteFrames(openNvRoot, openNv.Frames);
            var video = Path.Combine(root, "retail-left-opennv-right.mp4");
            var process = new ProcessStartInfo
            {
                FileName = Path.GetFullPath(ffmpegPath),
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            };
            foreach (var argument in Arguments(retailRoot, openNvRoot, video, framesPerSecond))
                process.ArgumentList.Add(argument);
            using var running = Process.Start(process) ??
                throw new InvalidOperationException("Could not start ffmpeg for parity evidence.");
            var standardError = running.StandardError.ReadToEnd();
            running.WaitForExit();
            if (running.ExitCode != 0 || !File.Exists(video))
                throw new InvalidOperationException(
                    $"Parity ffmpeg encoding failed with {running.ExitCode}: {standardError}");
            var probe = ProbeVideo(ffmpegPath, video);
            var videoInfo = new FileInfo(video);
            var report = new ParityClipReport(
                "opennv-parity-clip/v1",
                "captured-divergence-pending-parity",
                retail.Reason,
                retail.DivergenceSequence,
                framesPerSecond,
                retail.Frames.Count,
                video,
                videoInfo.Length,
                Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(video))).ToLowerInvariant(),
                probe,
                retail.Frames.Select(FrameEvidence).ToArray(),
                openNv.Frames.Select(FrameEvidence).ToArray());
            File.WriteAllText(
                Path.Combine(root, "parity-clip-report.json"),
                JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) +
                Environment.NewLine);
            return report;
        }
        catch
        {
            Directory.Delete(root, recursive: true);
            throw;
        }
    }

    internal static IReadOnlyList<string> Arguments(
        string retailRoot,
        string openNvRoot,
        string video,
        int framesPerSecond) =>
    [
        "-hide_banner", "-loglevel", "error", "-y",
        "-framerate", framesPerSecond.ToString(System.Globalization.CultureInfo.InvariantCulture),
        "-i", Path.Combine(retailRoot, "frame-%06d.png"),
        "-framerate", framesPerSecond.ToString(System.Globalization.CultureInfo.InvariantCulture),
        "-i", Path.Combine(openNvRoot, "frame-%06d.png"),
        "-filter_complex", "[0:v]setpts=PTS-STARTPTS[left];[1:v]setpts=PTS-STARTPTS[right];[left][right]hstack=inputs=2[v]",
        "-map", "[v]", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-movflags", "+faststart",
        video,
    ];

    private static void WriteFrames(string directory, IReadOnlyList<ParityCapturedFrame> frames)
    {
        for (var index = 0; index < frames.Count; index++)
        {
            var frame = frames[index];
            if (!frame.EncodedFrame.AsSpan().StartsWith(PngSignature) ||
                !CryptographicOperations.FixedTimeEquals(
                    frame.Sha256,
                    SHA256.HashData(frame.EncodedFrame)))
                throw new InvalidDataException("Parity clip contains a non-PNG or hash-invalid frame.");
            File.WriteAllBytes(
                Path.Combine(directory, $"frame-{index:000000}.png"),
                frame.EncodedFrame);
        }
    }

    private static ParityFrameEvidence FrameEvidence(ParityCapturedFrame frame) => new(
        frame.MonotonicNanoseconds,
        frame.TelemetrySequence,
        frame.EncodedFrame.LongLength,
        Convert.ToHexString(frame.Sha256).ToLowerInvariant());

    private static ParityVideoProbe ProbeVideo(string ffmpegPath, string video)
    {
        var extension = Path.GetExtension(ffmpegPath);
        var ffprobe = Path.Combine(
            Path.GetDirectoryName(Path.GetFullPath(ffmpegPath))!,
            "ffprobe" + extension);
        if (!File.Exists(ffprobe))
            throw new FileNotFoundException("Parity video validation requires ffprobe beside ffmpeg.", ffprobe);
        var process = new ProcessStartInfo
        {
            FileName = ffprobe,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
        };
        foreach (var argument in new[]
        {
            "-v", "error", "-select_streams", "v:0",
            "-show_entries", "stream=codec_name,width,height,pix_fmt,r_frame_rate",
            "-of", "json", video,
        })
            process.ArgumentList.Add(argument);
        using var running = Process.Start(process) ??
            throw new InvalidOperationException("Could not start ffprobe for parity evidence.");
        var json = running.StandardOutput.ReadToEnd();
        var error = running.StandardError.ReadToEnd();
        running.WaitForExit();
        if (running.ExitCode != 0)
            throw new InvalidOperationException(
                $"Parity ffprobe validation failed with {running.ExitCode}: {error}");
        using var document = JsonDocument.Parse(json);
        var streams = document.RootElement.GetProperty("streams");
        if (streams.GetArrayLength() != 1)
            throw new InvalidDataException("Parity video does not contain exactly one selected video stream.");
        var stream = streams[0];
        var result = new ParityVideoProbe(
            stream.GetProperty("codec_name").GetString()!,
            stream.GetProperty("width").GetInt32(),
            stream.GetProperty("height").GetInt32(),
            stream.GetProperty("pix_fmt").GetString()!,
            stream.GetProperty("r_frame_rate").GetString()!);
        if (result.Codec != "h264" || result.Width <= 0 || result.Height <= 0 ||
            result.Width % 2 != 0 || result.Height % 2 != 0 ||
            result.PixelFormat != "yuv420p")
            throw new InvalidDataException("Parity video media contract is invalid.");
        return result;
    }
}

internal sealed record ParityFrameEvidence(
    long MonotonicNanoseconds,
    ulong TelemetrySequence,
    long Bytes,
    string Sha256);

internal sealed record ParityVideoProbe(
    string Codec,
    int Width,
    int Height,
    string PixelFormat,
    string FrameRate);

internal sealed record ParityClipReport(
    string Schema,
    string Status,
    string Reason,
    ulong DivergenceSequence,
    int FramesPerSecond,
    int Frames,
    string Video,
    long VideoBytes,
    string VideoSha256,
    ParityVideoProbe Probe,
    IReadOnlyList<ParityFrameEvidence> RetailFrames,
    IReadOnlyList<ParityFrameEvidence> OpenNvFrames);
