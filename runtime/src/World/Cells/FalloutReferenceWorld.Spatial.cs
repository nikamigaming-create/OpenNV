using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using Godot;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    internal float HeadingAngle(FalloutFormKey caller, FalloutFormKey target,
        FalloutReferencePlacement? player, float unitsToMetres,
        Func<FalloutFormKey, FalloutReferencePlacement?>? livePlacement = null)
    {
        if (caller != records.RuntimeFormKey(0x14) && (records.GetEffective(caller).Signature is not ("REFR" or "ACHR" or "ACRE") ||
            records.GetEffective(Get(caller).Base).Signature is not ("NPC_" or "CREA")))
            return 0;
        var from = livePlacement?.Invoke(caller) ?? SpatialPlacement(caller, player, unitsToMetres, requireFacing: true);
        var to = livePlacement?.Invoke(target) ?? SpatialPlacement(target, player, unitsToMetres);
        from.Validate(); to.Validate();
        var first = FalloutCellSceneReader.ReadDefinition(records, from.Cell);
        var second = FalloutCellSceneReader.ReadDefinition(records, to.Cell);
        if (from.Cell != to.Cell && (first.Worldspace is null || first.Worldspace != second.Worldspace))
            throw new NotSupportedException("Heading between unrelated interior/world spaces is unbound.");
        return FalloutHeadingAngle.Between(from.Position, to.Position, from.RotationRadians[2]);
    }

    internal void QueuePlayerMoveTo(FalloutFormKey source, FalloutFormKey destination, float x = 0, float y = 0, float z = 0)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _ = records.GetEffective(source);
        if (destination != records.RuntimeFormKey(0x14) && Get(destination) is ({ Deleted: true } or { DeletePending: true }))
            throw new InvalidOperationException("Player MoveTo destination is deleted.");
        PlayerMoves.Enqueue(new(source, destination, x, y, z));
    }

    internal FalloutReferencePlacement ResolvePlayerMove(FalloutPlayerMove move,
        FalloutReferencePlacement player, float unitsToMetres)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (move.Destination != records.RuntimeFormKey(0x14) && Get(move.Destination) is ({ Deleted: true } or { DeletePending: true }))
            throw new InvalidOperationException("Player MoveTo destination is deleted.");
        var result = FalloutPlayerMoves.Resolve(move, SpatialPlacement(move.Destination, player, unitsToMetres));
        if (records.GetEffective(result.Cell).Signature != "CELL") throw new InvalidDataException("Player MoveTo destination has no CELL.");
        return result;
    }

    internal bool IsInInterior(FalloutFormKey reference, FalloutFormKey? playerCell = null)
    {
        var cell = reference == records.RuntimeFormKey(0x14)
            ? playerCell ?? throw new NotSupportedException("Player interior query has no live cell owner.")
            : Placement(reference).Cell;
        return (FalloutCellSceneReader.ReadDefinition(records, cell).Flags & FalloutCellSceneReader.InteriorCellFlag) != 0;
    }

    internal bool IsInCell(FalloutFormKey reference, FalloutFormKey requested) => IsInCell(reference, requested, null);

    internal bool IsInCell(FalloutFormKey reference, FalloutFormKey requested, FalloutFormKey? playerCell)
    {
        var cell = reference == records.RuntimeFormKey(0x14)
            ? playerCell ?? throw new NotSupportedException("Player GetInCell has no live cell owner.")
            : Placement(reference).Cell;
        return FalloutCellQueries.InCell(records, cell, requested);
    }

    internal float Distance(FalloutFormKey first, FalloutFormKey second,
        FalloutReferencePlacement? player, float unitsToMetres)
    {
        var from = SpatialPlacement(first, player, unitsToMetres);
        var to = SpatialPlacement(second, player, unitsToMetres);
        var fromCell = FalloutCellSceneReader.ReadDefinition(records, from.Cell);
        var toCell = FalloutCellSceneReader.ReadDefinition(records, to.Cell);
        if (from.Cell != to.Cell && (fromCell.Worldspace is null || fromCell.Worldspace != toCell.Worldspace))
            return float.MaxValue;
        var dx = (double)from.Position[0] - to.Position[0];
        var dy = (double)from.Position[1] - to.Position[1];
        var dz = (double)from.Position[2] - to.Position[2];
        return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    private FalloutReferencePlacement SpatialPlacement(FalloutFormKey reference,
        FalloutReferencePlacement? player, float unitsToMetres, bool requireFacing = false)
    {
        if (!float.IsFinite(unitsToMetres) || unitsToMetres <= 0)
            throw new ArgumentOutOfRangeException(nameof(unitsToMetres));
        if (reference == records.RuntimeFormKey(0x14))
        {
            if (player is null) throw new NotSupportedException("Player spatial query has no live placement.");
            player.Validate();
            return player;
        }
        var placement = Placement(reference);
        var state = Get(reference);
        var engagement = state.CaptureEngagement?.Invoke() ?? state.Engagement;
        var motion = engagement?.Position ?? state.PackageMotion?.Position;
        if (motion is null) return placement;
        var rotation = engagement?.Position is not null ? engagement.Rotation : state.PackageMotion?.Rotation;
        if (rotation is null && requireFacing)
            throw new NotSupportedException("Actor motion has no current heading owner.");
        var angles = rotation is null ? null : (Vector3?)GamebryoCoordinate.ReferenceEuler(
            new Basis(new Quaternion(rotation[0], rotation[1], rotation[2], rotation[3])));
        var current = placement with
        {
            Position = [motion[0] / unitsToMetres, -motion[2] / unitsToMetres, motion[1] / unitsToMetres],
            RotationRadians = angles is { } facing ? [facing.X, facing.Y, facing.Z] : placement.RotationRadians
        };
        current.Validate();
        return current;
    }

    internal bool InSameCell(FalloutFormKey first, FalloutFormKey second,
        FalloutReferencePlacement? player, float unitsToMetres)
    {
        if (!float.IsFinite(unitsToMetres) || unitsToMetres <= 0)
            throw new ArgumentOutOfRangeException(nameof(unitsToMetres));
        player?.Validate();
        (FalloutFormKey? World, FalloutFormKey? Interior, int X, int Y) Location(FalloutFormKey key)
        {
            var placement = SpatialPlacement(key, player, unitsToMetres);
            var cell = FalloutCellSceneReader.ReadDefinition(records, placement.Cell);
            if (cell.Worldspace is null) return (null, cell.FormKey, 0, 0);
            var x = placement.Position[0]; var y = placement.Position[1];
            return (cell.Worldspace, null, (int)MathF.Floor(x / 4096), (int)MathF.Floor(y / 4096));
        }
        return Location(first) == Location(second);
    }
}
