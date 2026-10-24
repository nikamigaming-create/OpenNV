using System.Runtime.ExceptionServices;
using Godot;

using OpenNV.Runtime.SceneGraph;

namespace OpenNV.Runtime.World.Cells;

// The owner checks authoritative world state; presentation staging alone
// must not retire gameplay. This hook runs while processing is still live.
internal interface IGamebryoReferenceEnableLifecycle
{
    void BeforeReferenceDisabled();
}

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
        ExceptionDispatchInfo? failure = null;
        CollisionObject3D[]? collisions = null;
        void Attempt(Action action)
        {
            try { action(); }
            catch (Exception error) { failure ??= ExceptionDispatchInfo.Capture(error); }
        }

        try
        {
            // Snapshot the live owners before cancellation can retire children.
            collisions = NodeTraversal.SelfAndDescendants<CollisionObject3D>(reference).ToArray();
            if (!enabled)
                foreach (var owner in NodeTraversal.SelfAndDescendants<Node>(reference)
                    .OfType<IGamebryoReferenceEnableLifecycle>().ToArray())
                    Attempt(owner.BeforeReferenceDisabled);
        }
        catch (Exception error) { failure ??= ExceptionDispatchInfo.Capture(error); }
        finally
        {
            // Gameplay authority has already changed. A failed retirement must
            // retain its fault, not leave visible, processing or colliding state.
            Attempt(() => reference.SetMeta(EnabledMeta, enabled));
            Attempt(() => reference.Visible = enabled);
            Attempt(() => reference.ProcessMode = enabled
                ? Node.ProcessModeEnum.Inherit : Node.ProcessModeEnum.Disabled);
            Attempt(() =>
            {
                collisions ??= NodeTraversal.SelfAndDescendants<CollisionObject3D>(reference).ToArray();
                foreach (var collision in collisions)
                {
                    Attempt(() =>
                    {
                        if (!collision.HasMeta(CollisionLayerMeta))
                            collision.SetMeta(CollisionLayerMeta, collision.CollisionLayer);
                        if (!collision.HasMeta(CollisionMaskMeta))
                            collision.SetMeta(CollisionMaskMeta, collision.CollisionMask);
                        SetCollisionFilter(collision, collision.GetMeta(CollisionLayerMeta).AsUInt32(),
                            collision.GetMeta(CollisionMaskMeta).AsUInt32());
                    });
                    if (!enabled)
                    {
                        // Still disable both filters if metadata publication
                        // failed. Preserve the first fault for the caller.
                        Attempt(() => collision.CollisionLayer = 0);
                        Attempt(() => collision.CollisionMask = 0);
                    }
                }
            });
        }
        failure?.Throw();
    }
}
