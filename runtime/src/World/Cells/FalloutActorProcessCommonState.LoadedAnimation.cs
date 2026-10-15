using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutProcessAnimationLoadOwner(Guid Process, string Stack, string Executable,
    FalloutCombatActorIdentity Actor, long Epoch, FalloutDetectionProcessLevel Level, Guid GameplayOwnership,
    long Changed, int Tier, Guid TierFactory);
internal sealed record FalloutProcessAnimationContextStore(FalloutReferenceModelDataInvocation Invocation,
    FalloutProcessAnimationLoadOwner Owner, FalloutProcessAnimationContextInvalidation Load,
    Guid? Before, Guid? After, FalloutSourceAnimationContextConstruction? Construction,
    long Entered, long Changed, bool Returned, string? Failure);

internal sealed partial class FalloutActorProcessCommonState
{
    private FalloutReferenceModelDataInvocation? _enteredAnimationStore;
    // The loader needs process identity while its animation pointer is being
    // replaced. This getter borrows the actual mutable tier and lease; it does
    // not manufacture a scene query with a null animation field.
    internal FalloutProcessAnimationLoadOwner ReadAnimationLoadOwner(FalloutReferenceModelDataInvocation invocation)
    {
        if (!_busy || _enteredAnimationStore != invocation) RequireNotBusy();
        var current = Require(invocation.Actor.Reference);
        if (invocation.Call == Guid.Empty || invocation.CommonProcess != _process || invocation.Actor != current.Source ||
            invocation.Epoch != current.Epoch || invocation.Ingress != FalloutReferenceModelDataIngress.ThreeDLoad ||
            invocation.ThreadId != Environment.CurrentManagedThreadId || current.Boundary is not null ||
            current.Gameplay is not { Retired: false } lease || current.Phase is FalloutProcessCommonPhase.OldRetired or FalloutProcessCommonPhase.Retired)
            throw new InvalidOperationException("Animation load changed the actual current process, class, epoch or lease.");
        var assignment = current.ActorAssignment ?? throw new NotSupportedException("source-animation-current-tier-constructor-unavailable");
        RequireActorAssignmentLane(assignment, FalloutActorAssignmentLane.Tier);
        var result = new FalloutProcessAnimationLoadOwner(_process, _stack, _source.ExecutableSha256, current.Source,
            current.Epoch, current.Level, lease.Ownership, current.Changed, assignment.Values.Tier, assignment.Publication.Factory);
        return result;
    }

    internal FalloutProcessAnimationContextStore StoreLoadedAnimationContext(FalloutReferenceModelDataInvocation invocation,
        FalloutSourceAnimationContextState? context, Action<FalloutReferenceModelDataInvocation> requireActual)
    {
        RequireNotBusy(); requireActual(invocation); var owner = ReadAnimationLoadOwner(invocation); var current = Require(invocation.Actor.Reference);
        if (owner.Tier is not (0 or 1) || current.SceneFields is not { } fields)
            throw new NotSupportedException("source-reference-AnimData-lower-process-Extra-publication-child-unowned");
        var enteredLoad = fields.AnimationContextLoad ?? fields.AnimationContextStores?.LastOrDefault()?.Load ??
            throw new InvalidOperationException("Common AnimData store has no actual entered inherited animation initializer.");
        if (enteredLoad.Invocation != invocation || context is not null &&
            (context.Construction.Invocation != invocation || context.Construction.Role != FalloutLoadedAnimationRole.Common ||
             !context.Construction.Returned || context.Actor != owner.Actor || context.CommonProcess != _process))
            throw new InvalidOperationException("Common AnimData pointer came from another actual source constructor.");
        // Replacement of a live context reaches three independent selection
        // clears and destructor effects. Initial null/reset and same-pointer
        // paths do not touch those fields.
        if (fields.AnimationContext is not null && fields.AnimationContext != context?.Identity)
            throw new NotSupportedException("source-Common-old-AnimData-three-selection-clears-and-retirement-unowned");
        var entered = Next(); string? failure = null; var fault = _callbackFault;
        _enteredAnimationStore = invocation; _busy = true;
        try
        {
            requireActual(invocation);
            if (_callbackFault != fault) throw new InvalidOperationException("Common animation setter swallowed actual reentry.");
            fields = fields with { AnimationContext = context?.Identity, AnimationContextWriter = null, AnimationContextLoad = null };
            _current[owner.Actor.Reference] = current with { SceneFields = fields, Changed = Next() };
            requireActual(invocation);
            if (_callbackFault != fault) throw new InvalidOperationException("Common animation setter swallowed actual reentry.");
        }
        catch (Exception error) { failure = error.Message; throw; }
        finally
        {
            var live = Require(owner.Actor.Reference); var scene = live.SceneFields!;
            var stored = new FalloutProcessAnimationContextStore(invocation, owner, enteredLoad, current.SceneFields!.AnimationContext,
                scene.AnimationContext, context?.Construction, entered, Next(), failure is null, failure);
            _current[owner.Actor.Reference] = live with
            {
                SceneFields = scene with
                { AnimationContextStores = [.. scene.AnimationContextStores ?? [], stored] },
                Changed = stored.Changed
            };
            _busy = false; _enteredAnimationStore = null;
        }
        return Require(owner.Actor.Reference).SceneFields!.AnimationContextStores![^1];
    }

    private static void ValidateLoadedAnimationPointer(FalloutProcessCommonEntry entry)
    {
        if (entry.SceneFields is not { } scene) return;
        Guid? pointer = null; long changed = 0;
        foreach (var store in scene.AnimationContextStores ?? [])
        {
            if (store.Invocation != store.Load.Invocation || store.Owner.Actor != entry.Source || store.Owner.Epoch != entry.Epoch ||
                store.Owner.Tier is not (0 or 1) || store.Owner.GameplayOwnership == Guid.Empty || store.Owner.TierFactory == Guid.Empty ||
                store.Owner.Process != store.Invocation.CommonProcess || store.Before != pointer || store.Entered <= changed ||
                store.Load.Changed >= store.Entered || store.Changed <= store.Entered || store.Changed > entry.Changed ||
                store.Returned != (store.Failure is null) || store.After == Guid.Empty ||
                (store.Construction is { } construction ? !construction.Returned || construction.Identity != store.After ||
                    construction.Invocation != store.Invocation || construction.Role != FalloutLoadedAnimationRole.Common : store.After is not null))
                throw new InvalidDataException("Common animation pointer lost its ordered source load, constructor, tier or store.");
            pointer = store.After; changed = store.Changed;
        }
        if (pointer != scene.AnimationContext) throw new InvalidDataException("Common animation pointer has no actual returned constructor/store history.");
    }
}
