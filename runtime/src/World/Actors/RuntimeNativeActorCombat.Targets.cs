using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

internal sealed partial class RuntimeNativeActorCombat
{
    private RuntimeNativeActorCombat? _opponent;
    private IEnumerable<RuntimeNativeActorCombat> CombatActors => _actor.GetTree().GetNodesInGroup(CombatActorsGroup)
        .OfType<RuntimeNativeActorCombat>().Where(actor => ReferenceEquals(actor._world, _world) &&
            ReferenceEquals(actor._context, _context) && !actor.IsQueuedForDeletion());

    private Vector3 TargetPosition(RuntimeNativePlayer player) => _opponent?._actor.GlobalPosition ?? player.GlobalPosition;
    private float TargetRadius(RuntimeNativePlayer player) => _opponent?._radius ?? player.CombatRadius;
    private float TargetHealth(RuntimeNativePlayer player) => _opponent is { } actor
        ? _world.Health(actor._state.Reference).Current : _context!.Vitals().ExactHitPoints;
    private Vector3 TargetPoint(RuntimeNativePlayer player) => _opponent?.BodyTargetPoint() ?? player.CombatTargetPoint;

    private Vector3 BodyTargetPoint()
    {
        var part = _world.BodyParts(_state.Reference).Parts.Single(value => value.Type == 0);
        // BPNT is the aiming target. BPNN can be a broad damage-region root
        // (for humanoid torso it is Bip01, below the visible chest).
        var bone = _skeleton.BoneIndex(string.IsNullOrEmpty(part.Target) ? part.Node : part.Target);
        if (bone < 0) throw new NotSupportedException("Combat target has no source torso bone.");
        return (_skeleton.Node.GlobalTransform * _skeleton.Node.GetBoneGlobalPose(bone)).Origin;
    }

    private bool ResolveTarget(RuntimeNativePlayer player)
    {
        var target = _state.Engagement!.Target;
        _opponent = target == _records.RuntimeFormKey(0x14) ? null :
            CombatActors.SingleOrDefault(actor => actor._state.Reference == target);
        if (_opponent is null && target != _records.RuntimeFormKey(0x14)) return false;
        if ((_opponent is { } actor && (actor.Dead || !_world.IsEnabled(target))) || TargetHealth(player) <= 0)
        {
            EndEngagement();
            return false;
        }
        return true;
    }

    private void EndEngagement()
    {
        CaptureEngagement();
        _state.Engagement = null;
        Activity.SetCombat(false); Activity.SetAlerted(false); Activity.SetWeaponDrawn(false);
        Activity.SetMovement(false, false);
        if (_actor is CharacterBody3D body) body.Velocity = Vector3.Zero;
        _context?.DispatchEvent?.Invoke(_state.Reference, "OnCombatEnd");
        if (_actor is RuntimeNativeCreature creature) creature.EvaluatePackages(false);
    }

    private bool TryCompanionCombat(RuntimeNativePlayer player)
    {
        if (!_state.PlayerTeammate) return false;
        var playerKey = _records.RuntimeFormKey(0x14);
        var target = CombatActors.Where(actor => actor != this && !actor.Dead && actor._state.Enabled && !actor._state.PlayerTeammate &&
                (actor._state.Engagement?.Target == playerKey ||
                 actor._state.Engagement is { } engagement && CombatActors.Any(friend => friend._state.Reference == engagement.Target && friend._state.PlayerTeammate)))
            .OrderBy(actor => actor._actor.GlobalPosition.DistanceSquaredTo(_actor.GlobalPosition)).FirstOrDefault();
        if (target is null) return false;
        var threat = _threat ??= FalloutActorThreat.Read(_records, _state.Base, _state.Templates);
        if (_actor.GlobalPosition.DistanceTo(target._actor.GlobalPosition) > ThreatRadius(threat) ||
            !CanSeePoint(target.BodyTargetPoint(), target)) return false;
        _state.Engagement = new(target._state.Reference);
        _assistsReceived++;
        return true;
    }

    private bool CanSeeTarget(RuntimeNativePlayer player) => CanSeePoint(TargetPoint(player), _opponent);

