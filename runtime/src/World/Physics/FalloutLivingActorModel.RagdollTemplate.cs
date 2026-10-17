using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Physics;

internal sealed record FalloutRagdollCollisionFlagStore(int Collision, ushort Before, ushort After);
internal sealed record FalloutRagdollBodyPoseStore(int Body, ulong NativeBody, FalloutCharacterControllerProxyTransform Transform);
internal sealed record FalloutLivingRagdollTemplateSnapshot(FalloutReferenceModelDataInvocation Load,
    IReadOnlyList<FalloutRagdollCollisionFlagStore> Flags, bool Disabled, bool Restored, string? Failure);

internal sealed partial class FalloutLivingActorModel
{
    private FalloutLivingRagdollTemplateSnapshot? _ragdollTemplate;

    internal void PrepareRagdollTemplate(FalloutReferenceModelDataInvocation load,
        Action<FalloutReferenceModelDataInvocation> requireActual)
    {
        RequireCurrent(); requireActual(load);
        if (_ragdollTemplate is not null || _ragdollBinding?.Capture() is not { Returned: true } binding || binding.Load != load)
            throw new InvalidOperationException("Ragdoll template preparation lost its same actual bound body assembly.");
        var stores = new List<FalloutRagdollCollisionFlagStore>();
        var root = _bodies[binding.Skeleton.RootBody].CollisionProperty;
        var disabled = (root.Flags & 0x60) == 0x20;
        _ragdollTemplate = new(load, stores.AsReadOnly(), disabled, false, null);
        try
        {
            if (disabled)
                foreach (var scene in Source.Skeleton.Scene)
                {
                    Check();
                    if (_scene[scene.Declaration.Block.Index].CollisionObject is not { } collision) continue;
                    var before = collision.Flags; collision.SetRagdollBlendFlag(false);
                    stores.Add(new(collision.Source.Block.Index, before, collision.Flags)); Check();
                }
            if (_bodies.Values.Any(body => body.Motion != 6))
                throw new NotSupportedException("source-ragdoll-temporary-motion-conversion-unowned");
        }
        catch (Exception error) { _ragdollTemplate = _ragdollTemplate with { Failure = error.Message }; throw; }
        void Check() { requireActual(load); RequireCurrent(); }
    }

    internal FalloutRagdollBodyPoseStore? UpdateRagdollTemplateCollision(FalloutNifSceneObject node,
        FalloutPlayerCollisionModeRead mode, Action<FalloutReferenceModelDataInvocation> requireActual)
    {
        RequireCurrent(); requireActual(mode.Load);
        if (_ragdollTemplate is not { Restored: false, Failure: null } entered || entered.Load != mode.Load ||
            mode.Thread != Environment.CurrentManagedThreadId || _inputs is null || _placement is null)
            throw new InvalidOperationException("Ragdoll collision update lost its actual temporary pose/thread/placement.");
        if (node.Key.Asset != 0)
        {
            if (Source.Parts[node.Key.Asset - 1].Physical.FindSceneObject(node.Key.Block)?.Collision is not null)
                throw new NotSupportedException("source-ragdoll-part-current-collision-update-unowned");
            return null;
        }
        if (!_scene.TryGetValue(node.Key.Block, out var current) || current.Source.Declaration != node.Source)
            throw new InvalidDataException("Ragdoll world update selected another actual source object.");
        if (current.CollisionObject is not { } collision) return null;
        // The selected source thread mode zero and the returned model-motion
        // store select the full keyframed branch. With blend flag 0x20 cleared,
        // the collision child writes the current C# world frame to the body.
        if (mode.Mode != 0 || !collision.Source.IsBlend || (collision.Flags & 0x60) != 0 ||
            (collision.Flags & 1) == 0 || collision.ModelMotion != 6 ||
            collision.CurrentWrapper?.CurrentBody is not { Motion: 6 } body)
            throw new NotSupportedException("source-ragdoll-current-collision-world-update-branch-unowned");
        if (collision.Capture() is not { BlendWeight: 0x3f800000, BlendVelocityWeight: 0x3f800000 })
            throw new NotSupportedException("source-ragdoll-partial-current-collision-blend-unowned");
        var transform = FalloutLivingModelConstruction.ProjectCurrentWorld(node.World, body.Source, _inputs, _placement);
        body.Backend.PublishTransform(transform); requireActual(mode.Load); body.RequireCurrent();
        return new(body.Source.Block.Index, body.Backend.PhysicalObjectId, transform);
    }

    internal void RestoreRagdollTemplateFlags(FalloutReferenceModelDataInvocation load,
        Action<FalloutReferenceModelDataInvocation> requireActual)
    {
        RequireCurrent(); requireActual(load);
        if (_ragdollTemplate is not { Restored: false, Failure: null } call || call.Load != load)
            throw new InvalidOperationException("Ragdoll flag restore lost its actual entered constructor.");
        if (call.Disabled)
            foreach (var scene in Source.Skeleton.Scene)
            {
                requireActual(load);
                _scene[scene.Declaration.Block.Index].CollisionObject?.SetRagdollBlendFlag(true);
            }
        _ragdollTemplate = call with { Restored = true };
    }
}
