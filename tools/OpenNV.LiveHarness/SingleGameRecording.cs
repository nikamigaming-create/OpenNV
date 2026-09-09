using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using OpenNV.Runtime.Diagnostics.Parity;

namespace OpenNV.LiveHarness;

// A bounded ordinary-gameplay deliverable from the game's existing viewport
// stream and its own process audio. This is not a lossless parity recording.
internal static class SingleGameRecording
{
    internal static async Task Run(string channel, int processId, int seconds, string encoder, string output, string? stopSignal = null)
    {
        if (seconds is < 1 or > 1800 || !Path.IsPathFullyQualified(output) || File.Exists(output) ||
            !Path.IsPathFullyQualified(encoder) || !File.Exists(encoder) ||
            (stopSignal is not null && (!Path.IsPathFullyQualified(stopSignal) || File.Exists(stopSignal))))
            throw new ArgumentException("Recording needs 1-1800 seconds, an encoder, a fresh absolute output path and an optional fresh absolute stop-signal path.");
        using var temporary = new TemporaryCaptureDirectory(output + ".capture-" + Guid.NewGuid().ToString("N"));
        using var source = new LiveHarnessFrameBuffer(channel, false);
        using var cancellation = new CancellationTokenSource();
        var video = Path.Combine(temporary.Path, "video.mp4");
        var audioDirectory = Path.Combine(temporary.Path, "audio");
        var audio = Task.Run(() => HarnessProcessAudio.Capture(processId, seconds * 1000, audioDirectory, cancellation.Token));
        Process? writer = null;
        Task<string>? errors = null;
        var frames = new List<object>();
        var begin = Stopwatch.GetTimestamp();
        try
        {
            LiveHarnessSurface? format = null;
            LiveHarnessSurface? latest = null;
            for (var index = 0; index < seconds * 30; index++)
            {
                if (index > 0 && stopSignal is not null && File.Exists(stopSignal)) break;
                if (audio.IsFaulted) await audio;
                var due = TimeSpan.FromSeconds(index / 30d) - Stopwatch.GetElapsedTime(begin);
                if (due > TimeSpan.Zero) await Task.Delay(due);
                // A publication can overlap the seqlock read; wait for its completed
                // frame rather than treating an in-progress copy as a lost stream.
                var readDeadline = Stopwatch.GetTimestamp();
                var frame = source.ReadLatest() ?? latest;
                while (frame is null && Stopwatch.GetElapsedTime(readDeadline) < TimeSpan.FromSeconds(2))
                {
                    await Task.Delay(2);
                    frame = source.ReadLatest();
                }
                if (frame is null) throw new IOException("The gameplay viewport stream is absent.");
                // Loading stalls remain visible in an ordinary gameplay video.
                // Keep their repeated source timestamps in the sidecar as well.
                latest = frame;
                if (format is null)
                {
                    format = frame;
                    var pixelFormat = frame.Format switch { 1 => "rgb24", 2 => "rgba", 3 => "bgr0", 4 => "bgra", _ => throw new InvalidDataException("Unknown viewport format.") };
                    writer = Start(encoder, ["-hide_banner", "-loglevel", "error", "-f", "rawvideo", "-pixel_format", pixelFormat,
                        "-video_size", $"{frame.Width}x{frame.Height}", "-framerate", "30", "-i", "pipe:0", "-c:v", "libx264", "-preset", "veryfast",
                        "-crf", "18", "-pix_fmt", "yuv420p", "-movflags", "+faststart", video], true);
                    errors = writer.StandardError.ReadToEndAsync();
                }
                if (format.Width != frame.Width || format.Height != frame.Height || format.Format != frame.Format)
                    throw new IOException("Gameplay viewport format changed during capture.");
                var rowBytes = frame.Width * (frame.Format == 1 ? 3 : 4);
                if (rowBytes == frame.Pitch)
                    await writer!.StandardInput.BaseStream.WriteAsync(frame.Bytes);
                else
                    for (var row = 0; row < frame.Height; row++)
                        await writer!.StandardInput.BaseStream.WriteAsync(frame.Bytes.AsMemory(row * frame.Pitch, rowBytes));
                frames.Add(new { index, frame.Draw, frame.Sequence, frame.Nanoseconds });
            }
            cancellation.Cancel();
            writer!.StandardInput.Close(); await writer.WaitForExitAsync();
            if (writer.ExitCode != 0) throw new IOException(await errors!);
            await audio;
            using var audioReport = JsonDocument.Parse(File.ReadAllText(Path.Combine(audioDirectory, "audio.json")));
            var audioStart = audioReport.RootElement.GetProperty("beginQpc").GetInt64();
            var delay = (audioStart - begin) / (double)Stopwatch.Frequency;
            using var mux = Start(encoder, ["-hide_banner", "-loglevel", "error", "-i", video, "-itsoffset", delay.ToString("R", CultureInfo.InvariantCulture),
                "-i", Path.Combine(audioDirectory, "audio.wav"), "-map", "0:v:0", "-map", "1:a:0", "-c:v", "copy", "-c:a", "aac",
                "-t", (frames.Count / 30d).ToString("R", CultureInfo.InvariantCulture), "-movflags", "+faststart", output], false);
            var muxErrors = mux.StandardError.ReadToEndAsync(); await mux.WaitForExitAsync();
            if (mux.ExitCode != 0) throw new IOException(await muxErrors);
            File.WriteAllText(Path.ChangeExtension(output, ".json"), JsonSerializer.Serialize(new { processId, seconds = frames.Count / 30d, maximumSeconds = seconds, frames,
                audioDelaySeconds = delay, limitation = "Ordinary gameplay at 30 fps; repeated latest frames retained, exact audiovisual parity unverified." }, Program.Json));
            Console.WriteLine("OPENNV_SINGLE_GAME_RECORDING_COMPLETE " + output);
        }
        catch
        {
            if (writer is { HasExited: false }) { writer.Kill(); await writer.WaitForExitAsync(); }
            File.Delete(output);
            throw;
        }
        finally
        {
            cancellation.Cancel();
            writer?.Dispose();
            try { await audio; } catch { /* Capture owns its failed audio cleanup. */ }
        }
    }
    private static Process Start(string encoder, string[] arguments, bool input)
    {
        var start = new ProcessStartInfo(encoder) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = input, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        return Process.Start(start) ?? throw new IOException("Could not start video encoder.");
    }
}
