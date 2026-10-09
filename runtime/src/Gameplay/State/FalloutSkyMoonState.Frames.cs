using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutSkyMoonState
{
    internal void Advance(FalloutMoonFrame frame)
    {
        BindThread();
        if (!float.IsFinite(frame.Hour) || !float.IsFinite(frame.NightAlpha) ||
            !float.IsFinite(frame.NightColor.X) || !float.IsFinite(frame.NightColor.Y) || !float.IsFinite(frame.NightColor.Z) ||
            frame.SkyMode != _sky.Mode || _climate is null || !_factoryEntered)
            throw new InvalidDataException("Moon frame changed actual clock/colour/mode/climate owners.");
        if (_moons.Values.Any(row => !_native.ContainsKey(row.Role)))
            throw new NotSupportedException("source-Moon-actual-native-field-publication-pending");
        _executing = true;
        try
        {
            // The source's day check is conditional on its retained bit and
            // strict unsigned comparison. The original Main forced bit is a
            // distinct input, never inferred from a renderer or Pause Boolean.
            if (((_sky.Flags & 0x20) != 0 && _storedDays < frame.DaysPassed) || frame.ForcePhase)
            {
                var phase = FalloutMoonMath.Phase(frame.DaysPassed, _climate.PhaseLength, _phase);
                if (_phase != phase)
                {
                    _phase = phase; Next();
                    foreach (var role in Enum.GetValues<FalloutMoonRole>())
                        if (_moons.TryGetValue(role, out var row) && row.Pending == 0)
                            _moons[role] = row with { Pending = frame.ForcePhase ? 2 : 1, Changed = Next() };
                }
                _storedDays = frame.DaysPassed; Next();
            }
            foreach (var role in Enum.GetValues<FalloutMoonRole>())
            {
                if (!_moons.TryGetValue(role, out var row)) continue;
                var angle = BitConverter.UInt32BitsToSingle(row.AngleBits);
                var previous = BitConverter.UInt32BitsToSingle(row.LastHourBits);
                if (row.LastHourBits == BitConverter.SingleToUInt32Bits(float.MaxValue))
                {
                    previous = 0; angle = 90;
                    row = row with { AngleBits = BitConverter.SingleToUInt32Bits(angle), LastHourBits = 0, Changed = Next() };
                    _moons[role] = row;
                }
                angle = FalloutMoonMath.Advance(Source, row.Settings, angle, previous, frame.Hour);
                row = row with
                {
                    AngleBits = BitConverter.SingleToUInt32Bits(angle),
                    LastHourBits = BitConverter.SingleToUInt32Bits(frame.Hour),
                    Changed = Next()
                };
                _moons[role] = row;
                var primary = FalloutMoonMath.Fade(Source, row.Settings, angle, shadow: false);
                var shadow = MathF.Min(FalloutMoonMath.Fade(Source, row.Settings, angle, shadow: true), frame.NightAlpha);
                if (!float.IsFinite(primary) || !float.IsFinite(shadow))
                    throw new NotSupportedException("Moon source fade reached an unowned non-finite arithmetic arm.");
                var native = _native[role];
                if (row.Pending == 2 || row.Pending == 1 && primary <= 0 && shadow <= 0)
                {
                    var path = Source.Texture(role, _phase);
                    var returned = native.LoadPhase(path);
                    if (returned.Moon != row.CapturedIdentity || returned.HasTexture != (returned.Loaded is not null) ||
                        returned.Loaded is { } loaded && !loaded.Path.Equals(path, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Moon texture loader returned another source phase/lifetime.");
                    // Empty New phase clears the geometry flag; the property
                    // sampler survives. Capture validates that separate field.
                    row = row with { Pending = 0, LastPhaseAttempt = _phase, Native = native.Capture(), Changed = Next() };
                    _moons[role] = row;
                }
                native.Apply(frame, angle, primary, shadow);
                _moons[role] = row with { Native = native.Capture(), Changed = Next() };
            }
        }
        catch (Exception error) { _failure = error.ToString(); Next(); throw; }
        finally { _executing = false; }
    }
}
