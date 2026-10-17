using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Physics;

internal sealed record FalloutLivingRagdollSkeletonSnapshot(FalloutReferenceModelDataInvocation Load,
    Guid Instance, FalloutRagdollSkeletonDescriptor Descriptor, bool Cached, FalloutNifSceneKey Parent,
    IReadOnlyList<ulong> NativeBodies, IReadOnlyList<ulong> NativeJoints);

internal sealed partial class FalloutLivingActorModel
{
    private FalloutLivingRagdollSkeletonSnapshot? _physicalSkeleton;
    internal FalloutLivingRagdollSkeletonSnapshot? CapturePhysicalSkeleton() => _physicalSkeleton;
    internal FalloutLivingRagdollSkeletonSnapshot InitializePhysicalSkeleton(FalloutReferenceModelDataInvocation load,
        FalloutActorLogicalScene graph, string key, FalloutRagdollDescriptorCache cache,
        Action<FalloutReferenceModelDataInvocation> requireActual)
    {
        RequireCurrent(); requireActual(load);
        graph.RequireRagdollTemplateWorld(load);
        if (_physicalSkeleton is not null || _ragdollBinding?.Capture() is not { Returned: true } binding || binding.Load != load ||
            _ragdollTemplate is not { Failure: null, Restored: false } || _inputs is null || !ReferenceEquals(Source.LogicalScene, graph))
            throw new InvalidOperationException("Ragdoll skeleton construction lost its actual current model, body binding or temporary pose.");
        var current = graph.Objects.ToDictionary(node => node.Key);
        var root = current[binding.Skeleton.CollisionRoot];
        if (root.Parent is not { } parent) throw new NotSupportedException("source-ragdoll-first-collision-parent-null-initialization-unowned");
        var subtree = new HashSet<FalloutNifSceneKey>(); var pending = new Stack<FalloutNifSceneObject>(); pending.Push(root);
        var bodies = new List<FalloutLivingPhysicalBody>(); var joints = new List<FalloutLivingPhysicalConstraint>();
        while (pending.TryPop(out var node))
        {
            Check();
            if (!subtree.Add(node.Key)) throw new InvalidDataException("Ragdoll physical subtree repeats a current source object.");
            if (node.Key.Asset == 0 && _scene[node.Key.Block].CollisionObject?.CurrentWrapper?.CurrentBody is { } body)
            {
                if (!body.Collision.IsBlend) throw new NotSupportedException("source-ragdoll-nonblend-physical-skeleton-collection-unowned");
                if (!body.IsWeaponObject)
                {
                    bodies.Add(body);
                    foreach (var linked in body.LinkedConstraints)
                        if (linked is FalloutLivingPhysicalConstraint actual) joints.Add(actual);
                        else if (linked is not null) throw new InvalidDataException("Ragdoll collector reached a foreign current joint wrapper.");
                }
            }
            for (var slot = node.Children.Count - 1; slot >= 0; --slot)
                if (node.Children[slot] is { } child) pending.Push(child);
        }
        var order = FalloutRagdollPhysicalOrder.Build(bodies.Select(body => body.Source.Block.Index).ToArray(),
            joints.Select(joint => new FalloutRagdollPhysicalLink(joint.Source.Header.Block.Index,
                joint.FirstBody.Source.Block.Index, joint.SecondBody.Source.Block.Index)).ToArray());
        var selected = bodies.ToDictionary(body => body.Source.Block.Index);
        var selectedJoints = joints.ToDictionary(joint => joint.Source.Header.Block.Index);
        var (descriptor, cached) = cache.Acquire(key, Source.Skeleton.Source.Sha256, Construct);
        if (!descriptor.Bones.Select(bone => bone.Body).SequenceEqual(order.Bodies) ||
            !descriptor.Bones.Select(bone => bone.Parent).SequenceEqual(order.Parents) || !descriptor.Constraints.SequenceEqual(order.Constraints))
            throw new InvalidDataException("Shared ragdoll skeleton differs from the actual ordered body/parent/constraint graph.");
        Check();
        return _physicalSkeleton = new(load, Guid.NewGuid(), descriptor, cached, parent.Key,
            order.Bodies.Select(index => selected[index].Backend.PhysicalObjectId).ToArray(),
            order.Constraints.Select(index => selectedJoints[index].Backend.PhysicalObjectId).ToArray());

        FalloutRagdollSkeletonDescriptor Construct()
        {
            var bones = new List<FalloutRagdollPhysicalBone>();
            var worlds = new List<FalloutCharacterControllerProxyTransform>();
            for (var index = 0; index < order.Bodies.Count; ++index)
            {
                Check(); var body = selected[order.Bodies[index]]; body.RequireCurrent();
                var world = body.ReadTransform(); var parentIndex = order.Parents[index];
                worlds.Add(world);
                var pose = parentIndex < 0 ? world : Relative(worlds[parentIndex], world);
                var scale = 0x3f800000u;
                if (index == 0)
                {
                    // The following source child replaces the root reference
                    // pose with the current scene-local frame, including the
                    // actual bhkRigidBodyT attachment when present.
                    var target = current[binding.Skeleton.Bones.Single(bone => bone.Body == body.Source.Block.Index).Scene];
                    pose = FalloutLivingModelConstruction.ProjectCurrentWorld(target.Local, body.Source, _inputs!,
                        new(IdentityTransform(), 0x3f800000));
                    scale = BitConverter.SingleToUInt32Bits(target.Local.Scale);
                }
                bones.Add(new(body.Source.Block.Index, body.Name, parentIndex, pose, scale));
            }
            return new(Guid.NewGuid(), key, Source.Skeleton.Source.Sha256, bones.AsReadOnly(), order.Constraints);
        }
        void Check() { RequireCurrent(); requireActual(load); }
    }

    private static FalloutCharacterControllerProxyTransform IdentityTransform() => new(new(
        FalloutPhysicalVector3.FromValues(1, 0, 0), FalloutPhysicalVector3.FromValues(0, 1, 0), FalloutPhysicalVector3.FromValues(0, 0, 1)), default);
    private static FalloutCharacterControllerProxyTransform Relative(FalloutCharacterControllerProxyTransform parent,
        FalloutCharacterControllerProxyTransform child)
    {
        var p = parent.Rotation;
        var inverse = new FalloutPhysicalRotation(new(p.Column0.X, p.Column1.X, p.Column2.X),
            new(p.Column0.Y, p.Column1.Y, p.Column2.Y), new(p.Column0.Z, p.Column1.Z, p.Column2.Z));
        var c = child.Rotation;
        var delta = FalloutPhysicalVector3.FromValues(FalloutPhysicalFloat32.Subtract(child.Translation.XValue, parent.Translation.XValue),
            FalloutPhysicalFloat32.Subtract(child.Translation.YValue, parent.Translation.YValue),
            FalloutPhysicalFloat32.Subtract(child.Translation.ZValue, parent.Translation.ZValue));
        return new(new(Rotate(c.Column0), Rotate(c.Column1), Rotate(c.Column2)), Rotate(delta));
        FalloutPhysicalVector3 Rotate(FalloutPhysicalVector3 value) => FalloutCharacterControllerTransformState.RotateOffset(value, inverse);
    }
}
