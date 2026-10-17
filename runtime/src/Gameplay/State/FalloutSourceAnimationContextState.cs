using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Gameplay.State;

internal enum FalloutSourceAnimationContextStep
{ Pending, RequestedGroup, TimedList, ActorPredicate, PairSlots, Root, BoundResource, Returned, Failed }
internal sealed record FalloutSourceAnimationContextCall(FalloutPlayerLateFrameInvocation Frame, Guid Context,
    Guid Factory, long Entered, long Changed, FalloutSourceAnimationContextStep Step,
    FalloutSourceAnimationContextStep? FailedAt, uint DeltaBits, Guid? RootFactory,
    int PendingCount, int TimedCount, int GroupCount, bool ActorPredicate, bool Returned, string? Failure, FalloutLoadedAnimationUpdate? Loaded = null);
internal sealed record FalloutSourceAnimationContextConstruction(string Source, Guid Identity, Guid Factory,
    FalloutReferenceModelDataInvocation Invocation, int GroupBuckets, bool Returned,
    FalloutLoadedAnimationRole Role = FalloutLoadedAnimationRole.FirstPerson);
internal sealed record FalloutSourceAnimationContextSnapshot(FalloutSourceAnimationContextConstruction Construction,
    Guid Process, Guid CommonProcess, long Sequence, Guid? RootFactory, int PendingCount, int TimedCount,
    int GroupCount, Guid? FirstPair, Guid? SecondPair, ushort RequestedGroup,
    string? Writer, FalloutSourceAnimationContextCall? LastCall, string? Failure,
    FalloutLoadedAnimationContextSnapshot? Loaded = null, Guid? RetiringBindingFactory = null,
    string? RetirementFailure = null, bool Retired = false,
    FalloutIdleAnimationAction? IdleFirst = null, FalloutIdleAnimationAction? IdleSecond = null);

// The real managed AnimData object, independently allocated at the original
// source constructor call. A KF clip, idle marker or logical graph is never
// substituted for this object's current root, lists or animation groups.
internal sealed partial class FalloutSourceAnimationContextState
{
    private readonly Action _requireLifetime;
    private readonly Dictionary<ushort, Guid> _groups = new(FalloutPlayerReturnTailSource.AnimationGroupBuckets);
    private readonly List<Guid> _pending = [], _timed = [];
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private FalloutReferenceModelDataNode? _root, _retiringBinding;
    private Guid? _firstPair = null, _secondPair = null;
    private ushort _requested = FalloutPlayerReturnTailSource.NoAnimationGroup;
    private long _sequence, _fault;
    private bool _busy, _retired, _retirementStarted;
    private string? _writer, _failure, _retirementFailure;
    internal FalloutPlayerReturnTailSource Source { get; }
    internal FalloutCombatActorIdentity Actor { get; }
    internal Guid Identity { get; } = Guid.NewGuid();
    internal Guid Factory { get; } = Guid.NewGuid();
    internal Guid Process { get; }
    internal Guid CommonProcess { get; }
    internal FalloutSourceAnimationContextConstruction Construction { get; }
    internal FalloutSourceAnimationContextCall? LastCall { get; private set; }
    internal string? SaveBlocker => _retired ? "source-AnimData-retired" :
        _retirementStarted ? "source-AnimData-retirement-remains-entered" : _busy ? "source-AnimData-update-entered" : _failure ?? _writer ??
        (LastBind is { Returned: false } ? "source-AnimData-bind-not-returned" : _manager?.SaveBlocker);

    internal FalloutSourceAnimationContextState(FalloutPlayerReturnTailSource source,
        FalloutReferenceModelDataInvocation construction, Action<FalloutReferenceModelDataInvocation> requireConstruction,
        Action requireLifetime, FalloutLoadedAnimationRole role = FalloutLoadedAnimationRole.FirstPerson)
    {
        source.Validate(); ArgumentNullException.ThrowIfNull(requireConstruction); ArgumentNullException.ThrowIfNull(requireLifetime);
        requireConstruction(construction); requireLifetime(); construction.Actor.Validate();
        if (construction.Call == Guid.Empty || construction.ReferenceProcess == Guid.Empty || construction.CommonProcess == Guid.Empty ||
            construction.ReferenceConstructor == Guid.Empty || !construction.Actor.EnginePlayer || construction.Epoch < 1 ||
            construction.Ingress != FalloutReferenceModelDataIngress.ThreeDLoad || construction.ThreadId != _thread)
            throw new InvalidDataException("AnimData constructor lacks its actual entered Player 3D-load allocation.");
        Source = source; Actor = construction.Actor; Process = construction.ReferenceProcess; CommonProcess = construction.CommonProcess;
        _requireLifetime = requireLifetime;
        Construction = new(source.Identity, Identity, Factory, construction,
            FalloutPlayerReturnTailSource.AnimationGroupBuckets, true, role);
        requireConstruction(construction); requireLifetime();
    }

