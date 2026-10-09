using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime.World.Cells;

internal partial class RuntimeNativeReferenceEvents
{
    internal void RequireSharedReferenceRetirement(FalloutFormKey reference, IReadOnlyList<ulong> actualObjects)
    {
        if (!_bindings.TryGetValue(reference, out var binding))
            throw new InvalidDataException("Shared CELL retirement lost its actual source reference-event binding.");
        if (binding.PendingActivation is not null || binding.PendingRelay is not null || binding.PendingPlayerInput ||
            _dispatchingActivationRelays.Contains(reference) || _world.PackageEvents.HasPending(reference) ||
            _world.HitEvents.HasPending(reference) || binding.Contacts.HasAdmittedContacts)
            throw new NotSupportedException("Actual outgoing reference retains an admitted contact/activation/package/hit consumer; off-cell completion is unowned.");
        if (Player?.CurrentFurniture == reference || _bindings.Values.Any(value => value.Node is RuntimeNativeNpc actor && actor.CurrentFurniture == reference))
            throw new NotSupportedException("Actual shared CELL retirement still owns a living furniture participant.");
        var nodes = SourceCellNativeConsumers(reference);
        if (nodes.Any(node => !GodotObject.IsInstanceValid(node) || !actualObjects.Contains(node.GetInstanceId())))
            throw new InvalidDataException("Shared CELL retirement changed an actual source event consumer without a native lease.");
        if (binding.Trigger is { } trigger)
        {
            var bodies = trigger.GetOverlappingBodies(); var areas = trigger.GetOverlappingAreas();
            using var bodiesOwner = (Godot.Collections.Array)bodies;
            using var areasOwner = (Godot.Collections.Array)areas;
            if (bodies.Cast<Node>().Concat(areas).Any(node => Contact(node) is not null))
                throw new NotSupportedException("Actual source trigger still owns an unconsumed physical contact at shared CELL retirement.");
        }
    }
    internal void ForgetDestroyedSourceReference(FalloutFormKey reference)
    {
        if (!_bindings.Remove(reference, out var binding)) return;
        if (binding.Node is { } node && GodotObject.IsInstanceValid(node)) _nodeReferences.Remove(node.GetInstanceId());
        // Ability/script state belongs to the retained C# instance, not this
        // presentation lifetime. Keep its started/fault receipts on reentry.
    }
}
