using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.World.Cells;

internal static class RuntimeNativePlayerMoves
{
    internal static async Task<FalloutPlayerMove?> ApplyNextAsync(FalloutReferenceWorld world, RuntimeNativePlayer player,
        FalloutFormKey activeCell, Func<FalloutReferencePlacement, Transform3D, Task> transfer)
    {
        if (world.PlayerMoves.Next is not { } move) return null;
        try
        {
            var (placement, transform) = Resolve(world, player, activeCell, move);
            if (placement.Cell == activeCell) player.Teleport(transform);
            else await transfer(placement, transform);
            world.PlayerMoves.Complete(move);
            return move;
        }
        catch (Exception error)
        {
            world.PlayerMoves.Fail(move, error);
            throw;
        }
    }

    internal static FalloutPlayerMove? ApplyNext(FalloutReferenceWorld world, RuntimeNativePlayer player,
        FalloutFormKey activeCell, Action<FalloutReferencePlacement, Transform3D> transfer)
    {
        if (world.PlayerMoves.Next is not { } move) return null;
        try
        {
            var (placement, transform) = Resolve(world, player, activeCell, move);
            if (placement.Cell == activeCell) player.Teleport(transform);
            else transfer(placement, transform);
            world.PlayerMoves.Complete(move);
            return move;
        }
        catch (Exception error)
        {
            world.PlayerMoves.Fail(move, error);
            throw;
        }
    }

    private static (FalloutReferencePlacement Placement, Transform3D Transform) Resolve(FalloutReferenceWorld world,
        RuntimeNativePlayer player, FalloutFormKey activeCell, FalloutPlayerMove move)
    {
        var position = player.GlobalPosition / player.UnitsToMeters;
        var rotation = GamebryoCoordinate.ReferenceEuler(player.GlobalBasis);
        var placement = world.ResolvePlayerMove(move,
            new(activeCell, [position.X, -position.Z, position.Y], [rotation.X, rotation.Y, rotation.Z]), player.UnitsToMeters);
        var transform = new Transform3D(GamebryoCoordinate.ConvertReferenceEuler(
            new(placement.RotationRadians[0], placement.RotationRadians[1], placement.RotationRadians[2]), 1),
            GamebryoCoordinate.ConvertVector(new(placement.Position[0], placement.Position[1], placement.Position[2])) * player.UnitsToMeters);
        return (placement, transform);
    }
}
