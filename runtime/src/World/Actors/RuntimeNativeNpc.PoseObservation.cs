using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.World.Actors;

internal partial class RuntimeNativeNpc
{
    // Read-only existing writer/phase metadata. No selection, clock advancement,
    // source lookup, save capture or pose publication is performed here.
    internal object PoseOwners => new
    {
        activeAnimationLayers = ObserveActiveAnimationLayers(),
        owner = Combat?.Dead == true ? "death/combat" : Combat?.OwnsPose == true ? "combat" : Combat?.Restrained == true ? "restrained-held" :
            IdleSnapshotActive ? "idle-snapshot" : _outgoingIdles.Count != 0 || _idleEaseIn is not null || _currentIdleEaseOut is not null ? "idle-layers" :
            Combat?.PackageOwnsPose == true ? _animation is null ? "package" : "package+idle" :
            _baseAnimation is not null ? _animation is null ? "base" : "base+idle" : _animation is null ? "no-kf" : "idle",
        baseClip = ObservePoseClip(_baseAnimation),
        baseResource = _baseResource,
        baseSourceHash = _baseHash,
        baseElapsedSeconds = _baseClock.ElapsedSeconds,
        baseSourceSeconds = _baseAnimationSeconds,
        baseDormant = Combat?.Dead == true || Combat?.OwnsPose == true || Combat?.Restrained == true || Combat?.PackageOwnsPose == true,
        idleClip = ObservePoseClip(_animation),
        idleSourceSeconds = _animationSeconds,
        idle = _idleForm?.ToString(),
        idleOwner = _idleOwner,
        idleRevision = _idleRevision,
        idleRequestState = _currentIdleRequest?.State,
        idleSequenceState = _currentIdleRequest?.SequenceState,
        idleResource = _currentIdleRequest?.Resource,
        idleWeight = _animation is null ? (float?)null : _currentIdleEaseOut?.Weight ?? (_idleEaseIn is { } incoming ? 1 - incoming.Weight : 1),
        snapshotActive = IdleSnapshotActive,
        snapshotClip = ObservePoseClip(_idleSnapshotAnimation),
        outgoing = _outgoingIdles.Select(value => new
        {
            idle = value.Request.Idle.ToString(),
            value.Request.Owner,
            value.Request.State,
            value.Request.SequenceState,
            value.Request.Resource,
            value.Request.Sha256,
            clip = ObservePoseClip(value.Animation),
            sourceSeconds = value.Clock.SourceSeconds,
            weight = value.Deactivation.Weight
        }).ToArray(),
        combatOwnsPose = Combat?.OwnsPose == true,
        packageOwnsPose = Combat?.PackageOwnsPose == true,
        packageDormant = Combat?.PackageOwnsPose != true,
        packageMoving = Combat?.PackageMoving == true,
        package = _aiPackage?.FormKey.ToString(),
        packageAnimation = _aiReferenceState?.PackageMotion?.Animation,
        packageElapsedSeconds = _aiReferenceState?.PackageMotion?.Seconds,
        combatAction = _aiReferenceState?.Engagement?.Action,
        combatAnimation = _aiReferenceState?.Engagement?.Animation,
        combatSeconds = _aiReferenceState?.Engagement?.Seconds,
        hitAnimation = _aiReferenceState?.HitReaction?.Animation,
        knockedDown = _aiReferenceState?.KnockedDown,
        headTracking = HeadTrackingState,
        face = FaceState,
        animationError = AnimationError,
        packageError = _aiError,
        combatError = Combat?.Error,
        boundary = "Existing layer clocks/request states; idle snapshot weights and native controller-slot equivalence are not inferred. Face morphs are not skeleton bones."
    };

    private static object? ObservePoseClip(RuntimeNativeNifAnimation? animation) => animation is null ? null : new
    {
        animation.Source.Sha256,
        animation.Sequence.Name,
        animation.Sequence.TargetName,
        animation.Sequence.StartTime,
        animation.Sequence.StopTime,
        animation.Sequence.Frequency,
        animation.Sequence.CycleType
    };
}
