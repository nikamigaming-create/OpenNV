using System.Buffers.Binary;
using System.Diagnostics;
using Godot;

namespace OpenNV.Runtime.Diagnostics.Parity;

// One explicitly requested diagnostic interval. Capture effects copy samples
// without changing them and are removed on success, cancellation and failure.
internal sealed class RuntimeAudioTrace : IDisposable
{
    private readonly List<(string Name, AudioEffectCapture Effect)> _buses = [];
    private readonly long _begin = Stopwatch.GetTimestamp();
    private readonly float _mixRate = AudioServer.GetMixRate();
    private readonly List<MemoryStream> _samples = [];
    private readonly CancellationTokenSource _stop = new();
    private Task? _pump;
    private Exception? _pumpFailure;
    private bool _disposed;

    internal RuntimeAudioTrace(float bufferSeconds = 10)
    {
        if (!float.IsFinite(bufferSeconds) || bufferSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(bufferSeconds));
        try
        {
            for (var index = 0; index < AudioServer.BusCount; index++)
            {
                var effect = new AudioEffectCapture { BufferLength = bufferSeconds };
                _buses.Add((AudioServer.GetBusName(index).ToString(), effect));
                _samples.Add(new MemoryStream());
                AudioServer.AddBusEffect(index, effect);
            }
            // The decoder/resource snapshot can occupy the main thread for
            // seconds. Audio has its own clock and must continue draining.
            _pump = Task.Run(() =>
            {
                try
                {
                    while (!_stop.IsCancellationRequested)
                    {
                        Drain();
                        _stop.Token.WaitHandle.WaitOne(5);
                    }
                    Drain();
                }
                catch (Exception error) { _pumpFailure = error; }
            });
        }
        catch { Dispose(); throw; }
    }

    internal bool HasInterval => Stopwatch.GetElapsedTime(_begin).TotalSeconds >= .25;

    internal object Finish(RenderTraceBlobStore blobs, ICollection<string> missing)
    {
        Detach(missing);
        StopPump();
        var end = Stopwatch.GetTimestamp();
        if (_pumpFailure is not null) missing.Add("audio:sample-pump:" + _pumpFailure.Message);
        var buses = _buses.Select((bus, index) =>
        {
            var bytes = _samples[index].ToArray();
            var frames = bytes.Length / 8;
            var pushed = bus.Effect.GetPushedFrames();
            var discarded = bus.Effect.GetDiscardedFrames();
            if (discarded != 0 || pushed != frames)
                missing.Add($"audio:{bus.Name}:sample-loss:pushed={pushed},retained={frames},discarded={discarded}");
            if (frames == 0) missing.Add($"audio:{bus.Name}:no-observed-samples");
            return new
            {
                bus = bus.Name,
                encoding = "interleaved-stereo-ieee754-f32-le",
                mixRate = _mixRate,
                pushed,
                discarded,
                frames,
                samples = blobs.Put(bytes),
                observation = "bus-effect-output-before-bus-volume;no-endpoint-or-per-voice-sample-attribution",
            };
        }).ToArray();
        return new
        {
            beginNanoseconds = (long)((decimal)_begin * 1_000_000_000 / Stopwatch.Frequency),
            endNanoseconds = (long)((decimal)end * 1_000_000_000 / Stopwatch.Frequency),
            outputLatencySeconds = AudioServer.GetOutputLatency(),
            buses,
            alignment = "independent-audio-clock;exact-audio-frame-to-video-frame-join-unobserved",
        };
    }

    private void Drain()
    {
        for (var index = 0; index < _buses.Count; index++)
        {
            var available = _buses[index].Effect.GetFramesAvailable();
            if (available == 0) continue;
            var frames = _buses[index].Effect.GetBuffer(available);
            if (frames.Length != available) throw new InvalidDataException("Audio read returned a partial packet.");
            var bytes = new byte[checked(frames.Length * 8)];
            for (var i = 0; i < frames.Length; i++)
            {
                BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(i * 8), frames[i].X);
                BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(i * 8 + 4), frames[i].Y);
            }
            _samples[index].Write(bytes);
        }
    }

    private void StopPump()
    {
        _stop.Cancel();
        _pump?.GetAwaiter().GetResult();
        _pump = null;
    }

    private void Detach(ICollection<string>? missing)
    {
        foreach (var (name, effect) in _buses)
        {
            var found = false;
            for (var index = 0; index < AudioServer.BusCount; index++)
                for (var slot = AudioServer.GetBusEffectCount(index) - 1; slot >= 0; slot--)
                    if (AudioServer.GetBusEffect(index, slot).GetInstanceId() == effect.GetInstanceId())
                    {
                        AudioServer.RemoveBusEffect(index, slot);
                        found = true;
                    }
            if (!found) missing?.Add($"audio:{name}:capture-effect-disappeared-during-observation");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Detach(null);
        StopPump();
        foreach (var (_, effect) in _buses) effect.Dispose();
        _buses.Clear();
        foreach (var samples in _samples) samples.Dispose();
        _samples.Clear();
        _stop.Dispose();
    }
}
