namespace OpenNV.Runtime.World.Actors;

internal partial class RuntimeNativeNpc
{
    private bool HasIndependentStoppedPose => _aiReferenceState?.Injury?.Dead == true ||
        _aiReferenceState?.Engagement is not null;

    private bool CanCaptureStoppedAiPose() => Combat is { } combat ? combat.StoppedAiPoseCaptureReady :
        !HasIndependentStoppedPose && _aiReferenceState?.KnockedDown != true && _aiReferenceState?.HitReaction is null;
}
