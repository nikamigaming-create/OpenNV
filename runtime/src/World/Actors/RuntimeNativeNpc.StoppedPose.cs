using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Actors;

internal partial class RuntimeNativeNpc
{
    private bool HasIndependentStoppedPose => _aiReferenceState?.Injury?.Dead == true ||
        _aiReferenceState?.Engagement is not null;

    private bool CanCaptureStoppedAiPose() => Combat is { } combat ? combat.StoppedAiPoseCaptureReady :
        !HasIndependentStoppedPose && _aiReferenceState?.KnockedDown != true && _aiReferenceState?.HitReaction is null;

    private FalloutActorStoppedPoseCaptureDiagnostic ReadStoppedPoseCaptureDiagnostic()
    {
        if (Combat is { } combat) return combat.StoppedAiPoseCaptureDiagnostic;
        var blockers = new List<FalloutActorCaptureBlocker>();
        if (HasIndependentStoppedPose) blockers.Add(new("npc-pose", "independent-pose-without-combat-owner"));
        if (_aiReferenceState?.KnockedDown == true) blockers.Add(new("npc-pose", "knocked-down"));
        if (_aiReferenceState?.HitReaction is not null) blockers.Add(new("npc-pose", "active-hit-reaction"));
        return new(CanCaptureStoppedAiPose(), false, blockers.AsReadOnly(), null, null, null);
    }
}
