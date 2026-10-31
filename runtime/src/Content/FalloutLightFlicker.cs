namespace OpenNV.Runtime.Content;

internal readonly record struct FalloutLightFlickerSample(float X, float Y, float Z, float Intensity);

// Actual point-light lifetime owns these phases. Material/controller animation
// is independent; this owner changes the light's local offset and dimmer only.
internal sealed class FalloutLightFlicker
{
    internal const uint Flags = 0x48;
    private const double Period = (double)(float)(2 * Math.PI);
    private readonly bool _slow;
    private readonly float _movement;
    private readonly Func<uint> _draw;
    internal float PhaseX { get; private set; }
    internal float PhaseY { get; private set; } = 1;
    internal float PhaseZ { get; private set; }

    internal FalloutLightFlicker(uint flags, float movement, Func<uint> draw)
    {
        if ((flags & Flags) == 0 || (flags & ~Flags) != 0)
            throw new NotSupportedException("Point-light animation requires its source flicker flags.");
        if (!float.IsFinite(movement)) throw new InvalidDataException("Source flicker movement is nonfinite.");
        _slow = (flags & 0x40) != 0;
        _movement = movement;
        _draw = draw ?? throw new ArgumentNullException(nameof(draw));
    }

    internal FalloutLightFlickerSample Advance(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0 || seconds > float.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(seconds));
        var delta = (float)seconds;
        if (_slow) delta = (float)(delta / 2.0);
        // Three full unsigned process draws, including a zero-delta update.
        PhaseX = Phase(PhaseX, _draw(), 357913952f, 1.1f, delta);
        PhaseY = Phase(PhaseY, _draw(), 357913952f, 1.2f, delta);
        PhaseZ = Phase(PhaseZ, _draw(), 238609296f, 1.3f, delta);
        var x = Sin((float)((PhaseX + (double)1.7f) * 2));
        var y = Sin((float)((PhaseY + .5) * 1.7f));
        var z = Sin((float)((PhaseZ + (double).3f) * 2.6f));
        var offset = (float)(_movement * (double)x * y / 2);
        var amplitude = _movement + (double)offset > 0 ? offset : 0;
        var sum = Sin((float)((PhaseZ + (double).3f) * 3)) / 5.0 +
            ((Sin((float)((PhaseY + .5) * 1.3f)) + 1.0) / 2 *
                (Sin((float)((PhaseX + (double)1.7f) * 1.1f)) + 1.0) / 2 / 3);
        var bounded = Math.Clamp((float)sum, -1, 1);
        var normal = (float)(bounded * .5 + .5);
        var intensity = (float)(normal * (double).4f + .6f);
        return new((float)(x * (double)amplitude), (float)(y * (double)amplitude),
            (float)(z * (double)amplitude), intensity);
    }

    private static float Phase(float previous, uint random, float divisor, float rate, float delta)
    {
        var phase = (float)((random / (double)divisor + rate) * delta + previous);
        if (!float.IsFinite(phase)) throw new InvalidDataException("Source flicker phase overflowed.");
        // The source performs one strict wrap, not an arbitrary modulo.
        return phase > Period ? (float)(phase - Period) : phase;
    }
    private static float Sin(float angle) => (float)Math.Sin(angle);
}
