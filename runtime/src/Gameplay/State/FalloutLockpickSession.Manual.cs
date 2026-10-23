using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed class FalloutLockpickMovementState
{
    internal static FalloutLockpickMovementState Process { get; } = new();
    internal float PreviousDelta;
}

internal sealed partial class FalloutLockpickSession
{
    internal float Position { get; private set; }
    internal float Target { get; private set; }
    internal float PinHealth { get; private set; }
    internal float Width { get; private set; }
    internal int ManualPhase { get; private set; } = 2;
    internal bool ManualReady { get; private set; }
    internal bool Won { get; private set; }
    private int _length, _concentric, _margin;
    private float _initialHealth, _breakSeconds;
    private Func<int>? _breakPin;
    private Action? _movementSound;
    private FalloutLockpickMovementState? _movement;

    internal void ConfigureManual(float width, float initialPosition, int length, int concentric, float health,
        float breakSeconds, Func<int> breakPin, Action movementSound, FalloutLockpickMovementState? movement = null)
    {
        if (ManualReady || Turning || Finished) throw new InvalidOperationException("Manual picking is already configured or retired.");
        if (!float.IsFinite(width) || width <= 0 || !float.IsFinite(initialPosition) || initialPosition < 0 || initialPosition > width ||
            length <= 0 || concentric <= 0 || !float.IsFinite(health) || health <= 0 || !float.IsFinite(breakSeconds))
            throw new NotSupportedException("Manual picking has unadmitted source geometry or health settings.");
        _margin = checked(concentric + length / 2);
        if (2d * _margin >= width) throw new NotSupportedException("Degenerate source picking geometry is not admitted.");
        Width = width; Position = initialPosition; _length = length; _concentric = concentric;
        Target = MathF.Min(MathF.Max(LayoutUnit * width, _margin), width - _margin);
        _initialHealth = PinHealth = health; _breakSeconds = breakSeconds;
        _breakPin = breakPin; _movementSound = movementSound; _movement = movement ?? FalloutLockpickMovementState.Process;
        ManualReady = true;
    }

    internal float PermittedAngle()
    {
        var distance = MathF.Abs(Position - Target);
        if (distance >= _margin)
        {
            var ratio = (float)((_margin + (double)distance - Width) / (2d * _margin - Width));
            return ratio * 15;
        }
        var left = (int)(Target - _length / 2);
        var best = Width; var selected = -1;
        for (var index = 0; index < 6; ++index)
        {
            var halfWidth = (float)(2 * (Target - left)) / 2;
            if (halfWidth > distance && halfWidth < best) { best = halfWidth; selected = index; }
            left = checked(left - _concentric / 5);
        }
        if (selected < 0) throw new NotSupportedException("No source-generated child admits this pick position.");
        return (float)((1 - selected / 6d) * 90);
    }

    internal void AdvanceManual(uint milliseconds, float delta, bool pressure)
    {
        if (!ManualReady || Finished) return;
        if (Turning) { Advance(milliseconds); if (Finished) Won = true; return; }
        if (!float.IsFinite(delta)) throw new InvalidDataException("Pick movement is not finite.");
        var elapsed = _stamp is { } previous ? unchecked(milliseconds - previous) : 0;
        _stamp = milliseconds;
        if (elapsed > int.MaxValue) throw new InvalidDataException("The pick clock regressed.");
        switch (ManualPhase)
        {
            case 2:
                CylinderAngle = MathF.Max(CylinderAngle - (float)(elapsed * 90d / 1000), 0);
                Move(delta);
                if (pressure) ManualPhase = 3;
                return;
            case 3:
                if (!pressure) { ManualPhase = 2; return; }
                var permitted = PermittedAngle();
                CylinderAngle = MathF.Min(CylinderAngle + (float)(elapsed * 90d / 1000), permitted);
                if (CylinderAngle < permitted) return;
                if (CylinderAngle >= 90)
                {
                    Finished = Won = true;
                    _success();
                }
                else ManualPhase = 4;
                return;
            case 4:
                if (!pressure) { ManualPhase = 2; return; }
                var seconds = (float)(elapsed / 1000d);
                if (_breakSeconds != 0) seconds /= _breakSeconds;
                PinHealth = MathF.Max(PinHealth - seconds, 0);
                if (!float.IsFinite(PinHealth)) throw new InvalidDataException("Pick health is not finite.");
                if (PinHealth > 0) return;
                // Retire before an uncertain inventory callback. A failed
                // prepared removal cannot cause another loss on the next tick.
                Finished = true;
                if (_breakPin!() <= 0) return;
                PinHealth = _initialHealth; CylinderAngle = 0; ManualPhase = 2; Finished = false;
                return;
            default: throw new InvalidOperationException("The manual picking phase is invalid.");
        }
    }

    private void Move(float delta)
    {
        var raw = Position + delta;
        if (!float.IsFinite(raw) || (double)raw > int.MaxValue || raw < int.MinValue)
            throw new NotSupportedException("Pick movement exceeds admitted signed remainder storage.");
        var oldRemainder = (int)Position % 100; var newRemainder = (int)raw % 100;
        var crossing = delta > 0 && oldRemainder > newRemainder || delta < 0 && newRemainder > oldRemainder;
        var sound = crossing && _random(10) == 0;
        if (!sound) sound = delta != 0 && _movement!.PreviousDelta == 0;
        if (sound) _movementSound!();
        Position = MathF.Min(MathF.Max(raw, 0), Width);
        _movement!.PreviousDelta = delta;
    }

    internal static (int Length, int Concentric, float Health, float BreakSeconds) ManualSettings(
        FalloutPluginStack records, int difficulty, float current)
    {
        string[] names = ["VeryEasy", "Easy", "Average", "Hard", "VeryHard"];
        int[] thresholds = [0, 25, 50, 75, 100, 101];
        if (difficulty is < 0 or > 4) throw new ArgumentOutOfRangeException(nameof(difficulty));
        var skill = Skill(current);
        var factor = Math.Clamp((float)(skill - thresholds[difficulty]) / (2 * (thresholds[difficulty + 1] - thresholds[difficulty])), 0, 1);
        int Interpolate(string prefix)
        {
            var min = unchecked((int)FalloutGameSettingIntegers.Read(records, prefix + "Min" + names[difficulty]));
            var max = unchecked((int)FalloutGameSettingIntegers.Read(records, prefix + "Max" + names[difficulty]));
            var result = min + ((double)max - min) * factor;
            return double.IsFinite(result) && result is >= int.MinValue and <= int.MaxValue ? (int)result :
                throw new InvalidDataException("Picking tolerance is outside signed storage.");
        }
        return (Interpolate("iSweetSpotLength"), Interpolate("iConcentricLength"),
            1 + skill * FalloutGameSettingFloats.Read(records, "fLockpickBonusHealth"),
            FalloutGameSettingFloats.Read(records, "fPickBreakSecs"));
    }
}
