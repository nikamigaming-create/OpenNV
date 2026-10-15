using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutPlayerMoverDeltaState
{
    internal static void Validate(FalloutPlayerMoverDeltaSnapshot saved)
    {
        ArgumentNullException.ThrowIfNull(saved); saved.Source.Validate();
        if (saved.PlayerProcess == Guid.Empty || saved.Factory == Guid.Empty || saved.Constructor == Guid.Empty ||
            string.IsNullOrWhiteSpace(saved.Stack) || saved.Sequence < 0 || saved.Calls < 0 ||
            (saved.Calls == 0) != (saved.LastCall is null) || saved.Publications is not { Count: > 0 })
            throw new InvalidDataException("Mover delta omitted actual process/factory/caller or bounded history.");
        var publications = new Dictionary<Guid, FalloutPlayerMoverDeltaPublication>(); var processes = new HashSet<Guid>();
        for (var index = 0; index < saved.Publications.Count; ++index)
        {
            var row = saved.Publications[index]; row.Actor.Validate();
            if (row.PlayerProcess == Guid.Empty || row.Factory == Guid.Empty || row.Constructor == Guid.Empty ||
                row.CommonProcess == Guid.Empty || row.Epoch < 1 || row.Published < 0 || row.Published > saved.Sequence ||
                !processes.Add(row.PlayerProcess) || !publications.TryAdd(row.Factory, row) ||
                (index == 0 ? row.Published != 0 || row.PreviousProcess is not null || row.PreviousFactory is not null :
                    row.PreviousProcess != saved.Publications[index - 1].PlayerProcess || row.PreviousFactory != saved.Publications[index - 1].Factory ||
                    row.Published < saved.Publications[index - 1].Published || row.Actor != saved.Publications[0].Actor || row.Epoch != saved.Publications[0].Epoch))
                throw new InvalidDataException("Mover delta has disconnected or reused actual cold owners.");
        }
        var current = saved.Publications[^1];
        if (current.PlayerProcess != saved.PlayerProcess || current.Factory != saved.Factory || current.Constructor != saved.Constructor)
            throw new InvalidDataException("Mover delta current object differs from its actual published lifetime.");
        if (saved.LastCall is not { } call)
        { if (saved.Sequence != 0 || saved.Failure is not null) throw new InvalidDataException("Empty mover delta fabricated a call or failure."); return; }
        ValidateInvocation(call.Invocation, saved.Source, saved.Stack);
        if (!publications.TryGetValue(call.Factory, out var owner) || owner.PlayerProcess != call.Invocation.PlayerProcess ||
            owner.Constructor != call.Invocation.PlayerConstructor || owner.CommonProcess != call.Invocation.Common.Process ||
            owner.Actor != call.Invocation.Common.Actor || owner.Epoch != call.Invocation.Common.Epoch ||
            call.Ordinal != saved.Calls || call.Entered < 1 || call.Entered <= owner.Published || call.Changed <= call.Entered ||
            call.Changed != saved.Sequence || call.ProcessReads is null || call.ProcessReads.Count > 4 ||
            call.ProcessBytes is null || call.ProcessBytes.Count > 2 || call.Flags is null || call.Flags.Count > 3 ||
            call.Controllers is null || call.Controllers.Count > 2 ||
            call.Returned != (call.Failure is null) || call.Returned != (call.Step == FalloutPlayerMoverDeltaStep.Returned) ||
            call.Failure != saved.Failure || (call.Returned ? call.FailedAt is not null : call.FailedAt is null) ||
            call.Failure is { } error && string.IsNullOrWhiteSpace(error))
            throw new InvalidDataException("Mover delta lost its actual ordered frame prefix/current owner or returned child.");
        RequireDefault(call.DefaultVector, call.Invocation, call.DefaultVector.Factory);
        if (call.Mover.VectorX != 0 || call.Mover.VectorY != 0 || call.Mover.VectorZ != 0)
            throw new InvalidDataException("Mover delta bypassed its same returned positive-zero XY/Z predecessor.");
        foreach (var process in call.ProcessReads) RequireCommon(process, call.Invocation);
        if (call.Activities is null || call.Activities.Count > 2 ||
            call.Activities.Count == 2 && call.Activities[0].Value == 0)
            throw new InvalidDataException("Mover delta changed its selected activity predicate/requery order.");
        foreach (var activity in call.Activities) RequireCommon(activity.Process, call.Invocation);
        foreach (var read in call.ProcessBytes)
        { RequireCommon(read.Common, call.Invocation); if (!Enum.IsDefined(read.Cell)) throw new InvalidDataException("Mover delta read another process byte."); }
        if (call.View is { } view) RequireView(view, call.Invocation, saved.Source);
        if (call.FirstAnimation is { } first && (call.View?.View != 0 ||
            first.Actor != call.Invocation.Common.Actor || first.Process != call.Invocation.Common.Process || first.Epoch != call.Invocation.Common.Epoch ||
            first.Source != saved.Source.Controller.Fields.Identity || first.Constructor != call.Invocation.PlayerConstructor || first.Factory == Guid.Empty || first.Stack != saved.Stack))
            throw new InvalidDataException("Mover delta first animation belongs to another actual field or omitted its nonnull child.");
        ValidateLoadedAnimationReads(call);
        foreach (var controller in call.Controllers)
        {
            RequireCommon(controller.Context.Process, call.Invocation);
            if (controller.Controller != controller.Context.Context || controller.Controller == Guid.Empty ||
                (controller.Controller is null ? controller.Factory is not null || controller.RuntimeProcess is not null || controller.Epoch is not null || controller.ContactFlags is not null || controller.Contacts is not null :
                    controller.Factory is null || controller.Factory == Guid.Empty || controller.RuntimeProcess is null ||
                    controller.RuntimeProcess == Guid.Empty || controller.Epoch != call.Invocation.Common.Epoch))
                throw new InvalidDataException("Mover delta lost the actual current controller/resource field association.");
        }
        foreach (var flags in call.Flags)
            if (flags.Actor != call.Invocation.Common.Actor || flags.Process != call.Invocation.Common.Process ||
                flags.Constructor != call.Invocation.PlayerConstructor || flags.Factory == Guid.Empty || flags.FieldsFactory == Guid.Empty ||
                flags.Epoch != call.Invocation.Common.Epoch || flags.Stack != saved.Stack)
                throw new InvalidDataException("Mover delta flags came from winning metadata or another actual field owner.");
        if (call.Data is { } data && (data.Actor != call.Invocation.Common.Actor || data.Process != call.Invocation.PlayerProcess ||
            data.Constructor != call.Invocation.PlayerConstructor || data.Epoch != call.Invocation.Common.Epoch || data.Factory == Guid.Empty ||
            data.Source != saved.Source.Identity || data.Stack != saved.Stack || (data.Data is null) != (data.Fields is null) || data.Data == Guid.Empty))
            throw new InvalidDataException("Mover delta reference data changed its true nullable data object or current source owner.");
        if (call.Menu is { } menu && (menu.Source != FalloutStandaloneInterfaceSource.Read(saved.Source.Controller.Fields.Controls.Main).Identity ||
            menu.Stack != saved.Stack || menu.Process == Guid.Empty || menu.Constructor == Guid.Empty || menu.Sequence < 0 ||
            menu.Value != (menu.Manager is not null && menu.Enabled != 0 && menu.Dialog != 0)))
            throw new InvalidDataException("Mover delta dialog predicate changed its existing source Interface fields.");
        if (call.ActorMovement is { } actor)
        {
            FalloutPlayerActorMovementState.ValidateCall(actor, saved.Source, saved.Stack);
            if (actor.Invocation.Frame != call.Invocation || actor.Invocation.Data != call.Data || actor.Invocation.Delta != call.Delta ||
                actor.Invocation.MoverWord != call.Mover.Flags || call.Data?.Data is null)
                throw new InvalidDataException("Mover delta borrowed another actual nonnull-data Actor movement child.");
        }
        if (call.Parent is { } parent) RequireParent(parent, call.Invocation);
        if (!call.Returned) return;
        if (call.ProcessReads.Count < 1 || call.View is null || call.View.View == 0 && call.FirstAnimation is null ||
            call.Activities.Count == 0 || call.Activities[0].Value != 0 && call.Activities.Count != 2 ||
            call.Override != false ||
            call.Life is not { Result: false } || !call.ProcessBytes.Any(read => read.Cell == FalloutMoverDeltaProcessCell.SpecialMotion && read.Value == 0) ||
            call.Controllers.Count < 1 || call.Flags.Count == 0)
            throw new InvalidDataException("Returned mover delta omitted an actually reached animation/override/controller/flag child.");
        if ((call.Flags[^1].Flags & FalloutPlayerMoverDeltaSource.DirectPositionFlag) != 0)
        {
            if (call.Position is not { Returned: true, Failure: null } position || call.Menu is not null || call.Data is not null ||
                call.Parent is not null || position.Invocation.Actor != call.Invocation.Common.Actor || position.Invocation.ReferenceProcess != call.Invocation.PlayerProcess ||
                position.Invocation.CommonProcess != call.Invocation.Common.Process || position.Invocation.Epoch != call.Invocation.Common.Epoch)
                throw new InvalidDataException("Direct mover delta omitted its real returned reference/controller position mutation.");
        }
        else if (call.Menu is null || call.MovementReturned != true || call.Parent is null || call.Position is not null ||
            (call.Menu.Value && (call.ProcessBytes.LastOrDefault(read => read.Cell == FalloutMoverDeltaProcessCell.DialogMotion)?.Value ?? 0) != 0 && (call.Mover.Flags & FalloutPlayerMoverDeltaSource.MotionMask) == 0 ?
                call.Data is not null || call.Controllers.Count != 1 : (call.Data is null || call.Data.Data is not null && call.ActorMovement is not { Returned: true } ||
                    call.Data.Data is null && call.ActorMovement is not null) || call.Controllers.Count != 2))
            throw new InvalidDataException("Ordinary mover delta fabricated movement return from zero XYZ or null model in nonnull data.");
    }
    private static void ValidateLoadedAnimationReads(FalloutPlayerMoverDeltaCall call)
    {
        var common = call.ProcessReads.FirstOrDefault()?.AnimationContext; var first = call.FirstAnimation?.Context;
        var expected = common; var groups = call.AnimationGroups ?? [];
        if (call.View?.View == 0)
        {
            if (common is not null && first is not null)
            {
                if (!call.Returned && call.FailedAt <= FalloutPlayerMoverDeltaStep.AnimationEquality && groups.Count < 2)
                {
                    if (groups.Count > 1 || groups.Any(row => row.Frame != call.Invocation || row.Context != first || row.Factory == Guid.Empty || row.Sequence < 0) ||
                        call.SelectedAnimation != common || call.AnimationMovement is not null || call.Delta != FalloutActorMoverVector.Default)
                        throw new InvalidDataException("Failed mover delta fabricated its incomplete group comparison.");
                    return;
                }
                if (groups is not [var firstRead, var commonRead] || firstRead.Context != first || commonRead.Context != common ||
                    groups.Any(row => row.Frame != call.Invocation || row.Factory == Guid.Empty || row.Sequence < 0))
                    throw new InvalidDataException("Mover delta omitted the current first/common group-word equality reads.");
                if (firstRead.Word == commonRead.Word) expected = first;
            }
            else if (common is null) expected = first;
        }
        if ((common is null || first is null || call.View?.View != 0) && groups.Count != 0)
            throw new InvalidDataException("Mover delta invented unentered group comparisons.");
        if (call.SelectedAnimation != expected)
            throw new InvalidDataException("Mover delta selected a different real animation pointer from its current group words.");
        if (call.AnimationMovement is not { } read)
        {
            if (call.Delta != FalloutActorMoverVector.Default || call.Returned && expected is not null)
                throw new InvalidDataException("Mover delta returned a nonnull animation without its vector child.");
            return;
        }
        if (read.Frame != call.Invocation || read.Context != expected || read.Factory == Guid.Empty || read.Sequence < 0 ||
            read.Delta != call.Delta || call.Flags.FirstOrDefault() != read.Flags ||
            read.Returned && (read.ModelFactory is null || read.ModelFactory == Guid.Empty || read.Mode is null ||
                read.MoverFlags != call.Mover.Flags || read.Rotation.Count != 9) ||
            !read.Returned && (read.ModelFactory is not null || read.Mode is not null || read.Scale is not null ||
                read.Rotation.Count != 0 || read.Delta != FalloutActorMoverVector.Default))
            throw new InvalidDataException("Animation vector changed its actual model, mutable leaves, caller or returned result.");
        if (!read.Returned) return;
        var mode = read.Mode!;
        if (mode.Actor != call.Invocation.Common.Actor || mode.Process != call.Invocation.PlayerProcess ||
            mode.Constructor != call.Invocation.PlayerConstructor || (mode.Mode is 3 or 5 or 8 or 10) != (read.Scale is not null))
            throw new InvalidDataException("Animation vector replaced the same source Actor19C getter or its conditional scale.");
        var x = Value(read.Before.X); var y = Value(read.Before.Y); var z = Value(read.Before.Z);
        if (read.Scale is { } scale)
        {
            if (scale.Actor != mode.Actor || scale.Process != mode.Process || scale.Constructor != mode.Constructor)
                throw new InvalidDataException("Animation vector borrowed another current reference scale.");
            x /= Value(scale.Bits); y /= Value(scale.Bits);
        }
        if ((read.Flags.Flags & FalloutPlayerMoverDeltaSource.DirectPositionFlag) == 0) z = 0;
        else
        {
            var matrix = read.Rotation.Select(Value).ToArray();
            float Row(int row) => ((matrix[row] * x) + 0f) + (matrix[row + 1] * y) + (matrix[row + 2] * z);
            var nextX = Row(0); var nextY = Row(3); var nextZ = Row(6); x = nextX; y = nextY; z = nextZ;
        }
        if (read.Delta != new FalloutActorMoverVector(Bits(x), Bits(y), Bits(z)))
            throw new InvalidDataException("Animation vector lost its actual source Float32 scale/rotation/zero-Z law.");
    }
    private static void ValidateInvocation(FalloutPlayerLateFrameInvocation invocation, FalloutPlayerMoverDeltaSource source, string stack)
    {
        if (invocation is null || invocation.Caller.Main == Guid.Empty || invocation.Caller.Invocation == Guid.Empty || invocation.Caller.Ordinal < 1 ||
            invocation.PlayerProcess == Guid.Empty || invocation.PlayerConstructor == Guid.Empty || invocation.Prelude == Guid.Empty || invocation.ThreadId < 1 ||
            invocation.Common.Actor is not { EnginePlayer: true } || invocation.Common.Process == Guid.Empty || invocation.Common.Epoch < 1 ||
            invocation.Common.Level != FalloutDetectionProcessLevel.High || invocation.Common.GameplayOwnership == Guid.Empty ||
            invocation.Common.ExecutableSha256 != source.Controller.Fields.Controls.Main.EngineSha256 || invocation.Common.Stack != stack)
            throw new InvalidDataException("Mover delta omitted its actual source Main/Prelude/Player/Common/epoch/thread scope.");
    }
    private static void RequireCommon(FalloutProcessSceneQuery read, FalloutPlayerLateFrameInvocation invocation)
    {
        var actual = invocation.Common;
        if (read.Actor != actual.Actor || read.Process != actual.Process || read.Epoch != actual.Epoch || read.Level != actual.Level ||
            read.GameplayOwnership != actual.GameplayOwnership || read.Stack != actual.Stack || read.ExecutableSha256 != actual.ExecutableSha256 ||
            read.Changed < actual.Changed) throw new InvalidDataException("Mover delta child left its actual current Common source/epoch/lease.");
    }
    private static void RequireDefault(FalloutActorMoverFrameStoreOperation vector, FalloutPlayerLateFrameInvocation invocation, Guid moverFactory)
    {
        var frame = vector.Invocation;
        if (!vector.Returned || vector.Failure is not null || vector.Factory != moverFactory || vector.Factory == Guid.Empty ||
            vector.Kind != FalloutActorMoverFrameStoreKind.DefaultVector || vector.Stores is not { Count: 2 } ||
            vector.Stores[0] is not { Field: FalloutActorMoverField.PlayerVectorXY, Bits: 0, SecondBits: 0 } ||
            vector.Stores[1] is not { Field: FalloutActorMoverField.PlayerVectorZ, Bits: 0, SecondBits: null } ||
            frame.Caller != invocation.Caller || frame.PlayerProcess != invocation.PlayerProcess || frame.ThreadId != invocation.ThreadId)
            throw new InvalidOperationException("Mover delta lost its same actual returned mover/vector/Main predecessor.");
        RequireCommon(invocation.Common, invocation with { Common = frame.Common });
    }
    private static void RequireView(FalloutPlayerMoverDeltaViewRead read, FalloutPlayerLateFrameInvocation invocation, FalloutPlayerMoverDeltaSource source)
    {
        if (read.Source != FalloutStandalonePlayerSceneSource.Read(source.Controller.Fields.Controls.Main).Identity ||
            read.Stack != invocation.Common.Stack || read.Process != invocation.PlayerProcess || read.Constructor != invocation.PlayerConstructor ||
            read.Caller != invocation.Caller || read.Sequence < 0)
            throw new InvalidDataException("Mover delta view read changed its actual current Scene field.");
    }
    private static void RequireParent(FalloutReferenceParentCellRead parent, FalloutPlayerLateFrameInvocation invocation)
    {
        if (parent.Actor != invocation.Common.Actor || parent.Process == Guid.Empty || parent.Factory == Guid.Empty ||
            parent.Constructor != invocation.PlayerConstructor || parent.Stack != invocation.Common.Stack || parent.Sequence < 0 ||
            parent.ExecutableSha256 != invocation.Common.ExecutableSha256)
            throw new InvalidDataException("Mover delta changed the actual independent World ParentCELL field owner.");
    }
    private static void RequirePosition(FalloutReferencePositionInvocation position, FalloutPlayerLateFrameInvocation invocation)
    {
        if (position.Call == Guid.Empty || position.Actor != invocation.Common.Actor || position.ReferenceProcess != invocation.PlayerProcess ||
            position.ReferenceConstructor != invocation.PlayerConstructor || position.CommonProcess != invocation.Common.Process ||
            position.Epoch != invocation.Common.Epoch || position.ThreadId != invocation.ThreadId || position.PackageCommand is not null)
            throw new InvalidDataException("Mover delta position writer has another actual source frame/reference scope.");
    }
}
