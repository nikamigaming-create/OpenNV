using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using OpenNV.Runtime.Diagnostics.Parity;

namespace OpenNV.LiveHarness;

// Preserve the received native stream before the display mailbox can replace it.
// Every source timestamp and transport gap is retained; this does not claim that
// the producer's latest-frame transport observes every engine draw or audio event.
internal sealed class HarnessRecording
{
    private readonly Dictionary<string, FrameArchive> _streams;
    private readonly long _begin = Now();
    private long _end;
    private readonly object _journalLock = new();
    private readonly StreamWriter _journal;
    private Task? _completion;
    private volatile bool _finished;
    private readonly List<string> _failures = [];
    internal string DirectoryPath { get; }
    internal bool Active => _completion is null;
    internal bool Finished => _finished;

    internal HarnessRecording(string directory, string encoder, object initialState)
    {
        if (!Path.IsPathFullyQualified(directory) || Directory.Exists(directory))
            throw new ArgumentException("Recording requires a new absolute private directory.");
        if (!Path.IsPathFullyQualified(encoder) || !File.Exists(encoder))
            throw new ArgumentException("Recording requires an installed ffmpeg executable path.");
        DirectoryPath = directory;
        Directory.CreateDirectory(directory);
        _journal = new StreamWriter(Path.Combine(directory, "timeline.jsonl"), false, new UTF8Encoding(false)) { AutoFlush = true };
        _streams = new[] { "retail", "opennv" }.ToDictionary(target => target,
            target => new FrameArchive(Path.Combine(directory, target), encoder, _begin));
        Journal("recording-start", initialState);
        WriteStatus();
    }

    internal object Status => new
    {
        directory = DirectoryPath, active = Active, finished = Finished,
        beginNanoseconds = _begin, seconds = ((Interlocked.Read(ref _end) is var end && end != 0 ? end : Now()) - _begin) / 1e9,
        audio = "not-recorded", alignment = "unestablished",
        failures = Failures(),
        coverage = "received-native-frames-with-explicit-transport-gaps",
        streams = _streams.ToDictionary(pair => pair.Key, pair => pair.Value.Status),
    };

    internal void Accept(string target, LiveHarnessSurface frame)
    {
        if (Active) _streams[target].Accept(frame);
    }

    internal void Fail(string reason)
    {
        lock (_journalLock)
        {
            if (_failures.Contains(reason)) return;
            _failures.Add(reason);
            Journal("failure", reason);
        }
    }

    private string[] Failures()
    {
        lock (_journalLock) return _failures.ToArray();
    }

    internal void Journal(string kind, object value)
    {
        lock (_journalLock)
        {
            if (_completion is not null) return;
            _journal.WriteLine(JsonSerializer.Serialize(new
            {
                seconds = (Now() - _begin) / 1e9, nanoseconds = Now(), kind, value,
            }, Program.Json));
        }
    }

    internal Task Stop()
    {
        lock (_journalLock)
        {
            if (_completion is not null) return _completion;
            Interlocked.Exchange(ref _end, Now());
            _journal.Dispose();
            _completion = Task.Run(async () =>
            {
                await Task.WhenAll(_streams.Values.Select(stream => stream.Stop()));
                _finished = true;
                WriteStatus();
            });
            return _completion;
        }
    }

    private void WriteStatus() => File.WriteAllText(Path.Combine(DirectoryPath, "recording.json"),
        JsonSerializer.Serialize(Status, new JsonSerializerOptions(Program.Json) { WriteIndented = true }));
    private static long Now() => checked((long)(Stopwatch.GetTimestamp() * (1_000_000_000.0 / Stopwatch.Frequency)));

