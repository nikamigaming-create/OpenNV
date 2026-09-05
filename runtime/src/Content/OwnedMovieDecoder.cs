using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Threading.Channels;

namespace OpenNV.Runtime.Content;

internal sealed record OwnedMovieInfo(
    int Width, int Height, long RateNumerator, long RateDenominator,
    long FrameCount, int AudioRate, int AudioChannels)
{
    internal double FrameSeconds => (double)RateDenominator / RateNumerator;
    internal double DurationSeconds => FrameCount * FrameSeconds;
}

internal sealed record OwnedMovieVideoFrame(long Index, byte[] Rgba);

/// <summary>
/// Decodes an owned movie directly to bounded memory queues. The codec helper
/// never creates a transformed asset or a persistent movie cache.
/// </summary>
internal sealed class OwnedMovieDecoder : IDisposable, IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly List<Process> _processes = [];
    private readonly object _processLock = new();
    private readonly Func<ProcessStartInfo, Process?> _startProcess;
    private Task? _opening;
    private Task _videoTask = Task.CompletedTask;
    private Task _audioTask = Task.CompletedTask;
    private Task? _disposal;
    private Exception? _failure;
    private bool _disposed;
    private readonly Channel<OwnedMovieVideoFrame> _video = Channel.CreateBounded<OwnedMovieVideoFrame>(3);
    private readonly Channel<float[]> _audio = Channel.CreateBounded<float[]>(8);

    internal OwnedMovieInfo Info { get; private set; } = null!;
    internal ChannelReader<OwnedMovieVideoFrame> Video => _video.Reader;
    internal ChannelReader<float[]> Audio => _audio.Reader;
    internal Exception? Failure => Volatile.Read(ref _failure) ??
        _videoTask.Exception?.GetBaseException() ?? _audioTask.Exception?.GetBaseException();
    internal bool DecodingComplete => _videoTask.IsCompletedSuccessfully && _audioTask.IsCompletedSuccessfully &&
        _opening?.IsCompletedSuccessfully == true && Failure is null;

    internal OwnedMovieDecoder(Func<ProcessStartInfo, Process?>? startProcess = null)
    {
        _startProcess = startProcess ?? Process.Start;
    }

    internal Task OpenAsync(string filePath)
    {
        lock (_processLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_opening is not null)
                throw new InvalidOperationException("A movie decoder can only open one source.");
            return _opening = OpenCoreAsync(filePath);
        }
    }

    private async Task OpenCoreAsync(string filePath)
    {
        try { await ProbeAndStartAsync(filePath).ConfigureAwait(false); }
        catch (Exception error)
        {
            CompleteFailure(error);
            throw;
        }
    }

    private async Task ProbeAndStartAsync(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Owned movie is missing.", filePath);
        var probe = Start("ffprobe", ["-v", "error", "-show_streams", "-of", "json", filePath]);
        var errorTask = probe.StandardError.ReadToEndAsync();
        string json;
        try
        {
            json = await probe.StandardOutput.ReadToEndAsync(_stop.Token).ConfigureAwait(false);
            await probe.WaitForExitAsync(_stop.Token).ConfigureAwait(false);
            if (probe.ExitCode != 0)
                throw new InvalidDataException($"Movie metadata failed: {await errorTask.ConfigureAwait(false)}");
        }
        finally { await ReleaseProcessAsync(probe, errorTask).ConfigureAwait(false); }
        using var document = JsonDocument.Parse(json);
        var streams = document.RootElement.GetProperty("streams").EnumerateArray().ToArray();
        var video = streams.Single(stream => stream.GetProperty("codec_type").GetString() == "video");
        if (video.GetProperty("codec_name").GetString() != "binkvideo")
            throw new NotSupportedException("PlayBink requires a Bink video stream.");
        var rate = video.GetProperty("r_frame_rate").GetString()!.Split('/');
        var timeBase = video.GetProperty("time_base").GetString()!.Split('/');
        var rateNumerator = long.Parse(rate[0], CultureInfo.InvariantCulture);
        var rateDenominator = long.Parse(rate[1], CultureInfo.InvariantCulture);
        if (rateNumerator <= 0 || rateDenominator <= 0 ||
            long.Parse(timeBase[0], CultureInfo.InvariantCulture) != rateDenominator ||
            long.Parse(timeBase[1], CultureInfo.InvariantCulture) != rateNumerator ||
            video.GetProperty("start_pts").GetInt64() != 0)
            throw new InvalidDataException("Bink time base must identify consecutive frames from zero.");
        var audio = streams.Where(stream => stream.GetProperty("codec_type").GetString() == "audio").ToArray();
        if (audio.Length > 1)
            throw new NotSupportedException("Bink audio-track selection has no runtime owner yet.");
        Info = new OwnedMovieInfo(video.GetProperty("width").GetInt32(), video.GetProperty("height").GetInt32(),
            rateNumerator, rateDenominator, video.GetProperty("duration_ts").GetInt64(),
            audio.Length == 0 ? 0 : int.Parse(audio[0].GetProperty("sample_rate").GetString()!, CultureInfo.InvariantCulture),
            audio.Length == 0 ? 0 : audio[0].GetProperty("channels").GetInt32());
        if (Info.Width <= 0 || Info.Height <= 0 || Info.RateNumerator <= 0 || Info.RateDenominator <= 0 ||
            Info.FrameCount <= 0 || (Info.AudioChannels != 0 && (Info.AudioChannels is < 1 or > 2 || Info.AudioRate <= 0)))
            throw new NotSupportedException("Bink dimensions, timing, or channel layout are not supported.");
        _ = checked(Info.Width * Info.Height * 4);
        var videoProcess = Start("ffmpeg", ["-v", "error", "-xerror", "-nostdin", "-i", filePath,
            "-map", "0:v:0", "-an", "-fps_mode", "passthrough", "-pix_fmt", "rgba", "-f", "rawvideo", "pipe:1"]);
        _videoTask = DecodeVideoAsync(videoProcess);
        if (Info.AudioChannels == 0)
            _audio.Writer.TryComplete();
        else
        {
            var audioProcess = Start("ffmpeg", ["-v", "error", "-xerror", "-nostdin", "-i", filePath,
                "-map", "0:a:0", "-vn", "-c:a", "pcm_f32le", "-f", "f32le", "pipe:1"]);
            _audioTask = DecodeAudioAsync(audioProcess);
        }
    }

    private Process Start(string executable, string[] arguments)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        lock (_processLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _stop.Token.ThrowIfCancellationRequested();
            var process = _startProcess(start) ?? throw new InvalidOperationException($"Could not start {executable}.");
            _processes.Add(process);
            return process;
        }
    }

    private async Task DecodeVideoAsync(Process process)
    {
        var errorTask = process.StandardError.ReadToEndAsync();
        try
        {
            for (long index = 0; index < Info.FrameCount; ++index)
            {
                var pixels = new byte[checked(Info.Width * Info.Height * 4)];
                await process.StandardOutput.BaseStream.ReadExactlyAsync(pixels, _stop.Token).ConfigureAwait(false);
                await _video.Writer.WriteAsync(new OwnedMovieVideoFrame(index, pixels), _stop.Token).ConfigureAwait(false);
            }
            if (await process.StandardOutput.BaseStream.ReadAsync(new byte[1], _stop.Token).ConfigureAwait(false) != 0)
                throw new InvalidDataException("Bink video contains frames beyond its declared frame count.");
            await process.WaitForExitAsync(_stop.Token).ConfigureAwait(false);
            if (process.ExitCode != 0)
                throw new InvalidDataException($"Bink video decoder failed: {await errorTask.ConfigureAwait(false)}");
            _video.Writer.TryComplete();
        }
        catch (Exception error) { CompleteFailure(error); }
        finally { await ReleaseProcessAsync(process, errorTask).ConfigureAwait(false); }
    }

    private async Task DecodeAudioAsync(Process process)
    {
        var errorTask = process.StandardError.ReadToEndAsync();
        try
        {
            var stream = process.StandardOutput.BaseStream;
            var bytes = new byte[1024 * Info.AudioChannels * sizeof(float)];
            long sampleFrames = 0;
            while (true)
            {
                var count = 0;
                while (count < bytes.Length)
                {
                    var read = await stream.ReadAsync(bytes.AsMemory(count), _stop.Token).ConfigureAwait(false);
                    if (read == 0)
                        break;
                    count += read;
                }
                if (count == 0)
                    break;
                if (count % (Info.AudioChannels * sizeof(float)) != 0)
                    throw new InvalidDataException("Bink audio ended in a partial PCM frame.");
                var samples = new float[count / sizeof(float)];
                Buffer.BlockCopy(bytes, 0, samples, 0, count);
                if (samples.Any(sample => !float.IsFinite(sample)))
                    throw new InvalidDataException("Bink audio contains a non-finite PCM sample.");
                sampleFrames = checked(sampleFrames + samples.Length / Info.AudioChannels);
                await _audio.Writer.WriteAsync(samples, _stop.Token).ConfigureAwait(false);
            }
            await process.WaitForExitAsync(_stop.Token).ConfigureAwait(false);
            if (process.ExitCode != 0)
                throw new InvalidDataException($"Bink audio decoder failed: {await errorTask.ConfigureAwait(false)}");
            if (sampleFrames == 0)
                throw new InvalidDataException("Bink audio stream contains no PCM frames.");
            _audio.Writer.TryComplete();
        }
        catch (Exception error) { CompleteFailure(error); }
        finally { await ReleaseProcessAsync(process, errorTask).ConfigureAwait(false); }
    }

    private void CompleteFailure(Exception error)
    {
        if (_stop.IsCancellationRequested)
            return;
        Interlocked.CompareExchange(ref _failure, error, null);
        _video.Writer.TryComplete(Failure);
        _audio.Writer.TryComplete(Failure);
        StopProcesses();
    }

    public void Dispose()
    {
        lock (_processLock)
        {
            if (_disposed)
                return;
            _disposed = true;
            StopProcesses();
            _disposal = ReleaseAsync();
        }
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return new ValueTask(_disposal!);
    }

    private void StopProcesses()
    {
        lock (_processLock)
        {
            _stop.Cancel();
            foreach (var process in _processes)
                KillIfRunning(process);
        }
    }

    private static void KillIfRunning(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { } // The process exited during the check.
    }

    private async Task ReleaseProcessAsync(Process process, Task<string> standardError)
    {
        lock (_processLock)
            KillIfRunning(process);
        try
        {
            await process.WaitForExitAsync().ConfigureAwait(false);
            await standardError.ConfigureAwait(false);
        }
        finally
        {
            lock (_processLock)
            {
                _processes.Remove(process);
                process.Dispose();
            }
        }
    }

    private async Task ReleaseAsync()
    {
        if (_opening is not null)
        {
            try { await _opening.ConfigureAwait(false); }
            catch (Exception) { } // OpenAsync already reports the original error.
        }
        try { await Task.WhenAll(_videoTask, _audioTask).ConfigureAwait(false); }
        catch (Exception error) { Interlocked.CompareExchange(ref _failure, error, null); }
        finally
        {
            _video.Writer.TryComplete(Failure);
            _audio.Writer.TryComplete(Failure);
            _stop.Dispose();
        }
    }
}