    internal FalloutSourceAnimationContextCall Update(FalloutPlayerLateFrameInvocation frame, uint deltaBits,
        Action<FalloutPlayerLateFrameInvocation> requireActual)
    {
        if (_busy) { ++_fault; throw new InvalidOperationException("AnimData update reentered its actual owner."); }
        ArgumentNullException.ThrowIfNull(requireActual); CheckFrame(); var fault = _fault; _busy = true;
        LastCall = new(frame, Identity, Factory, Next(), _sequence, FalloutSourceAnimationContextStep.Pending,
            null, deltaBits, _root?.Node.Factory, _pending.Count, _timed.Count, _groups.Count, false, false, null);
        try
        {
            Check();
            if (_writer is { } writer) throw new NotSupportedException(writer);
            var delta = BitConverter.UInt32BitsToSingle(deltaBits);
            if (!float.IsFinite(delta)) throw new NotSupportedException("source-AnimData-nonfinite-Float32-update-unowned");
            if (_pending.Count != 0)
                throw new NotSupportedException("source-AnimData-pending-group-resource-load-and-removal-child-unowned");
            Step(FalloutSourceAnimationContextStep.RequestedGroup);
            if (_requested != FalloutPlayerReturnTailSource.NoAnimationGroup)
                throw new NotSupportedException("source-AnimData-requested-group-current-state-and-Actor-action-child-unowned");
            Step(FalloutSourceAnimationContextStep.TimedList);
            if (_timed.Count != 0) throw new NotSupportedException("source-AnimData-current-timed-node-subtraction-and-removal-unowned");
            Step(FalloutSourceAnimationContextStep.ActorPredicate);
            // This is the selected Player Character predicate, qualified by
            // the actual class table; it is independent of source life state.
            LastCall = LastCall with { ActorPredicate = true, Changed = Next() }; Check();
            Step(FalloutSourceAnimationContextStep.PairSlots);
            if (_firstPair is not null || _secondPair is not null)
                throw new NotSupportedException("source-AnimData-two-retained-pair-slot-payload-update-unowned");
            Step(FalloutSourceAnimationContextStep.Root);
            if (_root is not null)
            {
                _root.RequireCurrent(); Check(); Step(FalloutSourceAnimationContextStep.BoundResource);
                try { UpdateLoadedRoot(deltaBits, () => CheckFrame(), () => ReadFrameScale(frame), frame: frame); }
                finally { LastCall = LastCall with { Loaded = LastLoadedUpdate?.Frame == frame ? LastLoadedUpdate : null, Changed = Next() }; }
            }
            Step(FalloutSourceAnimationContextStep.Returned);
            LastCall = LastCall with { Returned = true, Changed = Next() }; return LastCall;
        }
        catch (Exception error)
        {
            _failure ??= string.IsNullOrWhiteSpace(error.Message) ? error.GetType().Name : error.Message;
            LastCall = LastCall! with
            {
                FailedAt = LastCall!.Step,
                Step = FalloutSourceAnimationContextStep.Failed,
                Changed = Next(),
                Failure = _failure
            }; throw;
        }
        finally { _busy = false; }

        void CheckFrame()
        {
            _requireLifetime(); requireActual(frame);
            if (_retired || _retirementStarted || frame.PlayerProcess != Process || frame.Common.Process != CommonProcess || frame.Common.Actor != Actor ||
                frame.Common.Epoch != Construction.Invocation.Epoch || frame.PlayerConstructor != Construction.Invocation.ReferenceConstructor ||
                frame.ThreadId != _thread || Environment.CurrentManagedThreadId != _thread)
                throw new InvalidOperationException("AnimData update changed its actual Actor, source constructor, epoch or thread.");
        }
        void Check() { CheckFrame(); if (_fault != fault) throw new InvalidOperationException("AnimData swallowed a real child reentry."); }
        void Step(FalloutSourceAnimationContextStep step) { Check(); LastCall = LastCall! with { Step = step, Changed = Next() }; }
    }

