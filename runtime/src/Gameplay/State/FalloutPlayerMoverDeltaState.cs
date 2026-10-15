using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;
using OpenNV.Runtime.World.Physics;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutPlayerMoverDeltaState
{
    internal const string ActorMovementBoundary = "source-Actor-mover-delta-nonnull-reference-data-transform-camera-position-body-unbound";
    internal const string AnimationBoundary = "source-Player-mover-delta-nonnull-AnimData-vector-producer-unbound";
    private readonly FalloutPlayerMoverDeltaOwners _owners;
    private readonly FalloutPluginStack _records;
    private readonly Action<FalloutPlayerLateFrameInvocation> _requireActual;
    private readonly Action _requireLifetime;
    private readonly List<FalloutPlayerMoverDeltaPublication> _publications = [];
    private long _sequence, _calls, _fault;
    private bool _busy, _retired;
    private string? _failure;
    internal FalloutPlayerMoverDeltaSource Source { get; }
    internal string Stack { get; }
    internal Guid Process { get; }
    internal Guid Factory { get; } = Guid.NewGuid();
    internal Guid Constructor { get; }
    internal FalloutPlayerMoverDeltaCall? LastCall { get; private set; }
    internal string? SaveBlocker => _busy ? "source-Player-mover-delta-in-flight" : _failure ??
        (_retired ? "source-Player-mover-delta-retired" : null);

    internal FalloutPlayerMoverDeltaState(FalloutPlayerMoverDeltaSource source, FalloutPluginStack records,
        string stack, Guid process, Guid constructor, FalloutProcessSceneQuery current,
        FalloutPlayerMoverDeltaOwners owners, Action<FalloutPlayerLateFrameInvocation> requireActual,
        Action requireLifetime, FalloutPlayerMoverDeltaSnapshot? saved = null)
    {
        source.Validate(); ArgumentNullException.ThrowIfNull(records); ArgumentNullException.ThrowIfNull(owners);
        ArgumentNullException.ThrowIfNull(requireActual); ArgumentNullException.ThrowIfNull(requireLifetime);
        ArgumentException.ThrowIfNullOrWhiteSpace(stack);
        Source = source; _records = records; Stack = stack; Process = process; Constructor = constructor;
        _owners = owners; _requireActual = requireActual; _requireLifetime = requireLifetime;
        if (process == Guid.Empty || constructor == Guid.Empty || owners.Mover.Process != process || owners.Mover.Constructor != constructor ||
            owners.Mover.Stack != stack || owners.Mover.Source.Controls != source.Controller.Fields.Controls ||
            owners.Fields.Process != current.Process || owners.Fields.Constructor != constructor || owners.Fields.Actor != current.Actor ||
            owners.Fields.Epoch != current.Epoch || owners.Fields.Source != source.Controller.Fields ||
            owners.ReferenceData.Source != source || owners.ReferenceData.Process != process || owners.ReferenceData.Constructor != constructor ||
            owners.ReferenceData.Actor != current.Actor || owners.ReferenceData.CommonProcess != current.Process ||
            owners.ReferenceData.Epoch != current.Epoch || owners.ReferenceData.Stack != stack ||
            owners.Interface.Source.Main != source.Controller.Fields.Controls.Main || owners.Interface.Stack != stack ||
            current.Stack != stack || current.Level != FalloutDetectionProcessLevel.High || !current.Actor.EnginePlayer || current.GameplayOwnership == Guid.Empty)
            throw new InvalidDataException("Mover delta changed its actual shared fields/records/Common/Player/controller source owners.");
        if (saved is not null)
        {
            Validate(saved); if (saved.Source != source || saved.Stack != stack || saved.PlayerProcess == process || saved.Constructor == constructor ||
                saved.Publications[^1].Actor != current.Actor || saved.Publications[^1].Epoch != current.Epoch)
                throw new InvalidDataException("Cold mover delta changed source or reused an old actual caller/factory.");
            _sequence = saved.Sequence; _calls = saved.Calls; LastCall = saved.LastCall; _failure = saved.Failure;
            _publications.AddRange(saved.Publications);
        }
        _publications.Add(new(process, Factory, constructor, current.Process, current.Actor, current.Epoch, _sequence,
            saved?.PlayerProcess, saved?.Factory));
    }

    internal FalloutPlayerMoverDeltaCall Update(FalloutPlayerLateFrameInvocation invocation)
    {
        RequireIdle(); RequireInvocation(invocation); _requireLifetime();
        if (_failure is { } failure) throw new NotSupportedException(failure);
        if (LastCall is { } previous && (previous.Returned == false || previous.Invocation.Caller.Main == invocation.Caller.Main ||
            previous.Invocation.PlayerProcess == Process && previous.Invocation.Caller.Ordinal >= invocation.Caller.Ordinal))
            throw new InvalidOperationException("Mover delta replayed a previous or unreturned actual frame child.");
        var vector = _owners.Mover.LastPlayerFrameDefaultVector ?? throw new InvalidOperationException("Mover delta has no returned preceding XY/Z store.");
        RequireDefault(vector, invocation, _owners.Mover.Factory);
        var mover = _owners.Mover.ReadPlayerFields(); var fault = _fault; _busy = true; ++_calls;
        LastCall = new(invocation, Factory, _calls, Next(), _sequence, FalloutPlayerMoverDeltaStep.DefaultVector, null,
            vector, mover, [], null, null, null, [], null, [], null, [], [], null, null,
            FalloutActorMoverVector.Default, null, null, null, false, null);
        try
        {
            Check(); Step(FalloutPlayerMoverDeltaStep.CommonAnimation);
            var common = ReadProcess(); Guid? selected = common.AnimationContext;
            LastCall = LastCall with { SelectedAnimation = selected, Changed = Next() };
            Step(FalloutPlayerMoverDeltaStep.View);
            var view = _owners.ReadView(invocation); Check(); RequireView(view, invocation, Source);
            LastCall = LastCall with { View = view, SelectedAnimation = selected, Changed = Next() };
            if (view.View == 0)
            {
                Step(FalloutPlayerMoverDeltaStep.FirstAnimation);
                var first = _owners.Fields.ReadMoverDeltaFirstAnimation(invocation, CheckCurrent);
                LastCall = LastCall with { FirstAnimation = first, Changed = Next() }; Check();
                if (selected is not null && first.Context is not null)
                {
                    Step(FalloutPlayerMoverDeltaStep.AnimationEquality);
                    var firstGroup = Context(first.Context.Value).ReadMovementGroup(invocation, CheckCurrent);
                    LastCall = LastCall with { AnimationGroups = [firstGroup], Changed = Next() };
                    var commonGroup = Context(selected.Value).ReadMovementGroup(invocation, CheckCurrent);
                    LastCall = LastCall with { AnimationGroups = [firstGroup, commonGroup], Changed = Next() };
                    if (commonGroup.Word == firstGroup.Word) selected = first.Context;
                }
                if (selected is null) selected = first.Context;
                LastCall = LastCall with { SelectedAnimation = selected, Changed = Next() };
            }
            Step(FalloutPlayerMoverDeltaStep.Activity);
            var activity = ReadActivity();
            // The Actor predicate and following six comparison invoke the
            // mutable selected Common getter separately, in source order.
            bool overridden = false;
            if (activity.Value != 0)
            {
                Step(FalloutPlayerMoverDeltaStep.ActivitySix);
                overridden = ReadActivity().Value == 6;
            }
            if (!overridden)
            {
                Step(FalloutPlayerMoverDeltaStep.Life);
                var raw = _owners.Runtime.Life(invocation.Common.Actor.Reference);
                var life = new FalloutPlayerFollowingLifeRead(invocation.Common, raw, false, FalloutPlayerFollowingContinuation.LifePredicate(raw.Require(), false));
                LastCall = LastCall with { Life = life, Changed = Next() }; Check(); overridden = life.Result == true;
            }
            if (!overridden)
            {
                Step(FalloutPlayerMoverDeltaStep.SpecialMotion);
                overridden = ReadByte(FalloutMoverDeltaProcessCell.SpecialMotion).Value != 0;
            }
            LastCall = LastCall with { Override = overridden, Changed = Next() };
            if (overridden)
            { Step(FalloutPlayerMoverDeltaStep.Override); throw new NotSupportedException("source-Actor-mover-delta-special-vector-extraction-and-rotation-unbound"); }
            if (selected is not null)
            {
                Step(FalloutPlayerMoverDeltaStep.AnimationVector); var currentFlags = ReadFlags();
                var vectorRead = Context(selected.Value).ReadMovementVector(invocation, currentFlags,
                    () => (_owners.ReadAnimationActorMode ?? throw new NotSupportedException("source-animation-vector-raw-Actor19C-owner-unbound"))(invocation),
                    () => (_owners.ReadAnimationScale ?? throw new NotSupportedException("source-animation-vector-current-reference-scale-owner-unbound"))(invocation), CheckCurrent);
                LastCall = LastCall with { AnimationMovement = vectorRead, Delta = vectorRead.Delta, Changed = Next() };
            }
            Step(FalloutPlayerMoverDeltaStep.Controller); ReadController(contact: true);
            if ((mover.Flags & FalloutPlayerMoverDeltaSource.DirectionMask) != 0)
            { Step(FalloutPlayerMoverDeltaStep.Direction); throw new NotSupportedException("source-Player-mover-delta-directed-AnimData-length-and-current-speed-unbound"); }
            if ((mover.Flags & FalloutPlayerMoverDeltaSource.ForwardMask) != 0 && (mover.Flags & FalloutPlayerMoverDeltaSource.TurningMask) != 0)
            { Step(FalloutPlayerMoverDeltaStep.Turning); throw new NotSupportedException("source-Player-mover-delta-current-status-speed-and-turn-trig-unbound"); }
            Step(FalloutPlayerMoverDeltaStep.ReferenceFlags); var flags = ReadFlags();
            if ((flags.Flags & FalloutPlayerMoverDeltaSource.DirectPositionFlag) != 0)
            {
                Step(FalloutPlayerMoverDeltaStep.DirectPosition);
                var result = _owners.WithPosition(invocation, scope =>
                {
                    Check(); RequirePosition(scope, invocation);
                    var position = _owners.Position.ReadCurrentPosition(scope);
                    var value = Add(position.Position, LastCall.Delta);
                    return _owners.Position.StoreActorPosition(scope, value, _owners.PositionConsumers);
                });
                LastCall = LastCall with { Position = result, Changed = Next() }; Check();
                if (!result.Returned) throw new InvalidOperationException("Mover delta lost its actual reference/controller position return.");
                return Return();
            }
            Step(FalloutPlayerMoverDeltaStep.MenuPredicate);
            var menu = _owners.Interface.ReadMoverDeltaDialogPredicate(invocation, CheckCurrent);
            LastCall = LastCall with { Menu = menu, Changed = Next() }; Check();
            var menuBlock = false;
            if (menu.Value)
            {
                Step(FalloutPlayerMoverDeltaStep.MenuMotion);
                menuBlock = ReadByte(FalloutMoverDeltaProcessCell.DialogMotion).Value != 0;
            }
            if (!menuBlock || (mover.Flags & FalloutPlayerMoverDeltaSource.MotionMask) != 0)
            {
                Step(FalloutPlayerMoverDeltaStep.MovementController); ReadController(contact: false);
                Step(FalloutPlayerMoverDeltaStep.ReferenceData);
                var data = _owners.ReferenceData.ReadForMover(invocation, CheckCurrent);
                LastCall = LastCall with { Data = data, Changed = Next() }; Check();
                if (data.Data is not null)
                {
                    Step(FalloutPlayerMoverDeltaStep.ActorMovement);
                    var body = _owners.ActorMovement ?? throw new NotSupportedException(ActorMovementBoundary);
                    var child = new FalloutPlayerActorMovementInvocation(Guid.NewGuid(), invocation, LastCall.Delta, mover.Flags, data);
                    FalloutPlayerActorMovementCall movement;
                    try { movement = body.Update(child); }
                    finally
                    {
                        var retained = body.LastCall;
                        LastCall = LastCall with { ActorMovement = retained?.Invocation == child ? retained : null, Changed = Next() };
                    }
                    Check();
                    if (!movement.Returned) throw new InvalidOperationException("Mover delta received an unreturned actual Actor movement child.");
                }
            }
            LastCall = LastCall with { MovementReturned = true, Changed = Next() };
            Step(FalloutPlayerMoverDeltaStep.ParentCell);
            var parent = _owners.ReadParent(invocation); Check(); RequireParent(parent, invocation);
            LastCall = LastCall with { Parent = parent, Changed = Next() };
            if (parent.Cell is { } cell && (FalloutCellSceneReader.ReadDefinition(_records, cell).Flags & FalloutCellSceneReader.InteriorCellFlag) == 0)
            { Step(FalloutPlayerMoverDeltaStep.ExteriorWater); throw new NotSupportedException("source-Player-mover-delta-exterior-height-water-current-cell-query-unbound"); }
            return Return();
        }
        catch (Exception error)
        {
            _failure ??= error.Message; LastCall = LastCall! with
            {
                FailedAt = LastCall!.Step,
                Step = FalloutPlayerMoverDeltaStep.Failed,
                Failure = _failure,
                Changed = Next()
            }; throw;
        }
        finally { _busy = false; }

        void CheckCurrent(FalloutPlayerLateFrameInvocation current)
        { if (current != invocation) throw new InvalidOperationException("Mover delta leaf changed its actual caller."); Check(); }
        FalloutSourceAnimationContextState Context(Guid identity)
        {
            var context = (_owners.RequireAnimationContext ?? throw new NotSupportedException(AnimationBoundary))(identity);
            if (context.Identity != identity) throw new InvalidOperationException("Mover delta selected another current animation object.");
            Check(); return context;
        }
        void Check()
        {
            RequireInvocation(invocation); _requireLifetime();
            if (_fault != fault) throw new InvalidOperationException("Mover delta swallowed an actual owner reentry refusal.");
        }
        void Step(FalloutPlayerMoverDeltaStep step) { Check(); LastCall = LastCall! with { Step = step, Changed = Next() }; }
        FalloutProcessSceneQuery ReadProcess()
        {
            var current = _owners.Common.ReadSceneQueries(invocation.Common.Actor.Reference, invocation.Common.Epoch);
            Check(); RequireCommon(current, invocation);
            LastCall = LastCall! with { ProcessReads = [.. LastCall!.ProcessReads, current], Changed = Next() }; return current;
        }
        FalloutMoverDeltaProcessRead ReadByte(FalloutMoverDeltaProcessCell cell)
        {
            var read = _owners.Common.ReadMoverDeltaProcessByte(invocation, cell, CheckCurrent);
            LastCall = LastCall! with { ProcessBytes = [.. LastCall!.ProcessBytes, read], Changed = Next() }; Check(); return read;
        }
        FalloutProcessPhysicalActivityQuery ReadActivity()
        {
            var read = _owners.Common.ReadPhysicalActivity(invocation.Common.Actor.Reference, invocation.Common.Epoch);
            RequireCommon(read.Process, invocation);
            LastCall = LastCall! with { Activities = [.. LastCall!.Activities, read], Changed = Next() }; Check(); return read;
        }
        FalloutReferenceFlagRead ReadFlags()
        {
            var read = _owners.Flags.ReadFrameReferenceFlags(invocation, CheckCurrent);
            LastCall = LastCall! with { Flags = [.. LastCall!.Flags, read], Changed = Next() }; Check(); return read;
        }
        void ReadController(bool contact)
        {
            var context = _owners.Common.ReadSceneContext(invocation.Common.Actor.Reference, invocation.Common.Epoch);
            RequireCommon(context.Process, invocation); Check();
            using var lease = _owners.Common.BorrowEarlyController(invocation.Common.Actor.Reference, invocation.Common.Epoch);
            var controller = lease?.Controller;
            if (controller?.Identity != context.Context) throw new InvalidDataException("Mover delta borrowed another actual Common controller pointer.");
            byte? flags = null; int? count = null;
            if (contact && controller is not null)
            { flags = controller.Contact.Snapshot.Flags; if (flags != 0) count = controller.Contact.Snapshot.ContactCount; }
            LastCall = LastCall! with
            {
                Controllers = [.. LastCall!.Controllers, new(context, controller?.Identity,
                controller?.Lineage.Factory, controller?.Lineage.RuntimeProcess, controller?.ProcessEpoch, flags, count)],
                Changed = Next()
            };
            Check();
        }
        FalloutPlayerMoverDeltaCall Return()
        { Step(FalloutPlayerMoverDeltaStep.Returned); LastCall = LastCall! with { Returned = true, Changed = Next() }; return LastCall; }
    }
    private void RequireInvocation(FalloutPlayerLateFrameInvocation invocation)
    {
        ValidateInvocation(invocation, Source, Stack); _requireActual(invocation);
        if (invocation.PlayerProcess != Process || invocation.PlayerConstructor != Constructor || invocation.ThreadId != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Mover delta lost its actual current Scene constructor/process/thread.");
    }
    private void RequireIdle()
    {
        ObjectDisposedException.ThrowIf(_retired, this);
        if (_busy) { ++_fault; throw new InvalidOperationException("Mover delta reentered its actual current owner."); }
    }
    private long Next() => _sequence = checked(_sequence + 1);
    internal FalloutPlayerMoverDeltaSnapshot Capture()
    {
        RequireIdle(); var snapshot = new FalloutPlayerMoverDeltaSnapshot(Source, Stack, Process, Factory, Constructor,
            _sequence, _calls, _publications.ToArray(), LastCall, _failure); Validate(snapshot); return snapshot;
    }
    internal void Retire() { if (_retired) return; RequireIdle(); _retired = true; }
    internal static FalloutPackageRawPosition Add(FalloutPackageRawPosition position, FalloutActorMoverVector delta) => new(
        Bits(FalloutPhysicalFloat32.Add(Value(position.X), Value(delta.X))),
        Bits(FalloutPhysicalFloat32.Add(Value(position.Y), Value(delta.Y))),
        Bits(FalloutPhysicalFloat32.Add(Value(position.Z), Value(delta.Z))));
    private static uint Bits(float value) => BitConverter.SingleToUInt32Bits(value);
    private static float Value(uint bits) => BitConverter.UInt32BitsToSingle(bits);
}
