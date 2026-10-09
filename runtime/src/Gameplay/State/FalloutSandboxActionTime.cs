using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

// Duration and the original GameHour selection field are retained once. The
// comparator returns total source progress, not a frame delta to subtract again.
internal sealed record FalloutSandboxActionTime(FalloutSandboxActionDeadline Source, float SelectionHour,
    float Duration, float LastElapsed = 0)
{
    internal void Validate()
    {
        if (Source is null) throw new InvalidDataException("Sandbox action omitted its original hour comparator.");
        Source.Validate();
        if (!float.IsFinite(SelectionHour) || !float.IsFinite(Duration) || Duration < 0 || !float.IsFinite(LastElapsed))
            throw new InvalidDataException("Sandbox action lost its selected source hour/duration.");
    }
    internal float Elapsed(float currentHour)
    {
        Validate(); return Source.Elapsed(SelectionHour, currentHour);
    }
}
