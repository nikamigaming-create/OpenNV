namespace OpenNV.Runtime.Formats.Gamebryo;

/// <summary>Source weapon text-key clocks, including clamped Fire/Loop intervals.</summary>
internal sealed class FalloutWeaponAnimationTimeline
{
    private readonly FalloutNifTextKeyTimeline _finite;
    private readonly FalloutNifTextKeyTimeline _wholeLoop;
    private readonly FalloutNifTextKeyTimeline? _internalLoop;
    private readonly double _loopStart, _loopStop;
    private readonly bool _loop;
    internal double Duration { get; }
    internal double Period => _internalLoop is null ? Duration : _loopStop - _loopStart;
    internal double? Hold { get; }
    internal int Discharges { get; }
    internal bool HasInternalLoop => _internalLoop is not null;
    internal bool UsesWeaponCadence { get; }

    internal static bool DischargesWeapon(string text) => text.Trim().Equals("Hit", StringComparison.OrdinalIgnoreCase) ||
        text.Trim().Equals("Fire", StringComparison.OrdinalIgnoreCase) || text.Trim().Equals("Release", StringComparison.OrdinalIgnoreCase) ||
        text == "WEAP: automatic discharge";

    internal FalloutWeaponAnimationTimeline(IReadOnlyList<FalloutNifTextKey> keys, float start, float stop, float frequency,
        bool loop = false, bool weaponCadence = false)
    {
        _loop = loop;
        // AttackLoop can be a continuous pose with no KF discharge marker.
        // Its gameplay events come from WEAP shots/sec, not an invented KF hit.
        UsesWeaponCadence = weaponCadence && !keys.SelectMany(key => key.Value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)).Any(DischargesWeapon);
        if (UsesWeaponCadence) keys = keys.Append(new(start, "WEAP: automatic discharge")).OrderBy(key => key.Time).ToArray();
        _finite = new(keys, start, stop, 2, frequency);
        _wholeLoop = new(keys, start, stop, 0, frequency);
        Duration = ((double)stop - start) / frequency;
        var texts = keys.SelectMany(key => key.Value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(text => (key.Time, Text: text.Trim()))).ToArray();
        var holds = texts.Where(key => key.Text.Equals("Hold", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (holds.Length > 1) throw new NotSupportedException("Weapon animation has multiple Hold markers.");
        Hold = holds.Length == 0 ? null : ((double)holds[0].Time - start) / frequency;
        var loops = texts.Where(key => key.Text.Equals("Loop", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (loops.Length > 1) throw new NotSupportedException("Weapon animation has multiple internal loops.");
        if (loops.Length == 1)
        {
            var fire = texts.Where(key => key.Text.Equals("Fire", StringComparison.OrdinalIgnoreCase) && key.Time < loops[0].Time).ToArray();
            if (fire.Length == 0) throw new InvalidDataException("Weapon Loop marker has no preceding Fire marker.");
            var first = fire[^1].Time;
            _loopStart = ((double)first - start) / frequency;
            _loopStop = ((double)loops[0].Time - start) / frequency;
            if (_loopStart < 0 || _loopStop > Duration) throw new InvalidDataException("Weapon internal loop leaves its sequence.");
            _internalLoop = new(keys, first, loops[0].Time, 0, frequency);
            Discharges = texts.Count(key => key.Time >= first && key.Time < loops[0].Time && DischargesWeapon(key.Text));
        }
        else Discharges = texts.Count(key => DischargesWeapon(key.Text));
        if (Hold is < 0 || Hold > Duration) throw new InvalidDataException("Weapon Hold marker leaves its sequence.");
    }

    internal double SampleSeconds(double elapsed, bool loop)
    {
        if (!double.IsFinite(elapsed) || elapsed < 0) throw new InvalidDataException("Weapon animation clock is invalid.");
        if (!loop) return Math.Min(elapsed, Duration);
        if (_internalLoop is null) return elapsed % Duration;
        return elapsed < _loopStop ? elapsed : _loopStart + (elapsed - _loopStop) % Period;
    }

    internal IEnumerable<FalloutNifTextKeyEvent> Crossed(double from, double to, bool includeFrom, bool loop)
    {
        if (!loop || _internalLoop is null)
        {
            foreach (var key in (loop ? _wholeLoop : _finite).Crossed(from, to, includeFrom)) yield return key;
            yield break;
        }
        if (from < _loopStop)
            foreach (var key in _finite.Crossed(from, Math.Min(to, _loopStop), includeFrom)) yield return key;
        if (to >= _loopStop)
            foreach (var key in _internalLoop.Crossed(Math.Max(0, from - _loopStop), to - _loopStop, includeFrom || from < _loopStop)) yield return key;
    }

    internal IEnumerable<FalloutNifTextKeyEvent> Crossed(double from, double to, bool includeFrom) => Crossed(from, to, includeFrom, _loop);
}
