using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal partial class RuntimeNativeReferenceEvents
{
    // Observation does not admit input, acknowledge a failed script or consume
    // an existing queue entry. The ordinary TryActivate queue remains its owner.
    internal bool CanAdmitIndependentDefaultInteraction(FalloutFormKey reference)
    {
        if (!_bindings.TryGetValue(reference, out var binding) || !_world.IsResident(reference) ||
            binding.Node is not { } node || !GodotObject.IsInstanceValid(node) || !node.IsInsideTree() ||
            !node.IsVisibleInTree() || node.IsQueuedForDeletion() || Interact is null ||
            !_scripts.CanAdmitIndependentDefaultActivation(reference)) return false;
        if (binding.PendingActivation is { } pending && pending != _records.RuntimeFormKey(0x14)) return false;
        // These are the existing DefaultActivate branches with a native
        // interaction callback. Other authored/default capabilities retain
        // their prior fault guards until their independent owner is proven.
        return binding.Signature is "DOOR" or "CONT" or "TERM" ||
            FalloutReferenceWorld.IsInventoryItem(binding.Signature) ||
            binding.Signature is "NPC_" or "CREA" && _world.IsDead(reference);
    }
}
