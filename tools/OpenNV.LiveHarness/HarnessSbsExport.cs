using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OpenNV.LiveHarness;

internal sealed record HarnessSbsConfiguration(string RecordingDirectory, string OutputDirectory, string Encoder,
    string LabelFont, double BeginSeconds, double EndSeconds, int FramesPerSecond, string SourceCommit);

// Replay both received streams on their common monotonic clock. Never align
// different story phases by trimming one side or changing its playback speed.
internal static class HarnessSbsExport
{
    private sealed record Frame(long Index, long Sequence, double Seconds, int Width, int Height, string Image);

    internal static void Run(string configurationPath)
    {
        var config = JsonSerializer.Deserialize<HarnessSbsConfiguration>(File.ReadAllText(configurationPath), Program.Json)
            ?? throw new InvalidDataException("Missing SBS configuration.");
        if (new[] { config.RecordingDirectory, config.OutputDirectory, config.Encoder, config.LabelFont }.Any(path => !Path.IsPathFullyQualified(path)) ||
            Directory.Exists(config.OutputDirectory) || !File.Exists(config.Encoder) || !File.Exists(config.LabelFont) ||
            !double.IsFinite(config.BeginSeconds) || !double.IsFinite(config.EndSeconds) || config.BeginSeconds < 0 ||
            config.EndSeconds <= config.BeginSeconds || config.FramesPerSecond is < 1 or > 240)
            throw new ArgumentException("SBS requires a new private output directory, installed tools and a finite shared interval.");
        using var status = JsonDocument.Parse(File.ReadAllText(Path.Combine(config.RecordingDirectory, "recording.json")));
        if (!status.RootElement.GetProperty("finished").GetBoolean()) throw new InvalidOperationException("Finalize the recording before exporting.");
        var streams = new[] { "retail", "opennv" }.ToDictionary(engine => engine, engine => Read(config, engine));
        var first = streams["retail"][0];
        if (streams.Values.SelectMany(frames => frames).Any(frame => frame.Width != first.Width || frame.Height != first.Height))
            throw new NotSupportedException("Native viewports differ; select matching capture dimensions before this export.");
        Directory.CreateDirectory(config.OutputDirectory);
        var segments = streams.ToDictionary(pair => pair.Key, pair => WriteTimeline(config, pair.Key, pair.Value));
        var font = config.LabelFont.Replace('\\', '/').Replace(":", "\\:", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal);
        const int Header = 48;
        const int Footer = 40;
        var video = Path.Combine(config.OutputDirectory, "startup-sbs.mp4");
        var filter = $"[0:v]fps={config.FramesPerSecond},setpts=PTS-STARTPTS[l];[1:v]fps={config.FramesPerSecond},setpts=PTS-STARTPTS[r];" +
            $"[l][r]hstack=inputs=2:shortest=1,pad=iw:ih+{Header + Footer}:0:{Header}:color=0x10161f," +
            $"drawtext=fontfile='{font}':text='RETAIL - Fallout New Vegas':x=18:y=12:fontsize=24:fontcolor=0xffbf55," +
            $"drawtext=fontfile='{font}':text='OPENNV - current build':x={first.Width + 18}:y=12:fontsize=24:fontcolor=0x48bfff," +
            $"drawtext=fontfile='{font}':text='Shared capture clock | state alignment unverified | audio not recorded | inspect report for gaps':x=18:y=h-30:fontsize=20:fontcolor=white[out]";
        var start = new ProcessStartInfo(config.Encoder)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true,
        };
        foreach (var argument in new[] { "-hide_banner", "-loglevel", "error", "-nostdin", "-n", "-f", "concat", "-safe", "0", "-i", segments["retail"],
            "-f", "concat", "-safe", "0", "-i", segments["opennv"], "-filter_complex", filter, "-map", "[out]", "-t", Number(config.EndSeconds - config.BeginSeconds),
            "-an", "-c:v", "libx264", "-preset", "fast", "-crf", "18", "-pix_fmt", "yuv420p", "-threads", "4", "-movflags", "+faststart", video })
            start.ArgumentList.Add(argument);
        using var encoder = Process.Start(start) ?? throw new IOException("Could not start SBS encoder.");
        var error = encoder.StandardError.ReadToEnd();
        encoder.WaitForExit();
        File.WriteAllText(Path.Combine(config.OutputDirectory, "encoder.log"), error);
        if (encoder.ExitCode != 0) throw new IOException("SBS encoder failed; inspect encoder.log.");
        object Descriptor(string path)
        {
            using var input = File.OpenRead(path);
            return new { path, bytes = input.Length, sha256 = Convert.ToHexString(SHA256.HashData(input)) };
        }
        var report = new
        {
            schema = "opennv-shared-clock-sbs/v1", evidenceStatus = "pending-parity", config.SourceCommit,
            config.BeginSeconds, config.EndSeconds, config.FramesPerSecond,
            width = first.Width * 2, height = first.Height + Header + Footer, panelY = Header, panelWidth = first.Width, panelHeight = first.Height,
            alignment = "shared capture clock only; game-state and event alignment unverified", audio = "not-recorded",
            policy = "Original native dimensions; common start/end; no per-engine time offset, speed change, crop, hue or exposure adjustment. Frame durations derive from the source index; output samples that timeline at the configured rate.",
            configuration = Descriptor(configurationPath), video = Descriptor(video),
            recording = status.RootElement.Clone(),
            timeline = Descriptor(Path.Combine(config.RecordingDirectory, "timeline.jsonl")),
            streams = streams.Select(pair => new
            {
                engine = pair.Key, source = Descriptor(Path.Combine(config.RecordingDirectory, pair.Key, "frames.jsonl")),
                edit = Descriptor(segments[pair.Key]), frames = pair.Value.Length,
                longestSourceInterval = pair.Value.Zip(pair.Value.Skip(1), (left, right) => right.Seconds - left.Seconds).DefaultIfEmpty().Max(),
            }).ToArray(),
        };
        File.WriteAllText(Path.Combine(config.OutputDirectory, "report.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions(Program.Json) { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(new { video, report = Path.Combine(config.OutputDirectory, "report.json"), status = "pending-parity" }, Program.Json));
    }

    private static Frame[] Read(HarnessSbsConfiguration config, string engine)
    {
        var frames = File.ReadLines(Path.Combine(config.RecordingDirectory, engine, "frames.jsonl"))
            .Select(line => JsonSerializer.Deserialize<Frame>(line, Program.Json) ?? throw new InvalidDataException("Null native frame.")).ToArray();
        if (frames.Length < 2 || frames[0].Seconds > config.BeginSeconds || frames[^1].Seconds < config.EndSeconds)
            throw new InvalidDataException($"{engine} does not cover the entire requested shared interval.");
        if (frames.Any(frame => !double.IsFinite(frame.Seconds) || frame.Image != Path.GetFileName(frame.Image)) ||
            frames.Zip(frames.Skip(1), (left, right) => right.Index != left.Index + 1 || right.Seconds <= left.Seconds || right.Sequence <= left.Sequence).Any(invalid => invalid))
            throw new InvalidDataException($"{engine} frame index is invalid or not monotonic.");
        var before = Array.FindLastIndex(frames, frame => frame.Seconds <= config.BeginSeconds);
        return frames.Skip(before).TakeWhile(frame => frame.Seconds <= config.EndSeconds).ToArray();
    }

    private static string WriteTimeline(HarnessSbsConfiguration config, string engine, Frame[] frames)
    {
        var path = Path.Combine(config.OutputDirectory, engine + ".ffconcat");
        using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
        writer.WriteLine("ffconcat version 1.0");
        void FileEntry(Frame frame)
        {
            var image = Path.Combine(config.RecordingDirectory, engine, frame.Image);
            if (!File.Exists(image)) throw new FileNotFoundException("Recorded native frame is missing.", image);
            writer.WriteLine("file '" + image.Replace('\\', '/').Replace("'", "'\\''", StringComparison.Ordinal) + "'");
            writer.WriteLine("option framerate 1000000");
        }
        for (var index = 0; index < frames.Length; ++index)
        {
            FileEntry(frames[index]);
            var begin = Math.Max(config.BeginSeconds, frames[index].Seconds);
            var end = index + 1 < frames.Length ? frames[index + 1].Seconds : config.EndSeconds;
            writer.WriteLine("duration " + Number(end - begin));
        }
        FileEntry(frames[^1]);
        return path;
    }
    private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);
}
