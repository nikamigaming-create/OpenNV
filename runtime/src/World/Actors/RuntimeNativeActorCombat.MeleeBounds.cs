using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.World.Actors;

internal sealed partial class RuntimeNativeActorCombat
{
    private FalloutNifBound? _meleeBound;
    private float? _creatureReachGameUnits;
    // The native scalar is model scale times logical reference scale. A yaw
    // matrix's extracted axis length is rounded presentation, not SetScale.
    private float LogicalActorScale => (_actor switch
    {
        RuntimeNativeNpc npc => npc.Appearance.Height,
        RuntimeNativeCreature creature => creature.Appearance.BaseScale,
        _ => throw new NotSupportedException("Actor bounds have no source model-scale owner.")
    }) * (_state.ActorReferenceScale ?? throw new NotSupportedException("Actor bounds have no source reference-scale owner."));
    private float CurrentAttackRange => _creatureReachGameUnits is { } reach
        ? FalloutActorMeleeBounds.NaturalReach(reach, LogicalActorScale, _skeleton.UnitsToMetres)
        : _attackRange;

    internal Vector3 DetectionQuarterHeightPoint
    {
        get
        {
            var bound = _meleeBound ??= FalloutActorMeleeBounds.Read(_skeleton.Source);
            var height = FalloutActorMeleeBounds.VerticalSpan(bound.Center, bound.Dimensions,
                LogicalActorScale, _skeleton.UnitsToMetres);
            return _actor.GlobalPosition + Vector3.Up * (height / 4);
        }
    }

    internal System.Numerics.Vector3[] DetectionSightPoints
    {
        get
        {
            var bound = _meleeBound ??= FalloutActorMeleeBounds.Read(_skeleton.Source);
            var height = FalloutActorMeleeBounds.VerticalSpan(bound.Center, bound.Dimensions,
                LogicalActorScale, _skeleton.UnitsToMetres);
            var root = _actor.GlobalPosition;
            return FalloutActorSight.Points(new(root.X, root.Y, root.Z), height, Target(1), Target(0));

            System.Numerics.Vector3? Target(byte type)
            {
                var part = _world.BodyParts(_state.Reference).Parts.SingleOrDefault(value => value.Type == type);
                if (part is null || string.IsNullOrEmpty(part.Target)) return null;
                if (!_skeleton.TryBoneIndex(part.Target, out var bone)) return null;
                var point = (_skeleton.Node.GlobalTransform * _skeleton.Node.GetBoneGlobalPose(bone)).Origin;
                return new(point.X, point.Y, point.Z);
            }
        }
    }

    private float MeleeSurfaceExtent
    {
        get
        {
            var bound = _meleeBound ??= FalloutActorMeleeBounds.Read(_skeleton.Source);
            return FalloutActorMeleeBounds.MaximumY(bound.Center, bound.Dimensions,
                LogicalActorScale, _skeleton.UnitsToMetres);
        }
    }
}
