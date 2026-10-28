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
            if (!enabled)
            {
                // Both sets must be captured before a lifecycle callback can
                // retire children. Traverse the same live tree once rather
                // than asking Godot for every node's children twice during
                // a residency transition.
                var collisionSnapshot = new List<CollisionObject3D>();
                var lifecycleSnapshot = new List<IGamebryoReferenceEnableLifecycle>();
                foreach (var node in NodeTraversal.SelfAndDescendants<Node>(reference))
                {
                    if (node is CollisionObject3D collision) collisionSnapshot.Add(collision);
                    if (node is IGamebryoReferenceEnableLifecycle lifecycle) lifecycleSnapshot.Add(lifecycle);
                }
                collisions = collisionSnapshot.ToArray();
                foreach (var owner in lifecycleSnapshot)
                    Attempt(owner.BeforeReferenceDisabled);
            }
            else collisions = NodeTraversal.SelfAndDescendants<CollisionObject3D>(reference).ToArray();
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
                        // The disabled root is already an ancestor of every
                        // captured collision body. Its live filters must be
                        // zero regardless of any other enable ancestor; the
                        // independent fail-safe writes below enforce that.
                        // Keep the recorded intended filters for a later
                        // enable, without republishing them and walking the
                        // full ancestor chain for every retiring body.
                        var layer = collision.GetMeta(CollisionLayerMeta).AsUInt32();
                        var mask = collision.GetMeta(CollisionMaskMeta).AsUInt32();
                        if (enabled) SetCollisionFilter(collision, layer, mask);
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
