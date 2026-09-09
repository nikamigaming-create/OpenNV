using Godot;

using OpenNV.Runtime.SceneGraph;

namespace OpenNV.Runtime.World.Cells;

internal static class GamebryoReferenceEnableRuntime
{
    private const string CollisionLayerMeta = "opennv_enabled_collision_layer";
    private const string CollisionMaskMeta = "opennv_enabled_collision_mask";
    private const string EnabledMeta = "opennv_reference_enabled";

    // Runtime state (for example death) can change a body's intended filters
    // after the reference was registered. Keep those changes across Disable /
    // Enable and respect a disabled owner when a new body is attached later.
    internal static void SetCollisionFilter(CollisionObject3D collision, uint layer, uint mask)
    {
        collision.SetMeta(CollisionLayerMeta, layer);
        collision.SetMeta(CollisionMaskMeta, mask);
        var enabled = true;
        for (Node? node = collision; node is not null; node = node.GetParent())
            if (node.HasMeta(EnabledMeta) && !node.GetMeta(EnabledMeta).AsBool()) { enabled = false; break; }
        collision.CollisionLayer = enabled ? layer : 0;
        collision.CollisionMask = enabled ? mask : 0;
    }

    internal static void Apply(Node3D reference, bool enabled)
    {
        reference.SetMeta(EnabledMeta, enabled);
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
