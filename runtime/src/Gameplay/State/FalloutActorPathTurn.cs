namespace OpenNV.Runtime.Gameplay.State;

// A path's stationary facing phase is independent of its animation clock.
// Keep its chosen side and minimum duration when presentation is reconstructed.
internal sealed record FalloutActorPathTurn(float TargetX, float TargetY, float TargetZ,
    float HeadingRadians, bool Left, float MinimumRemainingSeconds)
{
    internal void Validate()
    {
        if (!float.IsFinite(TargetX) || !float.IsFinite(TargetY) || !float.IsFinite(TargetZ) ||
            !float.IsFinite(HeadingRadians) || HeadingRadians < -MathF.PI || HeadingRadians > MathF.PI ||
            !float.IsFinite(MinimumRemainingSeconds) || MinimumRemainingSeconds < 0)
            throw new InvalidDataException("Saved actor path turn is invalid.");
    }
}
