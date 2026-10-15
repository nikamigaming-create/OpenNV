using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Gameplay.State;

// Source-ordered Player preparation with genuine early returns. A reached
// sequence transition/action selector is a required child, never an idle clip
// substituted for that child or a fabricated successful preparation return.
internal sealed partial class FalloutPlayerAnimationPreparationState
{
    internal const string SequenceBoundary = "source-Player-animation-prepare-current-sequence-transition-child-unowned";
    internal const string TimedGroupBoundary = "source-Player-animation-prepare-current-timed-group-delay-and-conditional-stop-unowned";
    internal const string ActionBoundary = "source-Player-animation-prepare-current-Actor-intent-group-selection-and-activation-unowned";
    private readonly FalloutPlayerAnimationPreparationOwners _owners;
    private readonly Action<FalloutPlayerLateFrameInvocation> _requireActual;
    private readonly Action _requireLifetime;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly Guid _process, _constructor, _common;
    private long _sequence, _ordinal, _fault;
    private bool _busy, _retired;
    private string? _failure;
    internal FalloutPlayerPostAnimationSource Source { get; }
    internal string Stack { get; }
    internal Guid Factory { get; } = Guid.NewGuid();
    internal FalloutPlayerAnimationPreparationCall? LastCall { get; private set; }
    internal string? SaveBlocker => _retired ? "source-Player-animation-prepare-retired" :
        _busy ? "source-Player-animation-prepare-in-flight" : _failure;

    internal FalloutPlayerAnimationPreparationState(FalloutPlayerPostAnimationSource source, string stack,
        Guid process, Guid constructor, Guid common, FalloutPlayerAnimationPreparationOwners owners,
        Action<FalloutPlayerLateFrameInvocation> requireActual, Action requireLifetime)
    {
        source.Validate(); ArgumentException.ThrowIfNullOrWhiteSpace(stack); ArgumentNullException.ThrowIfNull(owners);
        ArgumentNullException.ThrowIfNull(requireActual); ArgumentNullException.ThrowIfNull(requireLifetime); requireLifetime();
        if (process == Guid.Empty || constructor == Guid.Empty || common == Guid.Empty || owners.Scene.Process != process ||
            owners.Scene.Constructor != constructor || owners.Scene.Stack != stack || owners.Scene.Source != source.Tail.Scene ||
            owners.Fields.Process != common || owners.Fields.Constructor != constructor || owners.Fields.Stack != stack)
            throw new InvalidDataException("Player preparation lacks the actual shared World/Common/fields/Scene owners.");
        Source = source; Stack = stack; _process = process; _constructor = constructor; _common = common;
        _owners = owners; _requireActual = requireActual; _requireLifetime = requireLifetime;
    }

