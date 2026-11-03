using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

internal sealed partial class RuntimeNativeActorCombat
{
    internal static IEnumerable<RuntimeNativeActorCombat> PlayerMeleeTargets(RuntimeNativePlayer player)
    {
        var nodes = player.GetTree().GetNodesInGroup(CombatActorsGroup);
        using var lifetime = (Godot.Collections.Array)nodes;
        foreach (var node in nodes)
            if (node is RuntimeNativeActorCombat actor && !actor.IsQueuedForDeletion() && actor.EffectiveEnabled &&
                actor._context is { } context && context.Player() == player && context.Resident(actor._actor.GlobalPosition)) yield return actor;
    }

    internal FalloutMeleeActorGeometry PlayerMeleeGeometry
    {
        get
        {
            var bound = _meleeBound ??= FalloutActorMeleeBounds.Read(_skeleton.Source);
            var root = _actor.GlobalPosition;
            // Native vertical-overlap tests use process-local min/max bounds;
            // the independent cached maximum-Y extent carries actor scale.
            return new(new(root.X, root.Y, root.Z), MeleeSurfaceExtent,
                (bound.Center.Z - bound.Dimensions.Z) * _skeleton.UnitsToMetres,
                (bound.Center.Z + bound.Dimensions.Z) * _skeleton.UnitsToMetres, Activity.Swimming);
        }
    }

    internal (Node Collider, Vector3 Point, Vector3 Normal)? PlayerMeleeContact(Vector3 origin, Godot.Collections.Array<Rid> exclusions)
    {
        var torso = BodyTargetPoint();
        foreach (var point in DetectionSightPoints.Prepend(new(torso.X, torso.Y, torso.Z)))
        {
            using var query = PhysicsRayQueryParameters3D.Create(origin, new(point.X, point.Y, point.Z), _layer, exclusions);
            query.CollideWithAreas = true; query.CollideWithBodies = _ragdoll?.Active == true; query.HitFromInside = true;
            using var hit = _actor.GetWorld3D().DirectSpaceState.IntersectRay(query);
            if (hit.Count != 0 && hit["collider"].AsGodotObject() is Node collider && Find(collider) == this)
                return (collider, hit["position"].AsVector3(), hit["normal"].AsVector3());
        }
        return null;
    }
}
