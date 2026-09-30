namespace OpenNV.Runtime.Content;

internal sealed partial class FalloutUiComponentStore
{
    private sealed class Animation(float start, float end, double duration, int mode)
    {
        internal readonly float Start = start, End = end;
        internal readonly double Duration = duration;
        internal readonly int Mode = mode;
        internal double Elapsed;
    }

    private readonly Dictionary<string, Animation> _animations = new(StringComparer.OrdinalIgnoreCase);

    internal bool SetFloatGradual(string path, float? start = null, float? end = null,
        double? duration = null, int mode = 0)
    {
        if (start is { } first && !float.IsFinite(first) || end is { } last && !float.IsFinite(last))
            throw new InvalidDataException("UI animation endpoints must be finite.");
        if (duration is { } seconds && (!double.IsFinite(seconds) || seconds <= 0 || start is null || end is null))
            throw new InvalidDataException("UI animation needs two endpoints and a positive finite duration.");
        if (mode is < 0 or > 3) throw new NotSupportedException("UI animation mode has no interpolation owner.");
        if (!TrySplitTrait(path, out var segments, out var trait)) return false;
        var tile = ResolveTile(segments, alt: true);
        if (tile is null || IsDetached(tile)) return false;
        var key = Key(tile, trait);
        _animations.Remove(key);
        if (start is { } value) WriteFloat(key, value);
        if (duration is { } period) _animations.Add(key, new(start!.Value, end!.Value, period, mode));
        return true;
    }

    // Presentation elapsed time, independent of quest/game time and Time Mult.
    // Menu pause does not suspend tile animations; unloading/resetting does.
    internal void AdvanceAnimations(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        if (seconds == 0 || _animations.Count == 0) return;
        foreach (var (key, animation) in _animations.ToArray())
        {
            var repeated = animation.Mode is 2 or 3;
            var remaining = animation.Duration - animation.Elapsed;
            var ended = !repeated && seconds >= remaining;
            var advance = seconds % animation.Duration;
            animation.Elapsed = repeated
                ? advance >= remaining ? advance - remaining : animation.Elapsed + advance
                : ended ? animation.Duration : animation.Elapsed + seconds;
            var phase = animation.Elapsed / animation.Duration;
            var amount = animation.Mode switch
            {
                0 or 3 => phase,
                1 => phase < 1d / 6 ? phase * 6 : phase <= 5d / 6 ? 1 : (1 - phase) * 6,
                _ => phase <= 0.5 ? phase * 2 : (1 - phase) * 2,
            };
            // Interpolate in double so finite opposite float endpoints cannot
            // overflow the intermediate subtraction.
            WriteFloat(key, (float)(animation.Start * (1 - amount) + animation.End * amount));
            if (ended) _animations.Remove(key);
        }
    }

    private void WriteFloat(string key, float value)
    {
        if (_floatOverrides.TryGetValue(key, out var previous) && previous == value) return;
        _floatOverrides[key] = value;
        ++Revision;
    }
}