    private int SightBone()
    {
        if (_actor is RuntimeNativeNpc) return _skeleton.BoneIndex("Bip01 Head");
        var parts = _world.BodyParts(_state.Reference).Parts;
        return _skeleton.BoneIndex((parts.SingleOrDefault(part => part.Type == 1) ?? parts.Single(part => part.Type == 0)).Node);
    }

    private bool CanSeePoint(Vector3 target, RuntimeNativeActorCombat? actor)
    {
        var head = SightBone();
        if (head < 0) throw new NotSupportedException("Actor sight requires its source head bone.");
        var eye = (_skeleton.Node.GlobalTransform * _skeleton.Node.GetBoneGlobalPose(head)).Origin;
        using var query = PhysicsRayQueryParameters3D.Create(eye, target, _mask, new(CollisionRids));
        using var hit = _actor.GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count == 0) return true;
        if (hit["collider"].AsGodotObject() is not Node collider) return false;
        // A sight ray ends inside the target's physical body. That first
        // contact is visibility, including when the target is the player.
        // Other actors and world contacts still occlude the target.
        if (actor is not null) return Find(collider) == actor;
        return _context?.Player() is { } player && (collider == player || player.IsAncestorOf(collider));
    }

    private Node? TargetContact(Vector3 origin)
    {
        if (_opponent is null) return null;
        using var query = PhysicsRayQueryParameters3D.Create(origin, _opponent.BodyTargetPoint(), _mask | _layer, new(CollisionRids));
        query.CollideWithAreas = true;
        using var hit = _actor.GetWorld3D().DirectSpaceState.IntersectRay(query);
        return hit.Count != 0 && hit["collider"].AsGodotObject() is Node collider && Find(collider) == _opponent ? collider : null;
    }

    private string? _friendlySpreadBlocker;

    private bool FriendlyInsideSpread(RuntimeNativePlayer player, Vector3 origin, Vector3 target, float medianDegrees)
    {
        _friendlySpreadBlocker = null;
        if (medianDegrees <= 0) return false;
        var displacement = target - origin;
        var length = displacement.Length();
        var halfAngle = Mathf.DegToRad(medianDegrees * 2);
        if (halfAngle >= MathF.PI / 2) { _friendlySpreadBlocker = "unbounded-spread"; return true; }
        const int sides = 16;
        // Circumscribe the complete possible source spread, before drawing any
        // shot randomness or consuming ammunition. The native actor volumes
        // decide overlap; a clear center ray alone cannot protect a nearby ally.
        var radius = length * MathF.Tan(halfAngle) / MathF.Cos(MathF.PI / sides);
        using var cone = new ConvexPolygonShape3D
        {
            Points = Enumerable.Range(0, sides).Select(index => new Vector3(
                radius * MathF.Cos(index * MathF.Tau / sides), radius * MathF.Sin(index * MathF.Tau / sides), -length))
                .Prepend(Vector3.Zero).ToArray()
        };
        var direction = displacement.Normalized();
        using var query = new PhysicsShapeQueryParameters3D
        {
            Shape = cone,
            Transform = new(Basis.LookingAt(direction, MathF.Abs(direction.Dot(Vector3.Up)) < .99f ? Vector3.Up : Vector3.Right), origin),
            // World geometry has its own muzzle-line test. Including it in
            // this bounded actor query can fill every result with scenery
            // and incorrectly hold all shots in a densely built cell.
            CollisionMask = _layer | player.CollisionLayer,
            CollideWithAreas = true,
            Exclude = new(CollisionRids)
        };
        var contacts = _actor.GetWorld3D().DirectSpaceState.IntersectShape(query, 128);
        if (contacts.Count == 128) { _friendlySpreadBlocker = "contact-query-saturated"; return true; }
        foreach (var hit in contacts)
        {
            if (hit["collider"].AsGodotObject() is not Node collider) continue;
            if ((collider == player || player.IsAncestorOf(collider)) && _state.PlayerTeammate)
            { _friendlySpreadBlocker = "player"; return true; }
            var other = Find(collider);
            if (other is null || other == this || other == _opponent || other.Dead || !_world.IsEnabled(other._state.Reference)) continue;
            if (_state.PlayerTeammate && other._state.PlayerTeammate || _world.ActorRelation(_state.Reference, other._state.Reference) >= 2)
            { _friendlySpreadBlocker = other._state.Reference.ToString(); return true; }
        }
        return false;
    }
}
