using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;
using OpenNV.Runtime.World.Physics;

public partial class NativeEarlyControllerAudit
{
    // Reached controlled child on genuine native resources. This audit does
    // not replace the separate real nonnull-data Actor-body integration run.
    private static void ExerciseControlledActorMovement(FalloutEarlyControllerSource early, FalloutPhysicalConstructorInputs inputs)
    {
        using var records = new MoverDeltaNativeRecords(early.Fields.Controls.Main);
        using var fixture = new NativeFixture(early, inputs, records.Actor, records.Stack);
        using var supplied = fixture.Construct(); var controller = supplied.Controller;
        fixture.Common.PublishEarlyController(fixture.Invocation, supplied, fixture.RequireConstruction);
        controller.SetHelperFlags(fixture.Invocation, fixture.Factory.Shared.ReadHelperFlag());
        var mover = FalloutPlayerMoverDeltaSource.Reviewed(early);
        var cells = FalloutInitialPlacementCellsSource.Reviewed(FalloutReferencePositionSource.Reviewed(early.Fields));
        var orientation = FalloutPlayerWorldOrientationSource.Reviewed(early.Fields.Controls);
        var source = FalloutPlayerActorMovementSource.Reviewed(mover, cells, orientation);
        var before = controller.Capture(); FalloutControllerActorMovementInvocation? entered = null;
        for (var ordinal = 1; ordinal <= 12; ++ordinal)
        {
            var common = fixture.Common.ReadSceneQueries(records.Actor.Reference, 1);
            var frame = new FalloutPlayerLateFrameInvocation(new(Guid.NewGuid(), ordinal, Guid.NewGuid()), fixture.Scene.Process,
                fixture.Scene.Constructor, Guid.NewGuid(), common, System.Environment.CurrentManagedThreadId, 0x3c888889);
            // Authored typed parent: no model/data runtime admission is claimed
            // by this independent controller child fixture.
            var data = new FalloutReferenceModelDataRead(mover.Identity, records.Stack, fixture.Scene.Process, Guid.NewGuid(),
                fixture.Scene.Constructor, records.Actor, 1, 1, Guid.NewGuid(), new(0, 0, 0xff7fffff, 0xbf800000, 0, null, null));
            var parent = new FalloutPlayerActorMovementInvocation(Guid.NewGuid(), frame, FalloutActorMoverVector.Default, 0, data);
            entered = new(Guid.NewGuid(), parent, fixture.Common.ReadSceneContext(records.Actor.Reference, 1),
                fixture.Runtime.CharacterControllerSourceProcess);
            var flag = controller.StoreActorMovementDisabledFlag(source, entered, true, RequireChild);
            var returned = controller.UpdateControlledActorMovement(source, entered, RequireChild);
            var position = controller.ReadActorMovementReferencePosition(source, entered, RequireChild);
            var attachments = controller.ReadActorMovementAttachments(source, entered, RequireChild); entered = null;
            var current = controller.Capture();
            Require(flag.After == (flag.Before | FalloutPlayerActorMovementSource.ControllerDisabledMovementFlag) &&
                returned is { Returned: true, State: 0, PhaseRead: true, Phase: 0 } &&
                attachments is { Returned: true, Head: null, Tail: null } &&
                current.ActorMovement is { FlagCalls: > 0, ControlledCalls: > 0 } state && state.FlagCalls == ordinal &&
                state.ControlledCalls == ordinal && state.Sequence == ordinal * 3 &&
                current.MoverDelta?.Calls == 0 && current.Proxy.Velocity == before.Proxy.Velocity &&
                current.Phantom.Transform == before.Phantom.Transform && current.Phantom.NativeId == before.Phantom.NativeId &&
                current.State == before.State && current.Contact == before.Contact &&
                !PhysicsServer3D.AreaGetSpace(controller.Phantom.NativeResource).IsValid &&
                JsonSerializer.Serialize(current.Stores) == JsonSerializer.Serialize(before.Stores) &&
                position.Proxy == before.Phantom.Transform,
                "Controlled phase-zero movement fabricated delta/cache/solver work or changed genuine native/controller fields.");
            FalloutEarlyController.ValidateActorMovement(current.ActorMovement!);
        }
        var saved = controller.Capture().ActorMovement!;
        Reject(() => FalloutEarlyController.ValidateActorMovement(saved with { Controlled = saved.Controlled! with { PhaseRead = false } }));
        Reject(() => FalloutEarlyController.ValidateActorMovement(saved with { Controlled = saved.Controlled! with { State = 2 } }));
        Reject(() => FalloutEarlyController.ValidateActorMovement(saved with { ControlledPhase = 2 }));
        controller.ObserveUnownedControlledMovementPhaseWriter("authored-unknown-controlled-phase-writer");
        Require(controller.SaveBlocker == "authored-unknown-controlled-phase-writer", "Unowned current controlled phase did not retain its independent barrier.");
        GD.Print("OPENNV_NATIVE_CONTROLLED_MOVEMENT_PASS actualArea=true sameController=true realFlag27Store=true " +
            "currentStatePhaseRead=true solverSkippedBySource=true speedCacheUnqueried=true repeatedFrames=12 boundedHistory=true " +
            "fullActorBody=UNEXECUTED gameplay=UNEXECUTED recording=off");
        void RequireChild(FalloutControllerActorMovementInvocation current)
        {
            if (entered != current) throw new InvalidOperationException("Controlled fixture changed its actual entered same-controller child.");
            fixture.Common.RequireControllerActorMovement(current); fixture.Factory.RequireLifetime();
        }
    }
}
