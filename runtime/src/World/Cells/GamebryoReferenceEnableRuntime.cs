using Godot;

using OpenNV.Runtime.SceneGraph;

namespace OpenNV.Runtime.World.Cells;

internal static class GamebryoReferenceEnableRuntime
{
    private const string CollisionLayerMeta = "opennv_enabled_collision_layer";
    private const string CollisionMaskMeta = "opennv_enabled_collision_mask";

    internal static void Apply(Node3D reference, bool enabled)
    {
        reference.Visible = enabled;
        reference.ProcessMode = enabled
            ? Node.ProcessModeEnum.Inherit
            : Node.ProcessModeEnum.Disabled;
        foreach (var collision in NodeTraversal.SelfAndDescendants<CollisionObject3D>(reference))
        {
            if (!collision.HasMeta(CollisionLayerMeta))
                collision.SetMeta(CollisionLayerMeta, collision.CollisionLayer);
            if (!collision.HasMeta(CollisionMaskMeta))
                collision.SetMeta(CollisionMaskMeta, collision.CollisionMask);
            collision.CollisionLayer = enabled
                ? collision.GetMeta(CollisionLayerMeta).AsUInt32()
                : 0u;
            collision.CollisionMask = enabled
                ? collision.GetMeta(CollisionMaskMeta).AsUInt32()
                : 0u;
        }
    }
}
