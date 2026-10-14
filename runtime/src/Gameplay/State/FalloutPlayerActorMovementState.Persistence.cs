using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;
using OpenNV.Runtime.World.Physics;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutPlayerActorMovementState
{
    internal FalloutPlayerActorMovementSnapshot Capture()
    {
        RequireIdle(); var saved = new FalloutPlayerActorMovementSnapshot(Source, Stack, Process, Factory, Constructor,
            _sequence, _calls, _publications.ToArray(), LastCall, _failure); Validate(saved); return saved;
    }
    internal void Retire() { if (_retired) return; RequireIdle(); _retired = true; }
    internal static void Validate(FalloutPlayerActorMovementSnapshot saved)
    {
        saved.Source.Validate();
        if (string.IsNullOrWhiteSpace(saved.Stack) || saved.PlayerProcess == Guid.Empty || saved.Factory == Guid.Empty ||
            saved.Constructor == Guid.Empty || saved.Sequence < 0 || saved.Calls < 0 || saved.Publications is not { Count: > 0 } ||
            (saved.Calls == 0) != (saved.LastCall is null))
            throw new InvalidDataException("Actor movement omitted current source owner or bounded frame history.");
        var factories = new Dictionary<Guid, FalloutPlayerActorMovementPublication>(); var processes = new HashSet<Guid>();
        var constructors = new HashSet<Guid>();
        for (var index = 0; index < saved.Publications.Count; ++index)
        {
            var row = saved.Publications[index]; row.Actor.Validate();
            if (!row.Actor.EnginePlayer || row.PlayerProcess == Guid.Empty || row.Factory == Guid.Empty || row.Constructor == Guid.Empty ||
                row.CommonProcess == Guid.Empty || row.Epoch < 1 || row.Published < 0 || row.Published > saved.Sequence ||
                !processes.Add(row.PlayerProcess) || !constructors.Add(row.Constructor) || !factories.TryAdd(row.Factory, row) ||
                (index == 0 ? row.Published != 0 || row.PreviousProcess is not null || row.PreviousFactory is not null :
                    row.PreviousProcess != saved.Publications[index - 1].PlayerProcess || row.PreviousFactory != saved.Publications[index - 1].Factory ||
                    row.Published < saved.Publications[index - 1].Published || row.Actor != saved.Publications[0].Actor || row.Epoch != saved.Publications[0].Epoch))
                throw new InvalidDataException("Actor movement reused or disconnected its actual cold owner lineage.");
        }
        var current = saved.Publications[^1];
        if (current.PlayerProcess != saved.PlayerProcess || current.Factory != saved.Factory || current.Constructor != saved.Constructor)
            throw new InvalidDataException("Actor movement current object is not its published fresh owner.");
        if (saved.LastCall is not { } call)
        {
            if (saved.Sequence != 0 || saved.Failure is not null) throw new InvalidDataException("Empty Actor movement invented a call or failure.");
            return;
        }
        ValidateCall(call, saved.Source.Mover, saved.Stack);
        if (!factories.TryGetValue(call.Factory, out var owner) || owner.PlayerProcess != call.Invocation.Frame.PlayerProcess ||
            owner.Constructor != call.Invocation.Frame.PlayerConstructor || owner.CommonProcess != call.Invocation.Frame.Common.Process ||
            owner.Actor != call.Invocation.Frame.Common.Actor || owner.Epoch != call.Invocation.Frame.Common.Epoch ||
            call.Ordinal != saved.Calls || call.Entered <= owner.Published || call.Changed != saved.Sequence || call.Failure != saved.Failure)
            throw new InvalidDataException("Actor movement latest receipt belongs to another actual constructor interval.");
    }
    internal static void ValidateCall(FalloutPlayerActorMovementCall call, FalloutPlayerMoverDeltaSource mover, string stack)
    {
        mover.Validate(); var invocation = call.Invocation; var frame = invocation.Frame; var data = invocation.Data;
        var referenceSource = FalloutReferencePositionSource.Reviewed(mover.Controller.Fields).Identity;
        if (invocation.Call == Guid.Empty || call.Factory == Guid.Empty || call.Ordinal < 1 || call.Entered < 1 || call.Changed <= call.Entered ||
            frame.PlayerProcess == Guid.Empty || frame.PlayerConstructor == Guid.Empty || frame.Prelude == Guid.Empty || frame.ThreadId < 1 ||
            frame.Caller.Main == Guid.Empty || frame.Caller.Invocation == Guid.Empty || frame.Caller.Ordinal < 1 ||
            frame.Common.Actor is not { EnginePlayer: true } || frame.Common.Process == Guid.Empty || frame.Common.Epoch < 1 ||
            frame.Common.GameplayOwnership == Guid.Empty || frame.Common.Stack != stack ||
            frame.Common.ExecutableSha256 != mover.Controller.Fields.Controls.Main.EngineSha256 ||
            data.Data is null || data.Data == Guid.Empty || data.Fields is null || data.Source != mover.Identity || data.Stack != stack ||
            data.Process != frame.PlayerProcess || data.Constructor != frame.PlayerConstructor || data.Factory == Guid.Empty ||
            data.Actor != frame.Common.Actor || data.Epoch != frame.Common.Epoch ||
            call.Positions is null || call.Positions.Count > 3 || call.Lives is null || call.Lives.Count > 2 ||
            call.ControllerFlags is null || call.ControllerFlags.Count > 3 || !Enum.IsDefined(call.Step) ||
            call.Returned != (call.Failure is null) || call.Returned != (call.Step == FalloutPlayerActorMovementStep.Returned) ||
            call.Failure is { Length: 0 } || call.Returned && call.FailedAt is not null || !call.Returned && call.FailedAt is null)
            throw new InvalidDataException("Actor movement omitted its actual nonnull-data frame prefix.");
        if (call.Context is { } context) RequireCommon(context.Process);
        if (call.FormKind is { } kind)
        {
            RequireReference(kind.Source, kind.Stack, kind.Process, kind.Factory, kind.Constructor, kind.Actor);
            if (kind.Kind != FalloutPlayerActorMovementSource.CharacterFormKind)
                throw new InvalidDataException("Actor movement changed its retained actual Character constructor byte.");
        }
        if (call.RawPosition is { } raw) RequireReference(raw.Source, raw.Stack, raw.Process, raw.Factory, raw.Constructor, raw.Actor);
        foreach (var position in call.Positions)
        {
            RequireReference(position.Source, position.Stack, position.Process, position.Factory, position.Constructor, position.Actor);
            if (position.Mode != 0) throw new InvalidDataException("Actor movement substituted raw XYZ for an adjusted position getter.");
        }
        if (call.Angles is { } angles && (angles.Source != FalloutPlayerWorldOrientationSource.Reviewed(mover.Controller.Fields.Controls).Identity ||
            angles.Stack != stack || angles.Process != frame.PlayerProcess || angles.Factory == Guid.Empty || angles.Sequence < 0 ||
            angles.Binding.Reference != frame.Common.Actor.Reference || angles.Binding.Base != frame.Common.Actor.Base || !angles.Binding.IsCharacter))
            throw new InvalidDataException("Actor movement raw angles came from another reference or rendered pose.");
        if (call.ResetAngles is { } reset && (call.Angles is null || reset.Factory != call.Angles.Factory || reset.Source != call.Angles.Source ||
            reset.Process != call.Angles.Process || reset.Binding != call.Angles.Binding || reset.Sequence <= call.Angles.Sequence ||
            reset.Angles != default || ValidAngle(call.Angles.Angles.X) && ValidAngle(call.Angles.Angles.Y) && ValidAngle(call.Angles.Angles.Z)))
            throw new InvalidDataException("Actor movement exceptional rotation reset changed its genuine preceding writer.");
        if (call.Disabled is { } disabled && (disabled.Source != mover.Controller.Fields.Controls.Identity || disabled.Stack != stack ||
            disabled.Process != frame.PlayerProcess || disabled.Factory == Guid.Empty || disabled.Sequence < 0))
            throw new InvalidDataException("Actor movement disabled bit came from another mutable Player word.");
        if (call.Status is { } status) RequireReference(status.Source, status.Stack, status.Process, status.Factory, status.Constructor, status.Actor);
        if (call.Camera is { } camera && (camera.Source != FalloutPlayerWorldOrientationSource.Reviewed(mover.Controller.Fields.Controls).Identity ||
            camera.Stack != stack || camera.World == Guid.Empty || camera.RootFactory == Guid.Empty || camera.Sequence < 0 || camera.Updates < 1))
            throw new InvalidDataException("Actor movement camera read is not the actual published current World camera.");
        foreach (var life in call.Lives)
        {
            RequireCommon(life.Process);
            if (life.Raw.Value is not { } currentLife || life.Raw.Failure is not null || life.Result != FalloutPlayerFollowingContinuation.LifePredicate(currentLife, life.Argument))
                throw new InvalidDataException("Actor movement changed its actual life predicate argument or current field.");
        }
        if (call.LifeOne is { } one && (one.Raw.Value is not { } value || one.Raw.Failure is not null || one.Value != (value == 1)))
            throw new InvalidDataException("Actor movement replaced raw-Life-equals-one with another Actor predicate.");
        if (call.Activity is { } activity) RequireCommon(activity.Process);
        foreach (var flags in call.ControllerFlags)
        {
            RequireController(flags.Invocation, flags.Controller);
            if (flags.Sequence < 0) throw new InvalidDataException("Actor movement controller flags lack their current field sequence.");
        }
        if (call.DisabledFlag is { } flag)
        {
            RequireController(flag.Invocation, flag.Controller);
            if (!flag.Returned || flag.Failure is not null || call.Disabled is null || flag.Disabled != ((call.Disabled.Word & 1) != 0) ||
                flag.After != (flag.Disabled ? flag.Before | FalloutPlayerActorMovementSource.ControllerDisabledMovementFlag :
                    flag.Before & ~FalloutPlayerActorMovementSource.ControllerDisabledMovementFlag))
                throw new InvalidDataException("Actor movement omitted or changed its actual disabled bit store.");
        }
        if (call.Controlled is { } controlled)
        {
            RequireController(controlled.Invocation, controlled.Controller);
            if (!controlled.Returned || !controlled.PhaseRead || controlled.Phase != 0 || controlled.State == 2 || controlled.Failure is not null)
                throw new InvalidDataException("Actor movement controlled child fabricated a phase or skipped a real predicate.");
        }
        if (call.DeltaChild is { } delta)
        {
            RequireCommon(delta.Invocation.Context.Process);
            if (!delta.Returned || delta.Invocation.Frame != frame || delta.Invocation.Movement != invocation.Delta ||
                delta.Invocation.Angles != (call.ResetAngles ?? call.Angles)?.Angles || delta.Invocation.Flags != call.FreshMoverWord ||
                delta.Controller != call.Context?.Context || delta.Failure is not null || !delta.WorldRead || delta.World is not null || delta.Speed is null)
                throw new InvalidDataException("Actor movement ordinary child is not its real returned controller/cache delta.");
        }
        if (call.ControllerPosition is { } proxy)
        {
            RequireController(proxy.Invocation, proxy.Controller);
            var rotated = FalloutCharacterControllerTransformState.RotateOffset(proxy.Offset, proxy.Proxy.Rotation);
            var scale = BitConverter.UInt32BitsToSingle(FalloutPhysicalConstructorInputs.PhysicalToSourceBits);
            var actual = FalloutPhysicalVector3.FromValues(
                FalloutPhysicalFloat32.Multiply(FalloutPhysicalFloat32.Subtract(proxy.Proxy.Translation.XValue, rotated.XValue), scale),
                FalloutPhysicalFloat32.Multiply(FalloutPhysicalFloat32.Subtract(proxy.Proxy.Translation.YValue, rotated.YValue), scale),
                FalloutPhysicalFloat32.Multiply(FalloutPhysicalFloat32.Subtract(proxy.Proxy.Translation.ZValue, rotated.ZValue), scale));
            if (proxy.SourcePosition != actual || proxy.Sequence < 0)
                throw new InvalidDataException("Actor movement controller XYZ differs from its retained real proxy and offset.");
        }
        if (call.TesCell is { } cell && (cell.Source != FalloutInitialPlacementCellsSource.Reviewed(
            FalloutReferencePositionSource.Reviewed(mover.Controller.Fields)).Identity || cell.Stack != stack ||
            cell.Process == Guid.Empty || cell.Factory == Guid.Empty || cell.Constructor == Guid.Empty || cell.Sequence < 0))
            throw new InvalidDataException("Actor movement TES current CELL came from another field owner.");
        if (call.Attachments is { } attachments)
        {
            RequireController(attachments.Invocation, attachments.Controller);
            if (!attachments.Returned || attachments.Head is not null || attachments.Tail is not null)
                throw new InvalidDataException("Actor movement invented an empty reached controller attachment list.");
        }
        if (call.ActorPosition is { } corrected) RequirePosition(corrected, "Actor-reference-position");
        if (call.BasePosition is { } final) RequirePosition(final, "base-reference-position");
        if (!call.Returned) return;
        var blocked = (call.Disabled?.Word & 1) != 0;
        var expectedLives = call.RawLife?.Value == 3 ? 1 : 2;
        if (call.Context?.Context is null || call.FormKind is null || call.RawPosition is null || call.Angles is null ||
            call.SkipFrames is not { Number: 0 } || call.Disabled is null || call.Status is not { Mode: 0 } ||
            call.Camera is null || call.Positions.Count < 2 || call.RawLife is not { Value: not null, Failure: null } ||
            call.Lives.Count != expectedLives || call.Lives[^1].Argument != true || call.Lives[^1].Result != false ||
            expectedLives == 2 && (call.Lives[0].Argument || call.Lives[0].Result == false && call.LifeOne is null ||
                call.LifeOne?.Value == false && call.Activity is null) ||
            call.WarpDistance is null || call.CameraDistanceSquared != DistanceSquared(call.Positions[0].Position, call.Camera.Translation) ||
            call.DisabledFlag is null || call.FreshMoverWord is null || (call.FreshMoverWord.Value & FalloutPlayerMoverDeltaSource.MotionMask) != 0 ||
            (blocked ? call.Controlled is null || call.DeltaChild is not null : call.DeltaChild is null || call.Controlled is not null) ||
            call.ControllerFlags.Count != 3 || call.ControllerPosition is null || call.TesCell?.Cell is null ||
            call.Attachments is null || call.BasePosition is null || call.FinalPosition is null ||
            call.BasePosition.After.Position != call.FinalPosition || call.ControllerFlags[^1].Flags != call.ControllerFlags[1].Flags ||
            (call.ControllerFlags[^1].Flags & FalloutPlayerActorMovementSource.ControllerFallFlag) != 0)
            throw new InvalidDataException("Returned Actor movement omitted an actual reached camera/field/controller/TES/position child.");
        if (call.RawLife!.Value != 3 && call.Lives[0].Result == false && call.LifeOne?.Value == true && call.Activity is not null)
            throw new InvalidDataException("Actor movement entered activity after the source life-one short circuit.");
        var result = call.ActorPosition is null ? new FalloutPackageRawPosition(call.ControllerPosition.SourcePosition.X,
            call.ControllerPosition.SourcePosition.Y, call.ControllerPosition.SourcePosition.Z) : call.Positions[^1].Position;
        if (call.FinalPosition != result) throw new InvalidDataException("Actor movement final setter substituted a pending or rendered position.");
        void RequireCommon(FalloutProcessSceneQuery query)
        {
            if (query.Process != frame.Common.Process || query.Actor != frame.Common.Actor || query.Epoch != frame.Common.Epoch ||
                query.GameplayOwnership != frame.Common.GameplayOwnership || query.Stack != stack || query.Level != frame.Common.Level)
                throw new InvalidDataException("Actor movement child changed its same Common process/Actor/epoch.");
        }
        void RequireReference(string source, string currentStack, Guid process, Guid factory, Guid constructor, FalloutCombatActorIdentity actor)
        {
            if (source != referenceSource || currentStack != stack || process != frame.PlayerProcess || factory == Guid.Empty ||
                constructor != frame.PlayerConstructor || actor != frame.Common.Actor ||
                call.RawPosition is { } baseline && factory != baseline.Factory)
                throw new InvalidDataException("Actor movement child changed its same reference field factory.");
        }
        void RequireController(FalloutControllerActorMovementInvocation child, Guid controller)
        {
            if (child.Parent != invocation || child.Call == Guid.Empty || controller == Guid.Empty || controller != call.Context?.Context ||
                child.Context.Context != controller || child.RuntimeProcess == Guid.Empty)
                throw new InvalidDataException("Actor movement controller child changed its entered parent or actual pointer.");
            RequireCommon(child.Context.Process);
        }
        void RequirePosition(FalloutReferencePositionCall position, string kind)
        {
            if (!position.Returned || position.Kind != kind || position.Source != referenceSource || position.Factory != call.RawPosition?.Factory ||
                position.Invocation.Actor != frame.Common.Actor || position.Invocation.ReferenceProcess != frame.PlayerProcess ||
                position.Invocation.ReferenceConstructor != frame.PlayerConstructor || position.Invocation.CommonProcess != frame.Common.Process ||
                position.Invocation.Epoch != frame.Common.Epoch || position.Invocation.ThreadId != frame.ThreadId || position.Change is not { Returned: true })
                throw new InvalidDataException("Actor movement received no actual returned same reference setter/mask2 child.");
        }
    }
}
