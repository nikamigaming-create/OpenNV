using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    private object? _rawPlayerPlacementLease;
    private Func<FalloutReferencePlacement>? _rawPlayerPlacement;
    private float? _rawTransferUnits;
    internal FalloutPlayerRawTransferSource PlayerRawTransferSource => FalloutPlayerRawTransferSource.Read(
        FalloutMainPlayerPendingSource.Read(CampaignMainPlayerCellSource));

    internal IDisposable BindCurrentPlayerTransferPlacement(Func<FalloutReferencePlacement> placement, float unitsToMetres)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); ArgumentNullException.ThrowIfNull(placement);
        if (_rawPlayerPlacementLease is not null || !float.IsFinite(unitsToMetres) || unitsToMetres <= 0)
            throw new InvalidOperationException("Current Player raw placement requires one actual body lease and its coordinate scale.");
        var lease = new object(); _rawPlayerPlacementLease = lease;
        _rawPlayerPlacement = placement; _rawTransferUnits = unitsToMetres;
        return new RawPlayerPlacementLease(this, lease);
    }

    private sealed class RawPlayerPlacementLease(FalloutReferenceWorld world, object lease) : IDisposable
    {
        public void Dispose()
        {
            if (!ReferenceEquals(world._rawPlayerPlacementLease, lease)) return;
            world._rawPlayerPlacementLease = null; world._rawPlayerPlacement = null; world._rawTransferUnits = null;
        }
    }

    private FalloutReferencePlacement ReadRawTransferPlacement(FalloutFormKey target, out string owner)
    {
        if (target == _enginePlayer)
        {
            owner = "actual-published-Player-current-placement";
            var current = _rawPlayerPlacement ?? throw new NotSupportedException("Player MoveTo target has no actual current native placement lease.");
            var placement = current(); placement.Validate(); return placement;
        }
        var instance = Get(target);
        if (instance.Deleted || instance.DeletePending)
            throw new InvalidOperationException("Player transfer target is actually deleted or pending deletion.");
        if (instance.QuerySpatialPlacement is { } live)
        {
            owner = "actual-published-target-current-placement";
            var placement = live(); placement.Validate();
            if (placement.Cell != Placement(target).Cell)
                throw new InvalidDataException("Actual transfer target changed its authoritative current CELL.");
            return placement;
        }
        if (instance.CaptureEngagement is not null || instance.Engagement?.Position is not null || instance.PackageMotion?.Position is not null)
        {
            var units = _rawTransferUnits ?? throw new NotSupportedException("Moving transfer target has no actual source/native coordinate conversion owner.");
            owner = "actual-retained-target-physical-motion-placement";
            return SpatialPlacement(target, null, units, requireFacing: true);
        }
        owner = "authoritative-current-reference-placement";
        return Placement(target);
    }

    private string RawTransferReferenceHash(FalloutFormKey reference)
    {
        if (reference == _enginePlayer) return PlayerRawTransferSource.Pending.Player.Main.EngineSha256;
        var record = records.GetEffective(reference);
        if (record.IsDeleted) throw new InvalidDataException("Player transfer references a deleted winning source record.");
        return FalloutActorFurnitureContinuation.RecordHash(record);
    }

    internal void QueueSourcePlayerRawMoveTo(FalloutFormKey source, FalloutFormKey target, float x, float y, float z)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var declaration = PlayerRawTransferSource;
        // This is the actual source Actor neutral-life getter used by MoveTo.
        // It is separate from GetSitting, sleeping and knocked-state queries.
        if (ProcessRuntime.Life(_enginePlayer).Require() is 1 or 2 or 6) return;
        var placement = ReadRawTransferPlacement(target, out var owner);
        var cell = ReadMainPlayerSourceCell(placement.Cell);
        var receipt = new FalloutPlayerRawTransferReceipt(declaration, FalloutPlayerRawTransferWriter.MoveTo, Guid.NewGuid(),
            source, RawTransferReferenceHash(source), target, RawTransferReferenceHash(target), cell.Source, cell.CellFlags,
            FalloutPlayerTransferVector.Read(placement.Position), FalloutPlayerTransferVector.Read(placement.RotationRadians),
            FalloutPlayerTransferVector.Read([x, y, z]), owner);
        var move = new FalloutPlayerMove(source, target, x, y, z);
        PlayerMoves.EnqueueSource(move, FalloutPlayerRawTransferFactory.MoveTo(receipt));
    }

    internal FalloutPlayerPendingRequest QueueSourcePlayerDoor(FalloutFormKey door, string owner)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var record = records.GetEffective(door);
        if (record.IsDeleted || record.Signature != "REFR") throw new InvalidDataException("Door pending factory has no winning placed door.");
        var cell = FalloutCellSceneReader.ParentCell(record) ?? throw new InvalidDataException("Pending door has no source CELL.");
        var reference = FalloutCellSceneReader.Read(records, cell).References.Single(value => value.FormKey == door);
        var destination = FalloutDoorDestinationResolver.Resolve(records, reference);
        var teleport = reference.Teleport!;
        var targetPlacement = ReadRawTransferPlacement(destination.Destination.FormKey, out var placementOwner);
        var targetCell = ReadMainPlayerSourceCell(targetPlacement.Cell);
        var receipt = new FalloutPlayerRawTransferReceipt(PlayerRawTransferSource, FalloutPlayerRawTransferWriter.Door, Guid.NewGuid(),
            door, RawTransferReferenceHash(door), destination.Destination.FormKey, RawTransferReferenceHash(destination.Destination.FormKey),
            targetCell.Source, targetCell.CellFlags, FalloutPlayerTransferVector.Read(teleport.Position),
            FalloutPlayerTransferVector.Read(teleport.RotationRadians), new(0, 0, 0), placementOwner + "/directed-XTEL-values");
        return PlayerMoves.SourcePending.StoreSource(FalloutPlayerPendingKind.Door, null, door, owner,
            FalloutPlayerRawTransferFactory.Door(receipt));
    }

    internal void RequireSourcePlayerRawTransfer(FalloutPlayerTransferPayload payload)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        FalloutPlayerRawTransferFactory.Require(payload, PlayerRawTransferSource);
        var receipt = payload.FactoryReceipt!;
        if (RawTransferReferenceHash(receipt.RequestSource) != receipt.RequestSha256 ||
            RawTransferReferenceHash(receipt.Target) != receipt.TargetSha256 ||
            ReadMainPlayerSourceCell(receipt.ParentCell.Cell) is not { } cell || cell.Source != receipt.ParentCell ||
            cell.CellFlags != receipt.ParentCellFlags)
            throw new InvalidDataException("Raw Player transfer changed its exact source winner/master/CELL declaration.");
        // The target may have moved after the command. Its current placement is
        // deliberately not re-read: the original allocation already owns the
        // captured values, which survive a genuine cold queue continuation.
    }

    internal FalloutReferencePlacement ReadSourcePlayerRawPlacement(FalloutPlayerPendingRequest request,
        FalloutActorProcessFact<FalloutSourceFloatRounding> rounding)
    {
        PlayerMoves.SourcePending.Require(request);
        var payload = ReadMainPlayerPendingPayload(request); RequireSourcePlayerRawTransfer(payload);
        FalloutFormKey cell;
        switch (payload.Target)
        {
            case FalloutPlayerTransferTarget.Cell:
                cell = payload.Cell!.Value;
                break;
            case FalloutPlayerTransferTarget.Worldspace:
                var mode = rounding.Require();
                var x = FalloutQueuedReferencePriority.ConvertSourcePosition(payload.PositionX, mode) >> 12;
                var y = FalloutQueuedReferencePriority.ConvertSourcePosition(payload.PositionY, mode) >> 12;
                cell = _mainPlayerExterior.SpatialCellAtSourceCoordinates(payload.Worldspace!.Value, x, y);
                if (ReadMainPlayerSourceCell(cell).Source.Worldspace != payload.Worldspace)
                    throw new InvalidDataException("Raw Player world lookup changed its exact source world.");
                break;
            default:
                throw new NotSupportedException("Raw Player placement has no admitted reference/empty destination consumer.");
        }
        var placement = new FalloutReferencePlacement(cell,
            [BitConverter.UInt32BitsToSingle(payload.PositionX), BitConverter.UInt32BitsToSingle(payload.PositionY), BitConverter.UInt32BitsToSingle(payload.PositionZ)],
            [BitConverter.UInt32BitsToSingle(payload.RotationX), BitConverter.UInt32BitsToSingle(payload.RotationY), BitConverter.UInt32BitsToSingle(payload.RotationZ)]);
        placement.Validate(); return placement;
    }
}
