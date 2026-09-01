using Godot;

using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Interactions;

internal partial class MovingStaticInstance : RigidBody3D
{
    internal string ReferenceFormId { get; private set; } = "";
    internal string PhysicsSource { get; private set; } = "unsupported";
    internal string WorldForceSource { get; private set; } =
        "unsupported-owned-denominator-force-equation-unresolved";

    internal void Configure(
        string referenceFormId,
        VerifiedGltfLoader.DynamicBodyContract physics,
        PickupConfiguration configuration)
    {
        if (physics.Mass <= 0.0f || physics.Hulls.Count + physics.Spheres.Count == 0)
            throw new InvalidOperationException(
                $"Moving static dynamic body is incomplete: {referenceFormId}");

        ReferenceFormId = referenceFormId;
        Name = $"MOVING_STATIC_{referenceFormId}";
        Mass = physics.Mass;
        LinearDamp = physics.LinearDamping;
        AngularDamp = physics.AngularDamping;
        LinearDampMode = DampMode.Replace;
        AngularDampMode = DampMode.Replace;
        ContinuousCd = true;
        CollisionLayer = configuration.CollisionLayer;
        CollisionMask = configuration.CollisionMask;
        PhysicsMaterialOverride = new PhysicsMaterial
        {
            Friction = physics.Friction,
            Bounce = physics.Restitution,
        };

        foreach (var hull in physics.Hulls)
        {
            var points = hull.PointsGodotGameUnits.ToArray();
            if (points.Length < 4)
                throw new InvalidOperationException(
                    $"Moving static convex hull is incomplete: {referenceFormId}");
            AddChild(new CollisionShape3D
            {
                Name = "AuthoredDynamicHull",
                Shape = new ConvexPolygonShape3D
                {
                    Points = points,
                    Margin = 0.0f,
                },
            });
        }
        foreach (var sphere in physics.Spheres)
        {
            if (!float.IsFinite(sphere.RadiusGameUnits) || sphere.RadiusGameUnits <= 0.0f)
                throw new InvalidOperationException(
                    $"Moving static sphere is incomplete: {referenceFormId}");
            AddChild(new CollisionShape3D
            {
                Name = "AuthoredDynamicSphere",
                Shape = new SphereShape3D { Radius = sphere.RadiusGameUnits },
            });
        }

        SetMeta("opennv_collision_havok_layer", physics.Layer);
        SetMeta("opennv_collision_flags_and_part_number", physics.FlagsAndPartNumber);
        SetMeta("opennv_collision_unknown_short", physics.UnknownShort);
        SetMeta("opennv_collision_motion_system", physics.MotionSystem);
        SetMeta("opennv_collision_quality_type", physics.QualityType);
        SetMeta("opennv_collision_transform_policy", physics.ShapeTransformPolicy);
        SetMeta(
            "opennv_collision_source_body_translation_havok_units",
            physics.SourceBodyTranslationHavokUnits);
        SetMeta("opennv_collision_source_body_rotation", physics.SourceBodyRotation);
        SetMeta("opennv_world_force_source", WorldForceSource);
        if (physics.Spheres.Count == 1)
        {
            SetMeta("opennv_collision_shape_block", physics.Spheres[0].ShapeBlock);
            SetMeta(
                "opennv_collision_radius_havok_units",
                physics.Spheres[0].RadiusHavokUnits);
            SetMeta(
                "opennv_collision_radius_game_units",
                physics.Spheres[0].RadiusGameUnits);
        }
        PhysicsSource = $"owned-nif-{physics.ShapeType}";
    }
}
