using OpenNV.Runtime.World.Cells;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.World.Actors;

internal sealed partial class RuntimeNativeActorCombat
{
    internal void RequireAutomaticIdleSlot(byte slot)
    {
        if (slot == 0 || slot > 7 || slot == 1 &&
            (_packageMovementLayer is not null || _packageOutgoingLayers.Any(value => value.Slot == 1)))
            throw new NotSupportedException($"Automatic creature IDLE slot {slot} requires its active source-slot replacement owner.");
        // Creature package presentation owns slots 0/1; event, script and combat
        // admission have already excluded their other active sequence owners.
    }

    internal NativeActorCombatAnimation AutomaticIdleReplacementSource(FalloutPackageAnimationSource source)
    {
        source.Validate();
        return PackageLayerClip(source.Resource, source.Sha256);
    }

    internal void InsertAutomaticIdleLayers(List<(RuntimeNativeNifAnimation Animation, float Time, float Ease)> destination,
        IEnumerable<(RuntimeNativeNifAnimation Animation, float Time, float Ease)> automatic, byte slot)
    {
        var index = slot == 1 && _packageMovementLayer is { } movement ?
            destination.FindIndex(value => ReferenceEquals(value.Animation, movement.Clip.Animation)) : -1;
        if (index < 0) destination.AddRange(automatic);
        else destination.InsertRange(index, automatic);
    }
}
