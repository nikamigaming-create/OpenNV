namespace OpenNV.Runtime.Content;

// A placed-light lifetime retains an absolute dimmer and target. Pulse uses
// source frame steps and proximity switches, not a sine or period clock.
internal sealed class FalloutLightPulse
{
    internal const uint Flags = 0x180;
    private readonly float _sourceIntensity, _rate;
    internal float Intensity { get; private set; }
    internal float Target { get; private set; } = 1;

    internal FalloutLightPulse(uint flags, float intensity)
    {
        if ((flags & Flags) == 0 || (flags & ~Flags) != 0)
            throw new NotSupportedException("Point-light pulse requires its source pulse flags.");
        if (!float.IsFinite(intensity) || intensity <= 0) throw new InvalidDataException("Source pulse intensity is invalid.");
        _sourceIntensity = Intensity = intensity;
        _rate = (flags & 0x100) != 0 ? .05f : .1f;
    }

    internal float Advance(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0 || seconds > float.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(seconds));
        var step = _rate * (double)(float)seconds;
        Intensity = (float)(Intensity + (Target > Intensity ? step : -step));
        if (!float.IsFinite(Intensity)) throw new InvalidDataException("Source pulse dimmer overflowed.");
        // The threshold is the rate, not the frame step. Equality steps down;
        // proximity is strict. Retain a large-frame overshoot without clamping.
        if (MathF.Abs((float)(Intensity - (double)Target)) < _rate)
            Target = Target > .5f ? (float)(_sourceIntensity * .25) : _sourceIntensity;
        return Intensity;
    }
}
