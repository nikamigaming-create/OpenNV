using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Actors;

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

    private void RequireNpcDoor(Binding door, FalloutFormKey actor, bool portal = false)
    {
        if (!_bindings.TryGetValue(actor, out var activator) || activator.Signature is not ("NPC_" or "CREA") ||
            activator.Node is null || !_world.CanActivate(actor) || _world.IsDead(actor))
            throw new NotSupportedException("Door activation requires a living resident actor owner.");
        RequireRouteDoor(door, portal: portal);
        if (portal && (activator.Node is not INativeActorPortal native || !native.WantsPortal(door.Reference.FormKey)))
            throw new NotSupportedException("Actor portal activation has no matching native package leg.");
        if (!_world.CanNpcOpenDoor(door.Reference.FormKey, actor))
            throw new NotSupportedException("Locked NPC route door requires its retained key or conditional ownership owner.");
    }

    private void RequireRouteDoor(Binding door, FalloutFormKey? activatingParent = null, bool portal = false)
    {
        if (!_world.CanActivate(door.Reference.FormKey)) throw new NotSupportedException("Route door is not active.");
        if ((door.Reference.Flags & 0x100) != 0) throw new NotSupportedException("Route door is inaccessible.");
        if (door.Reference.Teleport is not null && !portal) throw new NotSupportedException("Portal collision is not an ordinary animated route door.");
        var parent = _records.GetEffective(door.Reference.FormKey).ReadSubrecords().Where(field => field.Signature == "XAPD").ToArray();
        if (parent.Length > 1 || parent.Length == 1 && parent[0].Data.Length != 1)
            throw new InvalidDataException("Door activation-parent declaration is invalid.");
        if (parent.Length == 1 && parent[0].Data.Span[0] != 0 && (activatingParent is null || !_world.IsActivationParent(door.Reference.FormKey, activatingParent.Value)))
            throw new NotSupportedException("Route door admits only activation from its parent.");
        if (door.Instance.ScriptError is { } error) throw new NotSupportedException($"Route door retains a source failure: {error}");
    }

    internal bool RequestNpcPortal(FalloutFormKey actor, FalloutFormKey door)
    {
        if (!_bindings.TryGetValue(door, out var binding) || binding.Signature != "DOOR" ||
            binding.Reference.Teleport is null || binding.Node is not { } node)
            throw new NotSupportedException("NPC Travel portal has no resident source door.");
        RequireNpcDoor(binding, actor, portal: true);
        if (binding.PendingActivation is not null) return false;
        var native = (INativeActorPortal)_bindings[actor].Node!;
        var npc = native.PortalBody;
        var combat = native.PortalCombat;
        if (!npc.IsOnFloor() || !combat.PackageMovementReady) return false;
        var origin = combat.InteractionOrigin;
        var surface = NativeReferenceGeometryTarget.ObserveSurface(node, npc, origin, combat.CollisionRids,
            contact => CollisionReference(contact.GetInstanceId()) == door, _ => _world.IsResident(door));
        if (origin.DistanceTo(surface.Aim) > combat.PackageActivationRange) return false;
        using var query = PhysicsRayQueryParameters3D.Create(origin,
            surface.Aim + (surface.Aim - origin).Normalized() * .01f, npc.CollisionMask | npc.CollisionLayer);
        query.Exclude = new Godot.Collections.Array<Rid>(combat.CollisionRids);
        query.CollideWithAreas = true;
        query.HitBackFaces = false;
        using var hit = npc.GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (!hit.TryGetValue("collider", out var collider) || collider.AsGodotObject() is not Node contact ||
            CollisionReference(contact.GetInstanceId()) != door) return false;
        binding.PendingActivation = actor;
        binding.PendingPlayerInput = false;
        GD.Print($"OPENNV_NATIVE_NPC_PORTAL_ACTIVATE actor={actor} door={door} queued=true");
        return true;
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
