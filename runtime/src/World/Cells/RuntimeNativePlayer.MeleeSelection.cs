using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime.World.Cells;

internal partial class RuntimeNativePlayer
{
    private (Node Collider, Vector3 Point, Vector3 Normal)? SelectPlayerMeleeActor(FalloutWeaponPresentation weapon, Transform3D aim)
    {
        var extent = CombatMeleeExtent;
        var bound = _meleeSourceBound!;
        var attacker = new FalloutMeleeActorGeometry(new(GlobalPosition.X, GlobalPosition.Y, GlobalPosition.Z), extent,
            (bound.Center.Z - bound.Dimensions.Z) * UnitsToMeters,
            (bound.Center.Z + bound.Dimensions.Z) * UnitsToMeters, Activity.Swimming);
        // A nonnull presentation of the engine default is still empty hands.
        // Actual equipped hand-to-hand items retain their source WEAP reach.
        var intrinsic = _presentationInventory!.EquippedObject(_presentationRecords!, 5) is null;
        var scale = _thirdPerson!.Actor.Appearance.Height * _sourceScale;
        var reach = FalloutActorMeleeSelection.Reach(intrinsic, weapon.Reach,
            intrinsic ? FalloutGameSettingFloats.Read(_presentationRecords!, "fHandReachMult") : 0,
            FalloutGameSettingFloats.Read(_presentationRecords!, "fCombatDistance"), scale, UnitsToMeters);
        var cone = FalloutGameSettingFloats.Read(_presentationRecords!, "fCombatHitConeAngle");
        var deadMultiplier = FalloutGameSettingFloats.Read(_presentationRecords!, "fCombatDeadActorHitConeMult");
        var forward = _xr is null ? -GlobalBasis.Z : -aim.Basis.Z;
        var facing = new System.Numerics.Vector3(forward.X, forward.Y, forward.Z);
        RuntimeNativeActorCombat? selected = null;
        var bestAngle = float.MaxValue;
        foreach (var actor in RuntimeNativeActorCombat.PlayerMeleeTargets(this))
        {
            var target = actor.PlayerMeleeGeometry;
            if (FalloutActorMeleeSelection.Distance(attacker, target) > reach) continue;
            var offset = target.Position - attacker.Position;
            var angle = FalloutActorMeleeSelection.Angle(offset, facing);
            if (angle > bestAngle || !FalloutActorMeleeSelection.InCone(angle, offset.Length(), cone, UnitsToMeters, actor.Dead, deadMultiplier)) continue;
            selected = actor; bestAngle = angle;
        }
        // Native chooses the bearing candidate before checking player sight;
        // it does not try progressively farther targets through an occluder.
        if (selected is null || !PlayerMeleeLineOfSight(selected)) return null;
        return selected.PlayerMeleeContact(DetectionOrigin, SelfQueryBodies) ??
            throw new NotSupportedException("Selected melee actor has no actual source body-part contact.");
    }

    private bool PlayerMeleeLineOfSight(RuntimeNativeActorCombat actor)
    {
        var target = actor.PlayerMeleeGeometry.Position;
        var offset = new Vector3(target.X, target.Y, target.Z) - GlobalPosition;
        if (offset.Length() < 2 * UnitsToMeters) return true;
        var viewCone = FalloutGameSettingFloats.Read(_presentationRecords!, "fDetectionViewCone");
        if (!FalloutActorSight.InCone(new(offset.X, offset.Y, offset.Z),
            new(-GlobalBasis.Z.X, -GlobalBasis.Z.Y, -GlobalBasis.Z.Z), viewCone)) return false;
        return FalloutActorSight.AnyClear(actor.DetectionSightPoints, point =>
        {
            using var query = PhysicsRayQueryParameters3D.Create(DetectionOrigin, new(point.X, point.Y, point.Z),
                CollisionMask | CollisionLayer, SelfQueryBodies);
            using var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
            return hit.Count == 0 || hit["collider"].AsGodotObject() is Node collider && RuntimeNativeActorCombat.Find(collider) == actor;
        });
    }
}
