using System.Globalization;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutFastTravelSnapshot(bool CanTravel, bool CanWait, bool KeepOnCellChange)
{
    internal static FalloutFastTravelSnapshot Initial => new(true, true, false);
}

/// <summary>Shared player policy changed by the source EnableFastTravel command.</summary>
internal sealed class FalloutFastTravel
{
    internal FalloutFastTravelSnapshot State { get; private set; } = FalloutFastTravelSnapshot.Initial;

    internal void Apply(IReadOnlyList<string> arguments)
    {
        if (arguments.Count is < 1 or > 3)
            throw new InvalidDataException("EnableFastTravel requires one to three integer flags.");
        var flags = arguments.Select(value => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var flag)
            ? flag : throw new InvalidDataException("EnableFastTravel flags must be source integers.")).ToArray();
        var enabled = flags[0] != 0;
        var canWait = flags.Length < 2 || flags[1] != 0;
        var keep = flags.Length > 2 && flags[2] == 1;
        State = new(enabled, enabled || canWait, enabled ? keep : State.KeepOnCellChange || keep);
    }

    internal static float ElapsedHours(float distance, float runningSpeed, float timeScale)
    {
        if (!float.IsFinite(distance) || distance < 0 || !float.IsFinite(runningSpeed) || runningSpeed <= 0 ||
            !float.IsFinite(timeScale) || timeScale <= 0)
            throw new InvalidDataException("Fast travel requires finite distance, positive running speed and positive time scale.");
        var hours = (float)((float)(distance / runningSpeed) / 3600.0) * timeScale;
        if (!float.IsFinite(hours) || (double)hours > int.MaxValue)
            throw new NotSupportedException("Fast travel exceeds its source signed whole-hour span.");
        var wholeHours = (int)hours;
        return wholeHours > 0 ? wholeHours : hours;
    }

    internal void CompleteRelocation()
    {
        if (!State.KeepOnCellChange) State = State with { CanTravel = true };
    }

    internal void Restore(FalloutFastTravelSnapshot state) => State = state ?? throw new InvalidDataException("Saved travel policy is absent.");
}
