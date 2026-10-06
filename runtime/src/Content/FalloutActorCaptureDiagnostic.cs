namespace OpenNV.Runtime.Content;

// These observations describe the existing capture gates. They are never
// capture authority, saved continuation state, or a replacement native owner.
internal sealed record FalloutActorCaptureBlocker(string Owner, string Predicate, string? Detail = null);

internal sealed record FalloutAnimationSoundCaptureDiagnostic(bool Ready, bool Retired,
    bool LostAtRetirement, int ActiveSpatial, int ActiveVoices, long EventCount, IReadOnlyList<string> Unbound);

internal sealed record FalloutActorStoppedPoseCaptureDiagnostic(bool Ready, bool Retired,
    IReadOnlyList<FalloutActorCaptureBlocker> Blockers,
    FalloutAnimationSoundCaptureDiagnostic? EnemySounds, string? HitReactionError,
    FalloutAnimationSoundCaptureDiagnostic? HitReactionSounds);

internal sealed record FalloutActorSelectionCaptureDiagnostic(FalloutFormKey Reference, ulong NativeOwner,
    bool Ready, bool Retired, FalloutFormKey? Candidate, ushort? ConditionFunction,
    IReadOnlyList<FalloutActorCaptureBlocker> Blockers, FalloutActorStoppedPoseCaptureDiagnostic StoppedPose);

internal sealed record FalloutActorPackageBindingCaptureDiagnostic(FalloutFormKey Reference, ulong NativeOwner,
    bool Ready, bool FiniteSoundReady, bool Retired, FalloutFormKey? Package,
    IReadOnlyList<FalloutActorCaptureBlocker> Blockers, FalloutActorStoppedPoseCaptureDiagnostic StoppedPose);