    private sealed class FrameArchive
    {
        private readonly Channel<LiveHarnessSurface> _pending = Channel.CreateBounded<LiveHarnessSurface>(
            new BoundedChannelOptions(48) { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
        private readonly string _directory;
        private readonly string _encoder;
        private readonly long _begin;
        private readonly Task _writer;
        private long _received;
        private long _written;
        private long _lastSequence;
        private long _transportGaps;
        private long _overflow;
        private string? _error;

        internal FrameArchive(string directory, string encoder, long begin)
        {
            _directory = directory; _encoder = encoder; _begin = begin;
            Directory.CreateDirectory(directory);
            _writer = Task.Run(Write);
        }

        internal object Status => new
        {
            received = Interlocked.Read(ref _received), written = Interlocked.Read(ref _written),
            transportFramesMissing = Interlocked.Read(ref _transportGaps),
            queueOverflow = Interlocked.Read(ref _overflow), error = _error,
        };

        internal void Accept(LiveHarnessSurface frame)
        {
            if (frame.Sequence <= _lastSequence) return;
            if (_lastSequence != 0 && frame.Sequence > _lastSequence + 2)
                Interlocked.Add(ref _transportGaps, (frame.Sequence - _lastSequence) / 2 - 1);
            _lastSequence = frame.Sequence;
            Interlocked.Increment(ref _received);
            if (!_pending.Writer.TryWrite(frame))
            {
                Interlocked.Increment(ref _overflow);
                _error = "Recording queue overflow: frame coverage failed. Inspect the retained sequence/timestamp index.";
            }
        }

        internal async Task Stop()
        {
            _pending.Writer.TryComplete();
            await _writer;
            if (Interlocked.Read(ref _received) == 0) _error = "No native frames were received from this engine.";
        }

        private async Task Write()
        {
            Process? encoder = null;
            Task<string>? errors = null;
            LiveHarnessSurface? first = null;
            using var index = new StreamWriter(Path.Combine(_directory, "frames.jsonl"), false, new UTF8Encoding(false)) { AutoFlush = true };
            try
            {
                await foreach (var frame in _pending.Reader.ReadAllAsync())
                {
                    if (first is null)
                    {
                        first = frame;
                        var start = new ProcessStartInfo(_encoder)
                        {
                            UseShellExecute = false, CreateNoWindow = true,
                            RedirectStandardInput = true, RedirectStandardError = true,
                        };
                        var format = frame.Format switch { 1 => "rgb24", 2 => "rgba", 3 => "bgr0", 4 => "bgra", _ => throw new InvalidDataException("Unknown frame format.") };
                        foreach (var argument in new[] { "-hide_banner", "-loglevel", "error", "-f", "rawvideo", "-pixel_format", format,
                            "-video_size", $"{frame.Width}x{frame.Height}", "-framerate", "60", "-i", "pipe:0", "-fps_mode", "passthrough",
                            "-c:v", "png", "-compression_level", "1", "-threads", "2", "-start_number", "0", Path.Combine(_directory, "%09d.png") })
                            start.ArgumentList.Add(argument);
                        encoder = Process.Start(start) ?? throw new IOException("Could not start frame encoder.");
                        errors = encoder.StandardError.ReadToEndAsync();
                    }
                    if (first.Width != frame.Width || first.Height != frame.Height || first.Format != frame.Format)
                        throw new InvalidDataException("Native frame format changed during recording; start a new segment.");
                    var number = Interlocked.Read(ref _written);
                    var rowBytes = frame.Width * (frame.Format == 1 ? 3 : 4);
                    if (rowBytes == frame.Pitch) await encoder!.StandardInput.BaseStream.WriteAsync(frame.Bytes);
                    else for (var row = 0; row < frame.Height; row++)
                            await encoder!.StandardInput.BaseStream.WriteAsync(frame.Bytes.AsMemory(row * frame.Pitch, rowBytes));
                    index.WriteLine(JsonSerializer.Serialize(new
                    {
                        index = number, frame.Sequence, frame.Draw, frame.Nanoseconds,
                        seconds = (frame.Nanoseconds - _begin) / 1e9,
                        frame.Width, frame.Height, frame.Pitch, frame.Format,
                        sourceSha256 = Convert.ToHexString(SHA256.HashData(frame.Bytes)),
                        image = $"{number:D9}.png",
                    }, Program.Json));
                    Interlocked.Increment(ref _written);
                }
            }
            catch (Exception exception)
            {
                _error = exception.Message;
                _pending.Writer.TryComplete(exception);
            }
            finally
            {
                if (encoder is not null)
                {
                    encoder.StandardInput.Close();
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                    try
                    {
                        await encoder.WaitForExitAsync(timeout.Token);
                        var output = await errors!;
                        if (encoder.ExitCode != 0) _error = $"Frame encoder exited {encoder.ExitCode}: {output}";
                    }
                    catch (OperationCanceledException) { encoder.Kill(); _error = "Frame encoder did not finalize in time."; }
                    encoder.Dispose();
                }
                var files = Directory.EnumerateFiles(_directory, "*.png").LongCount();
                if (files != Interlocked.Read(ref _written))
                    _error = $"Encoded frame count differs: {files} files / {_written} indexed frames. {_error}";
            }
        }
    }
}