    internal FalloutPlayerAnimationPreparationCall Prepare(FalloutPlayerLateFrameInvocation frame)
    {
        RequireIdle(); RequireFrame(frame);
        if (_failure is { } failed) throw new NotSupportedException(failed);
        if (LastCall is { } old && (!old.Returned || old.Frame.Caller.Main == frame.Caller.Main ||
            old.Frame.PlayerProcess == frame.PlayerProcess && old.Frame.Caller.Ordinal >= frame.Caller.Ordinal))
            throw new InvalidOperationException("Preparation replayed or crossed an unreturned actual frame.");
        var fault = _fault; _busy = true;
        LastCall = new(Source.Identity, Stack, frame, Factory, ++_ordinal, Next(), _sequence,
            FalloutPlayerAnimationPreparationStep.Life, null, null, null, null, null, null, null, null, null, null, null, false, null);
        try
        {
            var raw = _owners.Runtime.Life(frame.Common.Actor.Reference);
            LastCall = LastCall with { Life = new(frame.Common, raw, false, null), Changed = Next() }; Check();
            var life = FalloutPlayerFollowingContinuation.LifePredicate(raw.Require(), false);
            LastCall = LastCall with { Life = LastCall.Life! with { Result = life }, Changed = Next() };
            if (life) return Return();
            Step(FalloutPlayerAnimationPreparationStep.Activity);
            var activity = _owners.Common.ReadPhysicalActivity(frame.Common.Actor.Reference, frame.Common.Epoch);
            RequireCommon(activity.Process, frame); LastCall = LastCall with { Activity = activity, Changed = Next() }; Check();
            if (activity.Value != 0) return Return();
            Step(FalloutPlayerAnimationPreparationStep.View);
            var view = _owners.Scene.ReadTailView(Source, frame, CheckFrame);
            LastCall = LastCall with { View = view, Changed = Next() }; Check();
            if (view.Value == 0) return Return();
            Step(FalloutPlayerAnimationPreparationStep.SelectedAnimation);
            var selected = _owners.Fields.ReadAnimationForPreparation(Source, frame,
                () => _owners.Scene.ReadTailView(Source, frame, CheckFrame),
                () => _owners.Common.ReadAnimationContextForReturnTail(frame, CheckFrame), CheckFrame);
            LastCall = LastCall with { Selected = selected, Changed = Next() }; Check();
            Step(FalloutPlayerAnimationPreparationStep.Controller);
            var context = _owners.Common.ReadSceneContext(frame.Common.Actor.Reference, frame.Common.Epoch);
            RequireCommon(context.Process, frame);
            LastCall = LastCall with { Controller = new(context, context.Context, null, null, null, null, null), Changed = Next() }; Check();
            if (selected.Context is null || context.Context is null) return Return();
            using var lease = _owners.Common.BorrowEarlyController(frame.Common.Actor.Reference, frame.Common.Epoch);
            var controller = lease?.Controller;
            if (controller?.Identity != context.Context)
                throw new InvalidDataException("Preparation changed its exact current Common controller pointer.");
            LastCall = LastCall with
            {
                Controller = new(context, controller?.Identity, controller?.Lineage.Factory,
                controller?.Lineage.RuntimeProcess, controller?.ProcessEpoch, null, null),
                Changed = Next()
            }; Check();
            if (controller is null) throw new InvalidDataException("Preparation lost its actual retained nonnull controller.");

            // The selected prerequisite helper is a source-qualified pure
            // return on either conditional arm. No process field value or
            // constructor/allocation receipt is invented for that identity.
            Step(FalloutPlayerAnimationPreparationStep.SelectedSlot);
            var actual = RequireContext(selected.Context.Value, selected.Selection switch
            {
                FalloutPackageAnimationSelection.Process => FalloutLoadedAnimationRole.Common,
                FalloutPackageAnimationSelection.FirstPerson => FalloutLoadedAnimationRole.FirstPerson,
                _ => null,
            });
            var slot = actual.ReadPreparationSlot(Source, frame, CheckFrame);
            LastCall = LastCall with { SelectedSlot = slot, Changed = Next() }; Check();
            if (slot.Selected is not null) throw new NotSupportedException(SequenceBoundary);
            Step(FalloutPlayerAnimationPreparationStep.SelectedGroup);
            var group = actual.ReadPreparationGroup(Source, frame, CheckFrame);
            LastCall = LastCall with { SelectedGroup = group, Changed = Next() }; Check();
            RequireUntimed(group);
            Step(FalloutPlayerAnimationPreparationStep.FirstAnimation);
            var first = _owners.Fields.ReadMoverDeltaFirstAnimation(frame, CheckFrame);
            LastCall = LastCall with { First = first, Changed = Next() }; Check();
            if (first.Context is null)
                throw new NotSupportedException("source-Player-animation-prepare-required-direct-first-context-current-owner-unavailable");
            Step(FalloutPlayerAnimationPreparationStep.FirstSlot);
            var actualFirst = RequireContext(first.Context.Value, FalloutLoadedAnimationRole.FirstPerson);
            var firstSlot = actualFirst.ReadPreparationSlot(Source, frame, CheckFrame);
            LastCall = LastCall with { FirstSlot = firstSlot, Changed = Next() }; Check();
            if (firstSlot.Selected is not null) throw new NotSupportedException(SequenceBoundary);
            Step(FalloutPlayerAnimationPreparationStep.FirstGroup);
            var firstGroup = actualFirst.ReadPreparationGroup(Source, frame, CheckFrame);
            LastCall = LastCall with { FirstGroup = firstGroup, Changed = Next() }; Check();
            RequireUntimed(firstGroup);
            Step(FalloutPlayerAnimationPreparationStep.ActionSelection);
            throw new NotSupportedException(ActionBoundary);
        }
        catch (Exception error)
        {
            _failure ??= string.IsNullOrWhiteSpace(error.Message) ? error.GetType().Name : error.Message;
            LastCall = LastCall! with
            {
                FailedAt = LastCall!.Step,
                Step = FalloutPlayerAnimationPreparationStep.Failed,
                Failure = _failure,
                Changed = Next()
            }; throw;
        }
        finally { _busy = false; }

        FalloutSourceAnimationContextState RequireContext(Guid identity, FalloutLoadedAnimationRole? role)
        {
            var actual = _owners.RequireContext(identity); Check();
            if (actual.Identity != identity || actual.Source != Source.Tail || actual.Process != frame.PlayerProcess ||
                actual.CommonProcess != frame.Common.Process || actual.Actor != frame.Common.Actor ||
                actual.Construction.Invocation.ReferenceConstructor != frame.PlayerConstructor ||
                actual.Construction.Invocation.Epoch != frame.Common.Epoch || role is not null && actual.Construction.Role != role)
                throw new InvalidDataException("Preparation dereferenced another actual AnimData allocation/source/Actor.");
            return actual;
        }
        void CheckFrame(FalloutPlayerLateFrameInvocation current)
        { if (current != frame) throw new InvalidOperationException("Preparation child replaced its actual parent frame."); Check(); }
        void Check()
        { RequireFrame(frame); if (_fault != fault) throw new InvalidOperationException("Preparation swallowed child reentry."); }
        void Step(FalloutPlayerAnimationPreparationStep step)
        { Check(); LastCall = LastCall! with { Step = step, Changed = Next() }; }
        FalloutPlayerAnimationPreparationCall Return()
        { Step(FalloutPlayerAnimationPreparationStep.Returned); return LastCall = LastCall! with { Returned = true, Changed = Next() }; }
    }
    private static void RequireUntimed(FalloutAnimationPreparationGroupRead group)
    {
        if ((byte)group.Group is FalloutPlayerPostAnimationSource.FirstTimedPreparationGroup or
            FalloutPlayerPostAnimationSource.SecondTimedPreparationGroup)
            throw new NotSupportedException(TimedGroupBoundary);
    }
    private static void RequireCommon(FalloutProcessSceneQuery current, FalloutPlayerLateFrameInvocation frame)
    {
        if (current.Process != frame.Common.Process || current.Actor != frame.Common.Actor || current.Epoch != frame.Common.Epoch ||
            current.Level != frame.Common.Level || current.GameplayOwnership != frame.Common.GameplayOwnership ||
            current.ExecutableSha256 != frame.Common.ExecutableSha256 || current.Stack != frame.Common.Stack)
            throw new InvalidDataException("Preparation replaced its actual current Common source/lease.");
    }
    private void RequireFrame(FalloutPlayerLateFrameInvocation frame)
    {
        _requireLifetime(); _requireActual(frame);
        if (_retired || frame.PlayerProcess != _process || frame.PlayerConstructor != _constructor || frame.Common.Process != _common ||
            frame.Common.Stack != Stack || frame.Common.ExecutableSha256 != Source.Tail.Scene.Main.EngineSha256 ||
            !frame.Common.Actor.EnginePlayer || frame.ThreadId != _thread || Environment.CurrentManagedThreadId != _thread)
            throw new InvalidOperationException("Preparation changed its actual current World/frame/thread.");
    }
    private void RequireIdle()
    {
        if (_busy) { ++_fault; throw new InvalidOperationException("Preparation reentered its actual current child."); }
        _requireLifetime(); ObjectDisposedException.ThrowIf(_retired, this);
    }
    private long Next() => checked(++_sequence);
    internal void Retire() { RequireIdle(); _retired = true; }
}