    // The exact model-association store precedes independent group/controller
    // initialization. Retain that real prefix; do not return the whole bind.
    internal void EnterModelBinding(FalloutReferenceModelDataInvocation invocation, FalloutReferenceModelDataNode root,
        Action<FalloutReferenceModelDataInvocation> requireActualBind)
    {
        RequireIdle(); ArgumentNullException.ThrowIfNull(root); ArgumentNullException.ThrowIfNull(requireActualBind);
        requireActualBind(invocation); _requireLifetime(); root.RequireCurrent();
        if (invocation.Actor != Actor || invocation.ReferenceProcess != Process || invocation.CommonProcess != CommonProcess ||
            invocation.Ingress != FalloutReferenceModelDataIngress.ThreeDLoad || invocation.ThreadId != _thread ||
            invocation.Epoch != Construction.Invocation.Epoch || root.Node.Process != Process)
            throw new InvalidOperationException("AnimData binding changed its genuine load/root/source owner.");
        if (_root is not null) throw new NotSupportedException("source-AnimData-old-root-and-group-map-release-unowned");
        root.Retain(() => { _root = root; Next(); });
        _writer = "source-AnimData-model-bind-named-node-and-controller-manager-constructor-unowned";
        requireActualBind(invocation); _requireLifetime();
    }
    internal void ObserveUnownedWriter(string producer, Action requireActual)
    {
        RequireIdle(); ArgumentException.ThrowIfNullOrWhiteSpace(producer); ArgumentNullException.ThrowIfNull(requireActual);
        requireActual(); _writer = "source-AnimData-current-writer-unowned:" + producer; Next(); requireActual();
    }
    internal FalloutSourceAnimationContextSnapshot Capture() => new(Construction, Process, CommonProcess,
        _sequence, _root?.Node.Factory, _pending.Count, _timed.Count, _groups.Count, _firstPair, _secondPair,
        _requested, _writer, LastCall, _failure, CaptureLoaded(), _retiringBinding?.Node.Factory, _retirementFailure, _retired,
        _idleFirst, _idleSecond);
    private void RequireIdle()
    {
        _requireLifetime();
        if (_busy) { ++_fault; throw new InvalidOperationException("AnimData writer reentered its actual current update."); }
        if (_retired || _retirementStarted || _thread != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("AnimData lost its actual current source lifetime/thread.");
    }
    private long Next() => checked(++_sequence);
    internal bool Retired => _retired;
    internal Guid? RetainedModelFactory => _root?.Node.Factory ?? _retiringBinding?.Node.Factory;
    internal void Retire()
    {
        if (_retired) return;
        if (_busy || _thread != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("AnimData retirement left its actual idle source owner/thread.");
        _retirementStarted = true;
        try
        {
            // Manager teardown precedes release of the graph it still uses.
            // Independent contexts are drained by the containing facade.
            _manager?.Retire();
            if (_retiringBinding is { } pending)
            {
                var state = pending.RetirementState;
                if (state is not (FalloutReferenceModelBindingRetirementState.Retained or
                    FalloutReferenceModelBindingRetirementState.BindingPending or FalloutReferenceModelBindingRetirementState.Retired))
                    throw new InvalidOperationException("AnimData final binding retry lost its committed source release.");
                // A committed shared release may leave another owner retaining
                // the node. Only its genuine zero-reference state permits the
                // independent final binding retry.
                if (state == FalloutReferenceModelBindingRetirementState.BindingPending) pending.RetireBinding();
                _retiringBinding = null; Next();
            }
            if (_root is { } root)
            {
                root.Release(() => { _root = null; _retiringBinding = root; Next(); });
                // A normal return either leaves other real source leases or
                // returns final binding retirement. Neither needs another call.
                _retiringBinding = null;
            }
            _retired = true; Next();
        }
        catch (Exception error) { _retirementFailure ??= error.Message; throw; }
    }
}
