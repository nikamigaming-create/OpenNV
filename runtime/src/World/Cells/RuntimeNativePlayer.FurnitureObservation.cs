using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed record NativePlayerFurnitureInteraction(FalloutFormKey Reference, long Ordinal, bool Pending);

internal partial class RuntimeNativePlayer
{
    // Diagnostic identity of actual successful native activation publication.
    // It grants no gameplay result and is not a campaign/save cursor.
    private long _furnitureInteractionOrdinal;

    internal NativePlayerFurnitureInteraction? ObserveFurnitureInteraction()
    {
        if (_furnitureError is not null)
            throw new NotSupportedException($"Player furniture continuation failed: {_furnitureError}");
        if (_furniturePhase == 0) return null;
        if (_furniturePhase is < 1 or > 4 || _furnitureReference is not { } reference ||
            _furnitureSeat is not { } seat || _furnitureWorld is not { } world ||
            _furnitureReservationActor is not { } actor)
            throw new InvalidOperationException("Player furniture observation has no complete source reservation owner.");
        if (!IsInsideTree() || _furnitureBody is not { } body || !GodotObject.IsInstanceValid(body) || !body.IsInsideTree())
            throw new InvalidOperationException("Player furniture observation has no living native body owner.");
        if (!world.IsEnabled(reference) || !world.OwnsFurnitureSeat(reference, seat.Index, actor))
            throw new InvalidOperationException("Player furniture observation lost its exact enabled source seat reservation.");
        return new(reference, _furnitureInteractionOrdinal, _furniturePhase == 1);
    }
}
