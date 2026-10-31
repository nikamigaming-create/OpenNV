using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

internal sealed partial class RuntimeNativeActorCombat
{
    private sealed class RouteDoor(NativeNavigationContact contact, FalloutFormKey reference, bool portal)
    {
        internal NativeNavigationContact Contact { get; } = contact;
        internal FalloutFormKey Reference { get; } = reference;
        internal bool Portal { get; } = portal;
        internal bool Requested;
        internal double Seconds;
        internal string? Error;
    }
    private RouteDoor? _routeDoor;
    internal bool AutomaticIdleRequestReady => _routeDoor is null;
    internal Vector3? PackagePortalContact(FalloutFormKey door) =>
        _routeDoor is { Portal: true } route && route.Reference == door && route.Contact.Reference == door &&
        GodotObject.InstanceFromId(route.Contact.Collider) is CollisionObject3D collider &&
        GodotObject.IsInstanceValid(collider) && collider.IsInsideTree() && route.Contact.Point.IsFinite()
            ? route.Contact.Point : null;
    internal bool PackagePortalContactInReach(FalloutFormKey door) =>
        PackagePortalContact(door) is { } point && InteractionOrigin.DistanceSquaredTo(point) <=
            PackageActivationRange * PackageActivationRange;
    private int _doorActivations;
    private object DoorNavigationObservation => new
    {
        reference = _routeDoor?.Reference.ToString(),
        portal = _routeDoor?.Portal,
        collider = _routeDoor?.Contact.Collider,
        requested = _routeDoor?.Requested,
        seconds = _routeDoor?.Seconds,
        error = _routeDoor?.Error,
        activations = _doorActivations,
        corridorContact = ContactObservation(_routeProbe?.CorridorContact),
        rejectedContact = ContactObservation(_routeProbe?.RejectedContact),
        rejectedContacts = _routeProbe?.RejectedContacts.Select(ContactObservation).ToArray(),
        rejectedEdges = _routeProbe?.RejectedEdges,
        boundary = "exact-corridor-contact;ordinary-door-animation-or-matching-XTEL-parent-activation;retail-reach-timing-unmatched"
    };
    private static object? ContactObservation(NativeNavigationContact? contact) => contact is null ? null : new
    {
        collider = contact.Collider,
        shape = contact.Shape,
        reference = contact.Reference?.ToString(),
        colliderPath = GodotObject.InstanceFromId(contact.Collider) is Node node && node.IsInsideTree() ? node.GetPath().ToString() : null,
        from = new[] { contact.From.X, contact.From.Y, contact.From.Z },
        desired = new[] { contact.Desired.X, contact.Desired.Y, contact.Desired.Z },
        point = new[] { contact.Point.X, contact.Point.Y, contact.Point.Z },
        normal = new[] { contact.Normal.X, contact.Normal.Y, contact.Normal.Z }
    };

    private void ResetDoorNavigation()
    {
        _routeDoor = null;
        _routeProbe = null;
        _pursuitPath = [];
        _pursuitCursor = 0;
        _routeClock = 0;
    }

    private void BeginDoorNavigation()
    {
        if (_context?.RouteDoor is null || _routeProbe is not { RejectedContact: { } contact } probe) return;
        var door = _context.RouteDoor(_state.Reference, contact.Collider, false);
        if (door.Reference is not { } reference) return;
        _routeDoor = new(contact, reference, door.Portal) { Error = door.Error };
        _pursuitPath = door.Error is null ? probe.Approach : [];
        _pursuitCursor = 0;
        _waypointDistance = float.PositiveInfinity;
        _routeStall = 0;
        _routeError = door.Error;
        if (door.Portal && door.Error is null)
            GD.Print($"OPENNV_NATIVE_PORTAL_APPROACH reference={_state.Reference} door={reference} waypoints={_pursuitPath.Length} owner=collision-validated-parent-package-approach");
    }

    // Returns ownership while approaching, awaiting the source event/motion,
    // or retaining a refusal. A request is never interpreted as open collision.
    private bool AdvanceDoorNavigation(double delta)
    {
        var procedure = _routeDoor!;
        var door = _context!.RouteDoor!(_state.Reference, procedure.Contact.Collider, false);
        if (door.Reference != procedure.Reference || door.Portal != procedure.Portal || door.Error is not null)
            procedure.Error ??= door.Error ?? "Route door collision lost its resident source binding.";
        if (procedure.Portal)
        {
            if (procedure.Error is not null) { _routeError = procedure.Error; _pursuitPath = []; return true; }
            if (_pursuitCursor < _pursuitPath.Length && _routeStall >= .75)
            {
                procedure.Error = _routeError = "NPC cannot reach the collision-validated portal approach.";
                _pursuitPath = [];
            }
            // Walk only the capsule-validated prefix up to the portal contact.
            // Its owning Travel/Eat/Sleep procedure checks actual interaction
            // reach and visibility each frame and commits the normal source
            // activation. Never wait for an XTEL door to animate open, or
            // manufacture an ordinary-door activation/completion here.
            return true;
        }
        if (door.Open && !door.Moving && !door.Pending && door.Error is null)
        {
            ResetDoorNavigation();
            return false; // A fresh capsule route must still prove clearance.
        }
        if (procedure.Error is not null) { _routeError = procedure.Error; _pursuitPath = []; return true; }
        if (_pursuitCursor < _pursuitPath.Length)
        {
            if (_routeStall >= .75) procedure.Error = "NPC cannot reach the collision-validated route-door approach.";
            return true;
        }
        if (!procedure.Requested)
        {
            if (door.Pending || door.Moving)
            {
                procedure.Seconds += delta;
                if (procedure.Seconds > 8) procedure.Error = "The route door's existing activation or source motion did not settle within the bounded wait.";
                return true;
            }
            // Repeat the physical contact at the actual controller position.
            // The saved search contact alone never authorizes a remote action.
            var contact = NativeCapsuleNavigation.FirstCorridorContact(_mover!, _actor.GlobalPosition, [procedure.Contact.Desired]);
            var offset = procedure.Contact.Desired - _actor.GlobalPosition; offset.Y = 0;
            if (contact is null || contact.Collider != procedure.Contact.Collider &&
                _context.CollisionReference?.Invoke(contact.Collider) != procedure.Reference || offset.Length() > _radius * 3)
            {
                procedure.Error = "Route-door contact changed before the NPC reached activation range.";
                return true;
            }
            door = _context.RouteDoor(_state.Reference, contact.Collider, true);
            procedure.Requested = true;
            procedure.Seconds = 0;
            if (door.Admitted) _doorActivations++;
            if (door.Error is not null) procedure.Error = door.Error;
        }
        procedure.Seconds += delta;
        if (procedure.Seconds > 8)
            procedure.Error = "NPC route-door activation did not produce a settled open source door within the bounded wait.";
        return true;
    }
}
