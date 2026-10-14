using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;
using OpenNV.Runtime.World.Physics;

internal static partial class SourceMainScriptCallerContracts
{
    internal static void RunPlayerActorMovementContracts()
    {
        using (var fixture = new ReferencePositionFixture())
        {
            Reject(() => fixture.Call(FalloutReferencePositionIngress.ActorPosition, fixture.Position.ReadActorMovementFormKind));
            fixture.Call(FalloutReferencePositionIngress.ConstructorStartup,
                invocation => fixture.Position.StoreConstructorStartupPosition(invocation, fixture));
            fixture.Initialize();
            fixture.Call(FalloutReferencePositionIngress.ActorPosition,
                invocation => fixture.Position.InitializeConstructedPlayerFields(invocation, AnimationSettings("", "")));
            var kind = fixture.Call(FalloutReferencePositionIngress.ActorPosition, fixture.Position.ReadActorMovementFormKind);
            var raw = fixture.Call(FalloutReferencePositionIngress.ActorPosition, fixture.Position.ReadActorMovementRawPosition);
            Require(kind.Kind == FalloutPlayerActorMovementSource.CharacterFormKind && kind.Factory == raw.Factory &&
                raw.Position == new FalloutPackageRawPosition(0x45000000, 0x45000000, 0),
                "Actor movement did not reuse its actual constructor-kind/raw-XYZ field owner.");
            var saved = fixture.Capture();
            for (var cycle = 0; cycle < 3; ++cycle)
            {
                using var cold = new ReferencePositionFixture(saved);
                var coldKind = cold.Call(FalloutReferencePositionIngress.ActorPosition, cold.Position.ReadActorMovementFormKind);
                Require(coldKind.Kind == kind.Kind && coldKind.Factory != kind.Factory && cold.Events.Count == 0,
                    "Cold Character-kind borrow replayed source constructor or acquired a metadata/native type.");
                saved = cold.Capture();
            }
            fixture.Call(FalloutReferencePositionIngress.CopyOrLoad,
                invocation => fixture.Position.ObserveUnownedWriter(invocation, FalloutReferencePositionLane.ReferenceKind, "authored-kind-copy-writer"));
            Reject(() => fixture.Call(FalloutReferencePositionIngress.ActorPosition, fixture.Position.ReadActorMovementFormKind));
            Require(fixture.Position.Capture().Values.ReferenceKind == kind.Kind,
                "Unowned kind writer erased the retained constructor field instead of invalidating its current admission.");
        }
        using (var fixture = new ReferencePositionFixture())
        {
            var cell = fixture.Call(FalloutReferencePositionIngress.ActorPosition, fixture.Cells.ReadActorMovementCurrentCell);
            Require(cell.Cell is null, "TES current CELL getter invented ParentCELL or grid membership.");
            fixture.Call(FalloutReferencePositionIngress.CellAttachment, invocation => fixture.Cells.StoreCurrentCell(invocation, fixture.Cell));
            fixture.Call(FalloutReferencePositionIngress.CopyOrLoad, invocation =>
                fixture.Cells.ObserveUnownedWriter(invocation, FalloutInitialPlacementCellLane.Grid, "authored-independent-grid-copy"));
            var loaded = fixture.Call(FalloutReferencePositionIngress.ActorPosition, fixture.Cells.ReadActorMovementCurrentCell);
            Require(loaded.Cell == fixture.Cell && loaded.Factory == cell.Factory && loaded.Sequence > cell.Sequence,
                "TES current CELL borrowed its unavailable grid or lost the actual load store.");
            var snapshot = fixture.Capture();
            using var cold = new ReferencePositionFixture(snapshot);
            Require(cold.Call(FalloutReferencePositionIngress.ActorPosition, cold.Cells.ReadActorMovementCurrentCell).Cell == fixture.Cell &&
                JsonSerializer.Serialize(cold.Cells.Capture().Calls) == JsonSerializer.Serialize(snapshot.Cells.Calls),
                "Cold TES current pointer replayed its loader store.");
            fixture.Call(FalloutReferencePositionIngress.CopyOrLoad, invocation =>
                fixture.Cells.ObserveUnownedWriter(invocation, FalloutInitialPlacementCellLane.CurrentCell, "authored-current-interior-writer"));
            Reject(() => fixture.Call(FalloutReferencePositionIngress.ActorPosition, fixture.Cells.ReadActorMovementCurrentCell));
        }
        var inputs = new FalloutPhysicalConstructorInputs(FalloutSourceMainFamily.Fallout3, new(0, 0));
        var rotation = new FalloutPhysicalRotation(FalloutPhysicalVector3.FromValues(0, 1, 0),
            FalloutPhysicalVector3.FromValues(-1, 0, 0), FalloutPhysicalVector3.FromValues(0, 0, 1));
        var offset = FalloutPhysicalVector3.FromValues(2, 3, 4);
        var current = new FalloutCharacterControllerProxyTransform(rotation, FalloutPhysicalVector3.FromValues(12, 21, 30));
        var transform = new FalloutCharacterControllerTransformState(inputs, offset, () => current);
        var actualPosition = transform.ReadReferencePositionBits(current);
        var scale = BitConverter.UInt32BitsToSingle(FalloutPhysicalConstructorInputs.PhysicalToSourceBits);
        Require(actualPosition == FalloutPhysicalVector3.FromValues(15 * scale, 19 * scale, 26 * scale) &&
            transform.ReadPositionZBits() == actualPosition.Z,
            "Real proxy XYZ getter lost independent rotated X/Y/Z offset reads after its saved register pop.");
        Require(FalloutPlayerActorMovementState.DistanceSquared(new(0, 0, 0), FalloutPhysicalVector3.FromValues(3, 4, 12)) ==
                BitConverter.SingleToUInt32Bits(169) &&
            FalloutPlayerActorMovementState.ValidAngle(BitConverter.SingleToUInt32Bits(-float.MaxValue)) &&
            !FalloutPlayerActorMovementState.ValidAngle(FalloutPlayerActorMovementSource.ExceptionalAngleBits) &&
            !FalloutPlayerActorMovementState.ValidAngle(BitConverter.SingleToUInt32Bits(float.NaN)) &&
            !FalloutPlayerActorMovementState.ValidAngle(BitConverter.SingleToUInt32Bits(float.PositiveInfinity)),
            "Actor movement changed its Float32 source distance or exceptional-angle branch.");
        Console.WriteLine("OPENNV_PLAYER_ACTOR_MOVEMENT_FIELDS_PASS sameReferenceKindXYZ=true sameTES=true " +
            "unknownWritersRefused=true coldCycles=3 actualProxyArithmetic=true fullActorBody=UNEXECUTED gameplay=UNEXECUTED");
    }
    internal static void AuditOwnedPlayerActorMovementSource(FalloutPluginStack records, FalloutEarlyControllerSource early)
    {
        var owned = records.OwnedSource ?? throw new InvalidOperationException("Actor movement source audit requires actual owned inputs.");
        var mover = FalloutExecutableStringTable.ReadPlayerMoverDelta(owned.FalloutExecutablePath, early);
        var cells = FalloutExecutableStringTable.ReadInitialPlacementCells(owned.FalloutExecutablePath,
            FalloutReferencePositionSource.Reviewed(early.Fields));
        var orientation = FalloutExecutableStringTable.ReadPlayerWorldOrientation(owned.FalloutExecutablePath, early.Fields.Controls);
        var source = FalloutExecutableStringTable.ReadPlayerActorMovement(owned.FalloutExecutablePath, mover, cells, orientation);
        source.Validate();
        Console.WriteLine($"OPENNV_PLAYER_ACTOR_MOVEMENT_SOURCE source={source.Identity} sharedDisableWord=true sharedLife=true " +
            "cameraBeforePlayerBranch=true currentTESLoadStoresQualified=true controlledPhaseCtor=true body=UNEXECUTED gameplay=UNEXECUTED");
    }
}
