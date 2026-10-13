using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutProcessWeaponEntry(Guid Ownership, FalloutFormKey Form, long InventoryRevision);
internal sealed record FalloutProcessSceneFields(FalloutProcessWeaponEntry? WeaponEntry, Guid? AnimationContext,
    byte SceneUpdateMask = 0, Guid? PostSceneContext = null, byte ActionCondition = 0, byte Action9Condition = 0,
    Guid? PostActionContext = null, FalloutProcessPostActionScalars? PostActionScalars = null,
    FalloutProcessDetectionActionScalars? DetectionActionScalars = null,
    FalloutActorIdleAnimationFields? IdleAnimation = null, Guid? IdlePrimaryPackage = null,
    FalloutProcessMovementFlagFields? MovementFlags = null, Guid? ActorValueCache = null,
    FalloutProcessPhysicalActivityFields? PhysicalActivity = null)
{
    internal static FalloutProcessSceneFields Constructed => new(null, null);
}
internal sealed record FalloutProcessSceneQuery(Guid Process, string Stack, string ExecutableSha256, FalloutCombatActorIdentity Actor,
    long Epoch, FalloutDetectionProcessLevel Level, Guid GameplayOwnership, long Changed,
    FalloutProcessWeaponEntry? WeaponEntry, Guid? AnimationContext, Guid? IdlePrimaryPackage = null,
    Guid? ActorValueCache = null);
internal sealed record FalloutProcessPlayerSceneUpdate(FalloutProcessSceneQuery Before, long Changed,
    byte? BeforeMask, byte? AfterMask, int Stores, bool Returned);
internal sealed record FalloutProcessSceneContextQuery(FalloutProcessSceneQuery Process, Guid? Context);
internal sealed record FalloutProcessPlayerActionQuery(FalloutProcessSceneQuery Process, byte Condition, byte? SecondCondition);

internal sealed partial class FalloutActorProcessCommonState
{
    internal FalloutProcessSceneQuery ReadSceneQueries(FalloutFormKey actor, long epoch)
    {
        RequireNotBusy(); var current = Require(actor);
        if (!FalloutSourceMainFamily.IsFallout3(_source.ExecutableSha256) || current.Epoch != epoch ||
            current.Phase is FalloutProcessCommonPhase.Retired or FalloutProcessCommonPhase.OldRetired ||
            current.Gameplay is not { Retired: false } lease || current.Boundary is not null ||
            Identity(actor) != current.Source)
            throw new NotSupportedException("source-Actor-scene-process-current-class-and-lease-unavailable");
        // Base/Low returns zero from the original virtual. MiddleHigh/High
        // reads two independently constructed nullable fields. Their nonnull
        // writers have separate inventory/animation ownership requirements.
        var fields = current.Level is FalloutDetectionProcessLevel.High or FalloutDetectionProcessLevel.MiddleHigh
            ? current.SceneFields ?? throw new NotSupportedException("source-Actor-scene-process-MiddleHigh-fields-unconstructed")
            : FalloutProcessSceneFields.Constructed;
        return new(_process, _stack, _source.ExecutableSha256, current.Source, epoch, current.Level, lease.Ownership,
            current.Changed, fields.WeaponEntry, fields.AnimationContext, fields.IdlePrimaryPackage, fields.ActorValueCache);
    }

    private FalloutProcessSceneFields? ConstructSceneFields(FalloutDetectionProcessLevel level) =>
        FalloutSourceMainFamily.IsFallout3(_source.ExecutableSha256) &&
        level is FalloutDetectionProcessLevel.High or FalloutDetectionProcessLevel.MiddleHigh
            ? level == FalloutDetectionProcessLevel.High ? FalloutProcessSceneFields.Constructed with
            {
                PostActionScalars = FalloutProcessPostActionScalars.Constructed,
                DetectionActionScalars = FalloutProcessDetectionActionScalars.Constructed,
                IdleAnimation = FalloutActorIdleAnimationFields.Constructed,
                MovementFlags = FalloutProcessMovementFlagFields.Constructed,
                PhysicalActivity = FalloutProcessPhysicalActivityFields.Constructed
            }
            : FalloutProcessSceneFields.Constructed with { PhysicalActivity = FalloutProcessPhysicalActivityFields.Constructed } : null;

    internal FalloutProcessPlayerSceneUpdate UpdatePlayerScene(FalloutFormKey actor, long epoch)
    {
        var before = ReadSceneQueries(actor, epoch);
        if (!before.Actor.EnginePlayer) throw new InvalidOperationException("The Player scene update requires the actual engine Player identity.");
        if (before.Level != FalloutDetectionProcessLevel.High)
            return new(before, before.Changed, null, null, 0, true);
        var current = Require(actor);
        var fields = current.SceneFields ?? throw new InvalidOperationException("High Player scene fields were not constructed.");
        if (fields.SceneUpdateMask != 0)
            throw new NotSupportedException("source-High-process-scene-dirty-node-and-controller-consumers-unbound");
        // The actual Player class predicate returns false. The source zero
        // mask skips all node/controller children, then calls the independent
        // mask-clear virtual. The NPC-only post-update child is not called.
        var changed = Next();
        _current[actor] = current with { SceneFields = fields with { SceneUpdateMask = 0 }, Changed = changed };
        return new(before, changed, fields.SceneUpdateMask, 0, 1, true);
    }

    internal FalloutProcessSceneContextQuery ReadSceneContext(FalloutFormKey actor, long epoch)
    {
        var process = ReadSceneQueries(actor, epoch);
        var context = process.Level is FalloutDetectionProcessLevel.High or FalloutDetectionProcessLevel.MiddleHigh
            ? Require(actor).SceneFields!.PostSceneContext : null;
        return new(process, context);
    }

    internal FalloutProcessPlayerActionQuery ReadPlayerActionCondition(FalloutFormKey actor, long epoch)
    {
        var process = ReadSceneQueries(actor, epoch);
        if (!process.Actor.EnginePlayer) throw new InvalidOperationException("Player action conditions require the actual engine Player.");
        if (process.Level is not (FalloutDetectionProcessLevel.High or FalloutDetectionProcessLevel.MiddleHigh))
            return new(process, 0, null);
        var fields = Require(actor).SceneFields!;
        return new(process, fields.ActionCondition, fields.ActionCondition == 0 ? null : fields.Action9Condition);
    }

    private static void ValidateSceneFields(FalloutProcessCommonEntry entry, string executable)
    {
        var expected = FalloutSourceMainFamily.IsFallout3(executable) &&
            entry.Level is FalloutDetectionProcessLevel.High or FalloutDetectionProcessLevel.MiddleHigh;
        if (expected != (entry.SceneFields is not null) || entry.SceneFields is { WeaponEntry: not null } or { AnimationContext: not null } or { SceneUpdateMask: not 0 } or { PostSceneContext: not null } or { ActionCondition: not 0 } or { Action9Condition: not 0 } or { IdlePrimaryPackage: not null } or { ActorValueCache: not null })
            throw new InvalidDataException("Process scene fields lost their constructor or admitted a missing inventory/animation writer.");
        ValidatePostActionFields(entry);
        ValidateIdleAnimationFields(entry);
        ValidateMovementFlagFields(entry);
        ValidatePhysicalActivityFields(entry);
    }
}
