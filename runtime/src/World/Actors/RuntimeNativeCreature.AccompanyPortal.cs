using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.SceneGraph;

namespace OpenNV.Runtime.World.Actors;

internal sealed partial class RuntimeNativeCreature : INativeActorPortal
{
    private NativeActorPackagePortal? _actorPackagePortal;
    private string? _accompanyPlaybackWait;
    private FalloutFormKey? _accompanyDoor => _actorPackagePortal?.Door;
    private bool _accompanyPortalRequested => _actorPackagePortal?.Requested == true;
    private bool _accompanyPortalTransferred => _actorPackagePortal?.Transferred == true;
    private string? _accompanyPortalWait => _accompanyPlaybackWait ?? _actorPackagePortal?.Wait;
    CharacterBody3D INativeActorPortal.PortalBody => this;
    RuntimeNativeActorCombat INativeActorPortal.PortalCombat => Combat!;
    bool INativeActorPortal.PortalTransferred => _accompanyPortalTransferred;
    void INativeActorPortal.RetirePortalBindings() => RetireSourceBindings();
    bool INativeActorPortal.WantsPortal(FalloutFormKey door) => _actorPackagePortal?.Wants(door) == true;
    void INativeActorPortal.CommitPortal(FalloutFormKey door) => PackagePortal.Commit(door);
    void INativeActorPortal.FinishPortalAttempt(FalloutFormKey door, string? error) => _actorPackagePortal?.Finish(door, error);

    private NativeActorPackagePortal PackagePortal => _actorPackagePortal ??= new(this, Combat!, _aiRecords!, _aiWorld!,
        _aiState!, Skeleton.UnitsToMetres, () => _aiRecords!.GetEffective(_aiPackage!.FormKey), CurrentPortalTargetCell,
        () => _aiError is null && (_followPackage is { } follow && follow.Form == _aiPackage?.FormKey &&
            _aiWorld!.IsEnabled(follow.Target) || _travelPackage is { } travel && travel.Form == _aiPackage?.FormKey &&
            _travelProgress is { Complete: false }), AccompanyPortalPlaybackReady, error =>
        {
            _aiError = error; _failedPackage = _aiPackage?.FormKey; _aiState!.ProcedureCaptureBlocker = error;
            GD.PushError($"OPENNV_NATIVE_CREATURE_PORTAL_DIVERGENCE actor={Appearance.Reference}: {error}");
        });

    private FalloutFormKey CurrentPortalTargetCell() => _travelPackage is { } travel
        ? travel.Start(_aiRecords!, _aiWorld!, Appearance.Reference!.Value).Cell
        : NativeActorFollowTarget.Cell(_aiRecords!, _aiWorld!, Combat!, _followPackage!.Target);

    private bool AdvanceAccompanyPortal(FalloutFollowPackage package, double delta)
    {
        var world = _aiWorld ?? throw new NotSupportedException("Creature follower has no world owner.");
        if (!world.IsEnabled(package.Target)) return false;
        return AdvancePackagePortal(NativeActorFollowTarget.Cell(_aiRecords!, world, Combat!, package.Target), package.Running, false, delta);
    }

    private bool AdvancePackagePortal(FalloutFormKey targetCell, bool running, bool sneaking, double delta) =>
        PackagePortal.Advance(targetCell, running, sneaking, delta, _travelProgress);

    private bool AccompanyPortalPlaybackReady()
    {
        _accompanyPlaybackWait = null;
        if (Error is not null || _aiState!.ScriptError is not null)
            throw new NotSupportedException("Creature package portal retains a failed source animation or script.");
        if (_eventIdleClock is { } idle)
        {
            if (idle.Endless) throw new NotSupportedException("Creature package portal cannot retire an endless package event idle.");
            _accompanyPlaybackWait = "finite-package-idle-awaiting-ordinary-retirement";
            return false;
        }
        if (_eventIdleAnimation is not null || _aiState.PackageIdle is not null || _conversationTarget is not null)
            throw new NotSupportedException("Creature package portal retains an independent idle or dialogue continuation.");
        if (NodeTraversal.SelfAndDescendants<NativeOwnedAnimationSoundPlayer>(this).All(sound => sound.CanRetireTransientEffect)) return true;
        _accompanyPlaybackWait = "source-audio-awaiting-retirement";
        return false;
    }

}
