using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;
using OpenNV.Runtime.World.Physics;
using F = OpenNV.Runtime.World.Physics.FalloutPhysicalFloat32;

namespace OpenNV.Runtime.Gameplay.State;

// The actual selected nonnull-data Actor body. Existing owners keep each
// mutable field; this owner preserves caller order and the failed prefix.
internal sealed partial class FalloutPlayerActorMovementState
{
    private readonly FalloutPlayerActorMovementOwners _owners;
    private readonly Action<FalloutPlayerActorMovementInvocation> _requireActual;
    private readonly Action _requireLifetime;
    private readonly List<FalloutPlayerActorMovementPublication> _publications = [];
    private long _sequence, _calls, _fault;
    private bool _busy, _retired;
    private string? _failure;
    internal FalloutPlayerActorMovementSource Source { get; }
    internal string Stack { get; }
    internal Guid Process { get; }
    internal Guid Factory { get; } = Guid.NewGuid();
    internal Guid Constructor { get; }
    internal FalloutPlayerActorMovementCall? LastCall { get; private set; }
    internal string? SaveBlocker => _busy ? "source-Player-Actor-movement-in-flight" : _failure ??
        (_retired ? "source-Player-Actor-movement-retired" : null);

    internal FalloutPlayerActorMovementState(FalloutPlayerActorMovementSource source, string stack, Guid process,
        Guid constructor, FalloutProcessSceneQuery common, FalloutPlayerActorMovementOwners owners,
        Action<FalloutPlayerActorMovementInvocation> requireActual, Action requireLifetime,
        FalloutPlayerActorMovementSnapshot? saved = null)
    {
        source.Validate(); ArgumentException.ThrowIfNullOrWhiteSpace(stack); ArgumentNullException.ThrowIfNull(owners);
        ArgumentNullException.ThrowIfNull(requireActual); ArgumentNullException.ThrowIfNull(requireLifetime);
        if (!common.Actor.EnginePlayer || common.Stack != stack || process == Guid.Empty || constructor == Guid.Empty ||
            owners.Position.Process != process || owners.Position.Constructor != constructor || owners.Position.Actor != common.Actor ||
            owners.Position.Source != source.Cells.Reference || owners.Data.Source != source.Mover || owners.Data.Process != process ||
            owners.Data.Constructor != constructor || owners.Data.CommonProcess != common.Process || owners.Data.Actor != common.Actor ||
            owners.Orientation.Source != source.Orientation.Controls || owners.Orientation.Process != process || owners.Orientation.Stack != stack ||
            owners.Orientation.Binding.Reference != common.Actor.Reference || owners.Cells.Source != source.Cells || owners.Cells.Stack != stack ||
            owners.Mover.Process != process || owners.Mover.Constructor != constructor || owners.Mover.Stack != stack ||
            owners.Projection.Source != source.Orientation || owners.Projection.Stack != stack)
            throw new InvalidDataException("Actor movement changed its actual shared reference/controller/camera/INI/TES field owners.");
        if (!ReferenceEquals(owners.Records.IniSettings, owners.Settings))
            throw new InvalidDataException("Actor movement replaced its winning record stack's actual numeric INI owner.");
        owners.Orientation.RequireControlFields(owners.Controls);
        Source = source; Stack = stack; Process = process; Constructor = constructor;
        _owners = owners; _requireActual = requireActual; _requireLifetime = requireLifetime;
        if (saved is not null)
        {
            Validate(saved);
            if (saved.Source != source || saved.Stack != stack || saved.PlayerProcess == process || saved.Constructor == constructor ||
                saved.Publications[^1].Actor != common.Actor || saved.Publications[^1].Epoch != common.Epoch)
                throw new InvalidDataException("Cold Actor movement reused a historical caller or changed the actual source Actor.");
            _sequence = saved.Sequence; _calls = saved.Calls; _failure = saved.Failure; LastCall = saved.LastCall;
            _publications.AddRange(saved.Publications);
        }
        _publications.Add(new(process, Factory, constructor, common.Process, common.Actor, common.Epoch, _sequence,
            saved?.PlayerProcess, saved?.Factory));
    }
    internal FalloutPlayerActorMovementCall Update(FalloutPlayerActorMovementInvocation invocation)
    {
        RequireIdle(); RequireInvocation(invocation);
        if (_failure is { } failure) throw new NotSupportedException(failure);
        if (LastCall is { } previous && (!previous.Returned || previous.Invocation.Frame.Caller.Main == invocation.Frame.Caller.Main ||
            previous.Invocation.Frame.PlayerProcess == Process && previous.Invocation.Frame.Caller.Ordinal >= invocation.Frame.Caller.Ordinal))
            throw new InvalidOperationException("Actor movement replayed a failed or returned current frame child.");
        var frame = invocation.Frame; var fault = _fault; _busy = true; ++_calls;
        LastCall = new(invocation, Factory, _calls, Next(), _sequence, FalloutPlayerActorMovementStep.Controller,
            FailedAt: null, Context: null, FormKind: null, RawPosition: null, Angles: null, ResetAngles: null,
            SkipFrames: null, Disabled: null, Status: null, Camera: null, Positions: [], RawLife: null,
            Lives: [], LifeOne: null, Activity: null, WarpDistance: null,
            CameraDistanceSquared: null, DisabledFlag: null, Controlled: null, DeltaChild: null,
            ControllerFlags: [], ControllerPosition: null, ActorPosition: null, TesCell: null,
            Attachments: null, BasePosition: null, FinalPosition: null, Returned: false, Failure: null);
        try
        {
            Check(); var context = _owners.Common.ReadSceneContext(frame.Common.Actor.Reference, frame.Common.Epoch);
            RequireCommon(context.Process); LastCall = LastCall with { Context = context, Changed = Next() };
            using var lease = _owners.Common.BorrowEarlyController(frame.Common.Actor.Reference, frame.Common.Epoch);
            var controller = lease?.Controller;
            if (controller?.Identity != context.Context) throw new InvalidDataException("Actor movement borrowed another actual Common controller pointer.");
            Step(FalloutPlayerActorMovementStep.FormKind);
            var kind = _owners.ReadFormKind(frame); RequireReference(kind.Source, kind.Stack, kind.Process, kind.Factory, kind.Constructor, kind.Actor);
            LastCall = LastCall with { FormKind = kind, Changed = Next() }; Check();
            Step(FalloutPlayerActorMovementStep.RawPosition);
            var raw = _owners.ReadRawPosition(frame); RequireReference(raw.Source, raw.Stack, raw.Process, raw.Factory, raw.Constructor, raw.Actor);
            LastCall = LastCall with { RawPosition = raw, Changed = Next() }; Check();
            var position = raw.Position;
            Step(FalloutPlayerActorMovementStep.Angles);
            var angles = _owners.Orientation.ReadActorMovementAngles(frame, CheckFrame);
            LastCall = LastCall with { Angles = angles, Changed = Next() }; Check();
            if (!ValidAngle(angles.Angles.X) || !ValidAngle(angles.Angles.Y) || !ValidAngle(angles.Angles.Z))
            {
                Step(FalloutPlayerActorMovementStep.AngleReset);
                angles = _owners.Orientation.ResetExceptionalActorMovementAngles(frame, CheckFrame, _owners.RotationChanges);
                LastCall = LastCall with { ResetAngles = angles, Changed = Next() }; Check();
            }
            Step(FalloutPlayerActorMovementStep.SkipFrames);
            var skip = Setting(FalloutPlayerActorMovementSource.SkipFramesName, 'i');
            LastCall = LastCall with { SkipFrames = skip, Changed = Next() }; Check();
            if (skip.Number != 0)
                throw new NotSupportedException("source-Actor-movement-Havok-skip-frame-current-counter-and-model-transform-child-unbound");
            Step(FalloutPlayerActorMovementStep.DisabledWord);
            var disabled = _owners.Controls.ReadActorMovementDisabledWord(frame, CheckFrame);
            LastCall = LastCall with { Disabled = disabled, Changed = Next() }; Check(); var blocked = (disabled.Word & 1) != 0;
            Step(FalloutPlayerActorMovementStep.Status);
            var status = _owners.ReadStatus(frame); RequireReference(status.Source, status.Stack, status.Process, status.Factory, status.Constructor, status.Actor);
            LastCall = LastCall with { Status = status, Changed = Next() }; Check();
            if (status.Mode != 0)
                throw new NotSupportedException("source-Actor-movement-current-status-controller-quaternion-selection-unbound");
            Step(FalloutPlayerActorMovementStep.Camera);
            var camera = _owners.Projection.Current.ReadActorMovementCamera(frame, CheckFrame);
            LastCall = LastCall with { Camera = camera, Changed = Next() }; Check();
            Step(FalloutPlayerActorMovementStep.Position); var currentPosition = ReadPosition();
            var cameraDistance = DistanceSquared(currentPosition.Position, camera.Translation);
            LastCall = LastCall with { CameraDistanceSquared = cameraDistance, Changed = Next() };
            Step(FalloutPlayerActorMovementStep.RawLife); var life = _owners.Runtime.Life(frame.Common.Actor.Reference);
            LastCall = LastCall with { RawLife = life, Changed = Next() }; Check();
            if (life.Require() != 3)
            {
                Step(FalloutPlayerActorMovementStep.FirstLife);
                if (!Life(false).Result!.Value)
                {
                    Step(FalloutPlayerActorMovementStep.LifeOne);
                    var oneRaw = _owners.Runtime.Life(frame.Common.Actor.Reference);
                    var one = new FalloutPlayerActorMovementLifeOneRead(oneRaw, oneRaw.Require() == 1);
                    LastCall = LastCall with { LifeOne = one, Changed = Next() }; Check();
                    if (!one.Value)
                    {
                        Step(FalloutPlayerActorMovementStep.Activity);
                        var activity = _owners.Common.ReadPhysicalActivity(frame.Common.Actor.Reference, frame.Common.Epoch);
                        RequireCommon(activity.Process); LastCall = LastCall with { Activity = activity, Changed = Next() }; Check();
                    }
                }
            }
            Step(FalloutPlayerActorMovementStep.WarpDistance);
            var warp = Setting(FalloutPlayerActorMovementSource.WarpDistanceName, 'f');
            LastCall = LastCall with { WarpDistance = warp, Changed = Next() }; Check();
            // Source performs the distance comparison first. The current
            // engine Player bypasses only the following nonplayer grid work.
            _ = Value(cameraDistance) > (float)warp.Number!.Value;
            if (controller is null)
                throw new NotSupportedException("source-Player-Actor-movement-null-controller-at-mandatory-flag-store");
            var child = new FalloutControllerActorMovementInvocation(Guid.NewGuid(), invocation, context,
                _owners.Runtime.CharacterControllerSourceProcess);
            Step(FalloutPlayerActorMovementStep.DisabledFlag);
            var stored = controller.StoreActorMovementDisabledFlag(Source, child, blocked, CheckController);
            LastCall = LastCall with { DisabledFlag = stored, Changed = Next() }; Check();
            Step(FalloutPlayerActorMovementStep.SecondLife);
            if (Life(true).Result!.Value)
                throw new NotSupportedException("source-Actor-movement-life-true-rotation-and-current-vector-child-unbound");
            Step(FalloutPlayerActorMovementStep.Pose); var beforeFlags = ReadFlags(controller, child);
            if ((beforeFlags.Flags & FalloutPlayerActorMovementSource.ControllerPoseFlag) != 0)
                throw new NotSupportedException("source-Actor-movement-current-controller-pose-vector-child-unbound");
            Step(FalloutPlayerActorMovementStep.TimedFlags);
            if ((beforeFlags.Flags & FalloutPlayerActorMovementSource.ControllerTimedMask) != 0)
                throw new NotSupportedException("source-Actor-movement-current-timed-controller-scalar-writer-unbound");
            Step(FalloutPlayerActorMovementStep.MoverWord); var word = _owners.Mover.ReadPlayerFields().Flags;
            LastCall = LastCall with { FreshMoverWord = word, Changed = Next() }; Check();
            if ((word & FalloutPlayerMoverDeltaSource.MotionMask) != 0)
                throw new NotSupportedException("source-Actor-movement-current-requested-speed-and-vector-producer-unbound");
            if (blocked)
            {
                Step(FalloutPlayerActorMovementStep.ControlledChild);
                var returned = controller.UpdateControlledActorMovement(Source, child, CheckController);
                LastCall = LastCall with { Controlled = returned, Changed = Next() }; Check();
            }
            else
            {
                Step(FalloutPlayerActorMovementStep.DeltaChild);
                var deltaChild = new FalloutControllerMoverDeltaInvocation(Guid.NewGuid(), frame, context, child.RuntimeProcess,
                    angles.Angles, invocation.Delta, word);
                var returned = controller.UpdateMoverDelta(Source.Mover, deltaChild, _owners.Speed, current =>
                {
                    if (current != deltaChild) throw new InvalidOperationException("Actor movement changed its real delta child.");
                    Check(); _owners.Common.RequireControllerMoverDelta(current, controller);
                });
                LastCall = LastCall with { DeltaChild = returned, Changed = Next() }; Check();
            }
            Step(FalloutPlayerActorMovementStep.ControllerFlags); var flags = ReadFlags(controller, child);
            if ((flags.Flags & FalloutPlayerActorMovementSource.ControllerCorrectionFlag) != 0)
                throw new NotSupportedException("source-Actor-movement-controller-correction-height-and-Actor-position-child-unbound");
            if ((flags.Flags & FalloutPlayerActorMovementSource.ControllerPoseFlag) == 0)
            {
                Step(FalloutPlayerActorMovementStep.ControllerPosition);
                var proxyPosition = controller.ReadActorMovementReferencePosition(Source, child, CheckController);
                LastCall = LastCall with { ControllerPosition = proxyPosition, Changed = Next() }; Check();
                position = new(proxyPosition.SourcePosition.X, proxyPosition.SourcePosition.Y, proxyPosition.SourcePosition.Z);
                if ((flags.Flags & FalloutPlayerActorMovementSource.ControllerTurnFlag) != 0 && !blocked)
                    throw new NotSupportedException("source-Actor-movement-current-controller-yaw-setter-unbound");
                Step(FalloutPlayerActorMovementStep.CorrectionPosition); var previousPosition = ReadPosition();
                if (Value(DistanceSquared(position, new(previousPosition.Position.X, previousPosition.Position.Y, previousPosition.Position.Z))) >
                    Value(FalloutPlayerActorMovementSource.PositionCorrectionDistanceSquaredBits))
                {
                    // The original correction copies a fresh reference getter
                    // result back into the local candidate before the setter.
                    position = ReadPosition().Position; Step(FalloutPlayerActorMovementStep.ActorPosition);
                    var corrected = _owners.StoreActorPosition(frame, position); RequirePositionStore(corrected, frame, "Actor-reference-position");
                    LastCall = LastCall with { ActorPosition = corrected, Changed = Next() }; Check();
                }
                Step(FalloutPlayerActorMovementStep.TesCell); var cell = _owners.ReadTesCell(frame);
                RequireTes(cell); LastCall = LastCall with { TesCell = cell, Changed = Next() }; Check();
                if (cell.Cell is null)
                { Step(FalloutPlayerActorMovementStep.Ground); throw new NotSupportedException("source-Actor-movement-current-TES-grid-world-ground-height-child-unbound"); }
            }
            Step(FalloutPlayerActorMovementStep.Attachments);
            // The source freshly gets the same controller twice before its
            // same pending head/tail null predicate. It creates no list.
            var firstAttachment = _owners.Common.ReadSceneContext(frame.Common.Actor.Reference, frame.Common.Epoch);
            RequireCommon(firstAttachment.Process); Check();
            if (firstAttachment.Context != controller.Identity) throw new InvalidDataException("Attachment child changed its current Common controller.");
            var secondAttachment = _owners.Common.ReadSceneContext(frame.Common.Actor.Reference, frame.Common.Epoch);
            RequireCommon(secondAttachment.Process); Check();
            if (secondAttachment.Context != controller.Identity) throw new InvalidDataException("Attachment child changed its repeated current Common controller.");
            var attachments = controller.ReadActorMovementAttachments(Source, child, CheckController);
            LastCall = LastCall with { Attachments = attachments, Changed = Next() }; Check();
            Step(FalloutPlayerActorMovementStep.BasePosition);
            var final = _owners.StoreBasePosition(frame, position); RequirePositionStore(final, frame, "base-reference-position");
            LastCall = LastCall with { BasePosition = final, FinalPosition = position, Changed = Next() }; Check();
            Step(FalloutPlayerActorMovementStep.FallFlags); var fall = ReadFlags(controller, child);
            if ((fall.Flags & FalloutPlayerActorMovementSource.ControllerFallFlag) != 0)
                throw new NotSupportedException("source-Actor-movement-current-fall-height-damage-child-unbound");
            Step(FalloutPlayerActorMovementStep.Returned); LastCall = LastCall with { Returned = true, Changed = Next() }; return LastCall;
        }
        catch (Exception error)
        {
            _failure ??= error.Message; LastCall = LastCall! with
            {
                FailedAt = LastCall!.Step,
                Step = FalloutPlayerActorMovementStep.Failed,
                Failure = _failure,
                Changed = Next()
            }; throw;
        }
        finally { _busy = false; }
        void CheckFrame(FalloutPlayerLateFrameInvocation current)
        { if (current != frame) throw new InvalidOperationException("Actor movement leaf changed its entered Player frame."); Check(); }
        void Check() { RequireInvocation(invocation); if (_fault != fault) throw new InvalidOperationException("Actor movement swallowed current-owner reentry."); }
        void Step(FalloutPlayerActorMovementStep step) { Check(); LastCall = LastCall! with { Step = step, Changed = Next() }; }
        void CheckController(FalloutControllerActorMovementInvocation current)
        {
            if (current.Parent != invocation) throw new InvalidOperationException("Controller child changed its actual entered Actor movement.");
            Check(); _owners.Common.RequireControllerActorMovement(current);
        }
        FalloutPlayerFollowingLifeRead Life(bool argument)
        {
            var current = _owners.Runtime.Life(frame.Common.Actor.Reference); var raw = current.Require();
            var read = new FalloutPlayerFollowingLifeRead(frame.Common, current, argument, FalloutPlayerFollowingContinuation.LifePredicate(raw, argument));
            LastCall = LastCall! with { Lives = [.. LastCall!.Lives, read], Changed = Next() }; Check(); return read;
        }
        FalloutReferencePositionRead ReadPosition()
        {
            var read = _owners.ReadPosition(frame); RequireReference(read.Source, read.Stack, read.Process, read.Factory, read.Constructor, read.Actor);
            LastCall = LastCall! with { Positions = [.. LastCall!.Positions, read], Changed = Next() }; Check(); return read;
        }
        FalloutControllerMovementFlagsRead ReadFlags(FalloutEarlyController controller, FalloutControllerActorMovementInvocation child)
        {
            var read = controller.ReadActorMovementFlags(Source, child, CheckController);
            LastCall = LastCall! with { ControllerFlags = [.. LastCall!.ControllerFlags, read], Changed = Next() }; Check(); return read;
        }
        FalloutIniValue Setting(string name, char kind)
        {
            if (!ReferenceEquals(_owners.Records.IniSettings, _owners.Settings))
                throw new InvalidOperationException("Actor movement lost its actual current numeric INI owner.");
            var value = _owners.Settings.Find(FalloutIniCollection.Main, name) ??
                throw new NotSupportedException("Actor movement has no current source INI declaration: " + name);
            if (value.Declaration.Kind != kind || value.Number is not { } number || !double.IsFinite(number) ||
                kind == 'i' && (number is < int.MinValue or > int.MaxValue || number != Math.Truncate(number)))
                throw new InvalidDataException("Actor movement changed its selected typed numeric INI field.");
            return value;
        }
        void RequireCommon(FalloutProcessSceneQuery current)
        {
            if (current.Actor != frame.Common.Actor || current.Process != frame.Common.Process || current.Epoch != frame.Common.Epoch ||
                current.GameplayOwnership != frame.Common.GameplayOwnership || current.Stack != Stack || current.Level != frame.Common.Level)
                throw new InvalidDataException("Actor movement changed its current Common Actor/epoch/field owner.");
        }
    }
    private void RequireReference(string source, string stack, Guid process, Guid factory, Guid constructor, FalloutCombatActorIdentity actor)
    {
        if (source != _owners.Position.Source.Identity || stack != Stack || process != Process || factory != _owners.Position.Factory ||
            constructor != Constructor || actor != _owners.Position.Actor)
            throw new InvalidDataException("Actor movement borrowed another current reference-field receipt.");
    }
    private void RequireTes(FalloutPlayerFrameTesCellRead cell)
    {
        if (cell.Source != Source.Cells.Identity || cell.Stack != Stack || cell.Process != _owners.Cells.Process ||
            cell.Factory != _owners.Cells.Factory || cell.Constructor != _owners.Cells.Constructor || cell.Sequence < 0)
            throw new InvalidDataException("Actor movement substituted another current TES/ParentCELL authority.");
    }
    private void RequirePositionStore(FalloutReferencePositionCall call, FalloutPlayerLateFrameInvocation frame, string kind)
    {
        var invocation = call.Invocation;
        if (!call.Returned || call.Kind != kind || call.Source != _owners.Position.Source.Identity || call.Factory != _owners.Position.Factory ||
            invocation.Actor != frame.Common.Actor || invocation.ReferenceProcess != Process || invocation.ReferenceConstructor != Constructor ||
            invocation.CommonProcess != frame.Common.Process || invocation.Epoch != frame.Common.Epoch || invocation.ThreadId != frame.ThreadId ||
            call.Change is not { Returned: true })
            throw new InvalidDataException("Actor movement lost its returned existing reference setter/mask2 child.");
    }
    private void RequireInvocation(FalloutPlayerActorMovementInvocation invocation)
    {
        _requireLifetime(); _requireActual(invocation); var frame = invocation.Frame; var data = invocation.Data;
        if (invocation.Call == Guid.Empty || frame.PlayerProcess != Process || frame.PlayerConstructor != Constructor ||
            frame.Common.Stack != Stack || !frame.Common.Actor.EnginePlayer || frame.ThreadId != Environment.CurrentManagedThreadId ||
            frame.Caller.Main == Guid.Empty || frame.Caller.Invocation == Guid.Empty || frame.Caller.Ordinal < 1 ||
            data.Data is null || data.Fields is null || data.Source != Source.Mover.Identity || data.Factory != _owners.Data.Factory ||
            data.Process != Process || data.Constructor != Constructor || data.Actor != frame.Common.Actor || data.Epoch != frame.Common.Epoch)
            throw new InvalidOperationException("Actor movement requires its actual entered nonnull reference-data/Player/current frame.");
        _owners.Data.RequireActorMovementData(data, frame, current =>
        {
            if (current != frame) throw new InvalidOperationException("Actor movement data borrow changed its actual frame.");
            _requireActual(invocation);
        });
    }
    private void RequireIdle()
    {
        ObjectDisposedException.ThrowIf(_retired, this);
        if (_busy) { ++_fault; throw new InvalidOperationException("Actor movement reentered its actual source body."); }
    }
    private long Next() => _sequence = checked(_sequence + 1);
    internal static bool ValidAngle(uint bits) => bits != FalloutPlayerActorMovementSource.ExceptionalAngleBits && float.IsFinite(Value(bits));
    internal static uint DistanceSquared(FalloutPackageRawPosition position, FalloutPhysicalVector3 other)
    {
        var x = F.Subtract(Value(position.X), other.XValue); var y = F.Subtract(Value(position.Y), other.YValue);
        var z = F.Subtract(Value(position.Z), other.ZValue);
        return BitConverter.SingleToUInt32Bits(F.Add(F.Add(F.Multiply(y, y), F.Multiply(x, x)), F.Multiply(z, z)));
    }
    private static float Value(uint bits) => BitConverter.UInt32BitsToSingle(bits);
}
