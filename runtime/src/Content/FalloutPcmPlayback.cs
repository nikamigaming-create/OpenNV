namespace OpenNV.Runtime.Content;

internal sealed record FalloutPcmPlaybackSnapshot(int Frames, int SourceRate, int Channels, int OutputRate,
    FalloutSoundLoop Loop, double Position, long Loops, bool Playing, bool Releasing, bool StartPending = false)
{
    internal void Validate()
    {
        if (Frames <= 0 || SourceRate <= 0 || Channels is not (1 or 2) || OutputRate <= 0 || Loop is null ||
            !Enum.IsDefined(Loop.Mode) || Loop.Start >= Frames || Loop.End > Frames ||
            Loop.Mode != FalloutSoundLoopMode.None && Loop.End != 0 && Loop.End <= Loop.Start ||
            !double.IsFinite(Position) || Position < 0 || Position > Frames || Loops < 0 ||
            StartPending && Playing ||
            Releasing && Loop.Mode is not (FalloutSoundLoopMode.EnvelopeFast or FalloutSoundLoopMode.EnvelopeSlow))
            throw new InvalidDataException("Saved PCM playback has an invalid sample clock or loop region.");
    }
}

// This owner has no engine read-ahead buffer. The fractional source position is
// the complete interpolation state at the next output sample.
internal sealed class FalloutPcmPlayback
{
    private readonly object _gate = new();
    private readonly float[] _samples;
    private readonly int _channels, _sourceRate, _outputRate, _frames;
    private readonly FalloutSoundLoop _loop;
    private double _position;
    private long _loops;
    private bool _playing, _releasing, _restored;
    private bool _suspended;
    private bool _startPending = true;
    internal string? Error { get; private set; }

    internal FalloutPcmPlayback(float[] samples, int channels, int sourceRate, int outputRate,
        FalloutSoundLoop loop, FalloutPcmPlaybackSnapshot? saved = null)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (channels is not (1 or 2) || samples.Length == 0 || samples.Length % channels != 0 ||
            samples.Any(value => !float.IsFinite(value))) throw new InvalidDataException("PCM samples have invalid channels or values.");
        _samples = samples; _channels = channels; _sourceRate = sourceRate; _outputRate = outputRate;
        _frames = samples.Length / channels; _loop = loop;
        new FalloutPcmPlaybackSnapshot(_frames, sourceRate, channels, outputRate, loop, 0, 0, false, false).Validate();
        if (saved is null) return;
        saved.Validate();
        if (saved.Frames != _frames || saved.SourceRate != sourceRate || saved.Channels != channels || saved.Loop != loop)
            throw new InvalidDataException("Saved PCM playback differs from its decoded owned source.");
        _position = saved.Position; _loops = saved.Loops; _playing = saved.Playing;
        _releasing = saved.Releasing; _restored = true;
        _startPending = saved.StartPending;
    }

    internal FalloutPcmPlaybackSnapshot Capture()
    {
        lock (_gate)
        {
            if (Error is not null) throw new InvalidDataException("PCM mixer failed: " + Error);
            var state = new FalloutPcmPlaybackSnapshot(_frames, _sourceRate, _channels, _outputRate,
                _loop, _position, _loops, _playing, _releasing, _startPending);
            state.Validate(); return state;
        }
    }

    internal void RequireHealthy()
    {
        lock (_gate)
            if (Error is not null) throw new InvalidDataException("PCM mixer failed: " + Error);
    }
    internal void SetSuspended(bool suspended) { lock (_gate) _suspended = suspended; }

    internal double Query(int operation, double value)
    {
        lock (_gate)
        {
            switch (operation)
            {
                case 0:
                    if (_restored && value == 0)
                    {
                        _restored = false;
                        if (_startPending) { _startPending = false; _playing = true; }
                        return 0;
                    }
                    if (_startPending && value == 0) { _startPending = false; _playing = true; return 0; }
                    _restored = false; Seek(value); _startPending = false; _playing = true; _releasing = false; _loops = 0; return 0;
                case 1: _startPending = false; _playing = false; return 0;
                case 2: return _playing && Error is null ? 1 : 0;
                case 3: return _position / _sourceRate;
                case 4: Seek(value); return 0;
                case 5: return _loops;
                default: throw new InvalidDataException("Unknown PCM playback operation.");
            }
        }
    }

    internal void ReleaseEnvelope()
    {
        lock (_gate)
        {
            if (_loop.Mode is not (FalloutSoundLoopMode.EnvelopeFast or FalloutSoundLoopMode.EnvelopeSlow))
                throw new InvalidOperationException("PCM voice has no source release envelope.");
            _releasing = true;
            if (_loop.Mode == FalloutSoundLoopMode.EnvelopeFast) _position = _loop.End == 0 ? _frames : _loop.End;
        }
    }

    private void Seek(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0) throw new InvalidDataException("PCM seek has an invalid time.");
        _position = Math.Min(seconds * _sourceRate, _frames);
    }

    internal void Fail(Exception error) { lock (_gate) { Error ??= error.Message; _playing = false; } }

    internal unsafe int Mix(float* stereo, float rateScale, int count)
    {
        if (count < 0 || stereo == null || !float.IsFinite(rateScale) || rateScale <= 0)
            throw new InvalidDataException("Native PCM mix has an invalid buffer or rate.");
        lock (_gate)
        {
            if (_playing && Error is null && _suspended)
            {
                for (var index = 0; index < count * 2; index++) stereo[index] = 0;
                return count;
            }
            var written = 0;
            var step = (double)_sourceRate / _outputRate * rateScale;
            var end = _loop.End == 0 ? _frames : checked((int)_loop.End);
            var looping = _loop.Mode != FalloutSoundLoopMode.None && !_releasing;
            if (_playing && Error is null)
            {
                for (; written < count; written++)
                {
                    if (looping && _position >= end)
                    {
                        var turns = Math.Floor((_position - _loop.Start) / (end - _loop.Start));
                        _position -= turns * (end - _loop.Start); _loops = checked(_loops + (long)turns);
                    }
                    if (_position >= _frames) { _position = _frames; _playing = false; break; }
                    var current = (int)_position;
                    var next = current + 1;
                    if (looping && next == end) next = checked((int)_loop.Start);
                    else next = Math.Min(next, _frames - 1);
                    var fraction = (float)(_position - current);
                    var left = _samples[current * _channels];
                    var right = _samples[current * _channels + _channels - 1];
                    stereo[written * 2] = left + (_samples[next * _channels] - left) * fraction;
                    stereo[written * 2 + 1] = right + (_samples[next * _channels + _channels - 1] - right) * fraction;
                    _position += step;
                }
                // Normalize at the boundary, including an exact final sample.
                if (looping && _position >= end)
                {
                    var turns = Math.Floor((_position - _loop.Start) / (end - _loop.Start));
                    _position -= turns * (end - _loop.Start); _loops = checked(_loops + (long)turns);
                }
                else if (!looping && _position >= _frames) { _position = _frames; _playing = false; }
            }
            for (var index = written * 2; index < count * 2; index++) stereo[index] = 0;
            return written;
        }
    }
}
