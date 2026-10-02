namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutEscortProgress(bool TargetAcquired, bool Complete)
{
    internal void Validate()
    {
        if (Complete && !TargetAcquired) throw new InvalidDataException("Saved escort completed without acquiring its target.");
    }
}
