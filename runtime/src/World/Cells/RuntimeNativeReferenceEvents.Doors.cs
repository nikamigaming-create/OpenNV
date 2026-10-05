using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed record NativeRouteDoorStatus(FalloutFormKey? Reference, bool Open = false, bool Moving = false,
    bool Pending = false, string? Error = null, bool Admitted = false)
{
    internal bool RequiresInteraction => !Open || Moving || Pending;
}

internal partial class RuntimeNativeReferenceEvents
{
    internal FalloutFormKey? CollisionReference(ulong collider)
    {
        for (var node = GodotObject.InstanceFromId(collider) as Node; node is not null; node = node.GetParent())
            if (_nodeReferences.TryGetValue(node.GetInstanceId(), out var reference)) return reference;
        return null;
    }

    private void RequireNpcDoor(Binding door, FalloutFormKey actor)
    {
        if (!_bindings.TryGetValue(actor, out var activator) || activator.Signature != "NPC_" ||
            activator.Node is null || !_world.CanActivate(actor) || _world.IsDead(actor))
            throw new NotSupportedException("Door activation requires a living resident NPC owner; creature door capabilities remain unbound.");
        RequireRouteDoor(door);
        if (_world.GetLocked(door.Reference.FormKey) != 0)
            throw new NotSupportedException("Locked NPC route-door key/ownership semantics remain unbound.");
    }

    private void RequireRouteDoor(Binding door)
    {
        if (!_world.CanActivate(door.Reference.FormKey)) throw new NotSupportedException("Route door is not active.");
        if ((door.Reference.Flags & 0x100) != 0) throw new NotSupportedException("Route door is inaccessible.");
        if (door.Reference.Teleport is not null) throw new NotSupportedException("NPC portal traversal requires its actor transfer owner.");
        var parent = _records.GetEffective(door.Reference.FormKey).ReadSubrecords().Where(field => field.Signature == "XAPD").ToArray();
        if (parent.Length > 1 || parent.Length == 1 && parent[0].Data.Length != 1)
            throw new InvalidDataException("Door activation-parent declaration is invalid.");
        if (parent.Length == 1 && parent[0].Data.Span[0] != 0)
            throw new NotSupportedException("Route door admits only activation from its parent.");
        if (door.Instance.ScriptError is { } error) throw new NotSupportedException($"Route door retains a source failure: {error}");
    }

    // Read-only observation for the player bot. Activation still comes through
    // the ordinary aimed input and its existing player event owner.
    internal NativeRouteDoorStatus PlayerRouteDoor(FalloutFormKey reference)
    {
        if (!_bindings.TryGetValue(reference, out var binding) || binding.Signature != "DOOR")
            return new(null, Error: "Source route collision is not a resident door.");
        try
        {
            RequireRouteDoor(binding);
            if (_world.GetLocked(reference) != 0)
                throw new NotSupportedException("Locked route door needs its ordinary unlocking interaction.");
            var motion = binding.Node?.GetChildren().OfType<RuntimeNativeDoorMotion>().SingleOrDefault() ??
                throw new NotSupportedException("Route door has no resident source motion owner.");
            motion.Synchronize();
            return new(reference, binding.Instance.DoorOpen, binding.Instance.DoorMotion?.Moving == true,
                binding.PendingActivation is not null);
        }
        catch (Exception error) when (error is InvalidOperationException or InvalidDataException or NotSupportedException)
        {
            return new(reference, Error: error.Message);
        }
    }

    internal NativeRouteDoorStatus RouteDoor(FalloutFormKey actor, ulong collider, bool activate)
    {
        var node = GodotObject.InstanceFromId(collider) as Node;
        var reference = node is null ? null : AimedReference(node);
        if (reference is null || !_bindings.TryGetValue(reference.FormKey, out var binding) || binding.Signature != "DOOR")
            return new(null, Error: "Source corridor collision is not a resident door.");
        try
        {
            RequireNpcDoor(binding, actor);
            var motion = binding.Node?.GetChildren().OfType<RuntimeNativeDoorMotion>().SingleOrDefault() ??
                throw new NotSupportedException("Route door has no resident source motion owner.");
            motion.Synchronize();
            var admitted = false;
            if (activate && !binding.Instance.DoorOpen && binding.Instance.DoorMotion?.Moving != true && binding.PendingActivation is null)
            {
                binding.PendingActivation = actor;
                binding.PendingPlayerInput = false;
                admitted = true;
                GD.Print($"OPENNV_NATIVE_NPC_DOOR_ACTIVATE actor={actor} reference={reference.FormKey} collider={collider} queued=true");
            }
            return new(reference.FormKey, binding.Instance.DoorOpen, binding.Instance.DoorMotion?.Moving == true,
                binding.PendingActivation is not null, Admitted: admitted);
        }
        catch (Exception error) when (error is InvalidOperationException or InvalidDataException or NotSupportedException)
        {
            return new(reference.FormKey, Error: error.Message);
        }
    }
}
