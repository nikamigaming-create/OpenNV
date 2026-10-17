using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Physics;

internal sealed record FalloutRagdollAnimationBone(string Name, int Parent, FalloutRagdollAnimationPose ReferencePose, bool LockTranslation);
internal sealed record FalloutRagdollAnimationDescriptor(Guid Identity, string Key, string Source, IReadOnlyList<FalloutRagdollAnimationBone> Bones)
{
    [System.Text.Json.Serialization.JsonInclude] public bool RootTranslationLocked { get; private set; } = true;
    [System.Text.Json.Serialization.JsonInclude] public long RootTranslationStores { get; private set; }
    internal void UnlockRootTranslation()
    {
        Validate();
        if (!RootTranslationLocked || RootTranslationStores != 0) throw new InvalidOperationException("Animation skeleton root translation store replayed.");
        RootTranslationLocked = false; RootTranslationStores = 1;
    }
    internal void Validate()
    {
        if (Identity == Guid.Empty || string.IsNullOrWhiteSpace(Key) || Source is not { Length: 64 } || !Source.All(Uri.IsHexDigit) || Bones is not { Count: > 0 } ||
            RootTranslationStores != (RootTranslationLocked ? 0 : 1))
            throw new InvalidDataException("Ragdoll animation skeleton lost its actual descriptor or source nodes.");
        foreach (var (bone, index) in Bones.Select((bone, index) => (bone, index)))
        {
            if (bone is null || string.IsNullOrWhiteSpace(bone.Name) || bone.Parent < -1 || bone.Parent >= index ||
                (index == 0) != (bone.Parent == -1) || !bone.LockTranslation || bone.ReferencePose is not { } pose ||
                !float.IsFinite(BitConverter.UInt32BitsToSingle(pose.Scale)) || BitConverter.UInt32BitsToSingle(pose.Scale) <= 0 ||
                !Finite(pose.Translation.XValue) || !Finite(pose.Translation.YValue) || !Finite(pose.Translation.ZValue) ||
                !Finite(pose.Rotation.W) || !Finite(pose.Rotation.X) || !Finite(pose.Rotation.Y) || !Finite(pose.Rotation.Z))
                throw new InvalidDataException("Ragdoll animation skeleton changed its ordered parent, lock or finite local reference pose.");
        }
        static bool Finite(float value) => float.IsFinite(value);
    }
}
internal sealed record FalloutLivingRagdollAnimationSnapshot(FalloutReferenceModelDataInvocation Load, Guid Identity,
    IReadOnlyList<FalloutNifSceneKey> Nodes, FalloutRagdollAnimationDescriptor? Descriptor, bool Cached,
    IReadOnlyList<FalloutRagdollAnimationPose> LocalPoses, IReadOnlyList<uint> BoneFlags, ushort PoseFlags,
    bool Returned, string? Failure, IReadOnlyList<FalloutRagdollAnimationBone>? Collected = null)
{
    internal void Validate(FalloutLivingRagdollSkeletonSnapshot physical)
    {
        if (Load != physical.Load || Identity == Guid.Empty || Nodes is null || LocalPoses is null || BoneFlags is null ||
            Nodes.Distinct().Count() != Nodes.Count || Returned == (Failure is not null) || Failure is not null && string.IsNullOrWhiteSpace(Failure))
            throw new InvalidDataException("Ragdoll animation instance lost its actual source load or entered prefix.");
        if (Descriptor is { } descriptor)
        {
            descriptor.Validate();
            if (descriptor.Key != physical.Descriptor.Key || descriptor.Source != physical.Descriptor.Source || descriptor.Bones.Count != Nodes.Count)
                throw new InvalidDataException("Ragdoll animation instance borrowed another shared descriptor.");
            if (Returned && (PoseFlags != 0x100 || BoneFlags.Count != Nodes.Count || BoneFlags.Any(value => value != 2) ||
                !LocalPoses.SequenceEqual(descriptor.Bones.Select(bone => bone.ReferencePose))))
                throw new InvalidDataException("Ragdoll animation pose changed its actual constructor reference copies or dirty flags.");
        }
        else if (Returned || Cached || LocalPoses.Count != 0 || BoneFlags.Count != 0 || PoseFlags != 0)
            throw new InvalidDataException("Ragdoll animation instance invented returned pose fields before its descriptor.");
        if (Collected is not null && (Collected.Count != Nodes.Count || Descriptor is { } selected &&
            !Collected.Select(bone => (bone.Name, bone.Parent)).SequenceEqual(selected.Bones.Select(bone => (bone.Name, bone.Parent)))))
            throw new InvalidDataException("Ragdoll animation omitted its source-ordered current node reference-pose prefix.");
    }
}

internal sealed partial class FalloutLivingActorModel
{
    private FalloutLivingRagdollAnimationSnapshot? _animationSkeleton;
    internal FalloutLivingRagdollAnimationSnapshot? CaptureAnimationSkeleton() => _animationSkeleton;

    internal void InitializeAnimationSkeleton(FalloutReferenceModelDataInvocation load, FalloutActorLogicalScene graph,
        FalloutRagdollDescriptorCache cache, Action<FalloutReferenceModelDataInvocation> requireActual)
    {
        RequireCurrent(); requireActual(load); graph.RequireRagdollTemplateWorld(load);
        if (_animationSkeleton is not null || _physicalSkeleton is not { } physical || physical.Load != load ||
            _ragdollDrive?.Capture() is not { Returned: true, Failure: null } || _ragdollArms is not { } arms || arms.Load != load ||
            !ReferenceEquals(graph, Source.LogicalScene))
            throw new InvalidOperationException("Ragdoll animation skeleton lost its same current physical drive, model or entered source scope.");
        var nodes = new List<FalloutNifSceneKey>(); var bones = new List<FalloutRagdollAnimationBone>();
        _animationSkeleton = new(load, Guid.NewGuid(), nodes.AsReadOnly(), null, false, [], [], 0, false, null, bones.AsReadOnly());
        try
        {
            var parent = graph.Objects.Single(node => node.Key == physical.Parent);
            var root = FirstCollision(parent) ?? throw new InvalidDataException("Ragdoll animation skeleton has no current first collision target.");
            var indices = new Dictionary<FalloutNifSceneKey, int>();
            Collect(root);
            var (descriptor, cached) = cache.AcquireAnimation(physical.Descriptor.Key, Source.Skeleton.Source.Sha256,
                () => new(Guid.NewGuid(), physical.Descriptor.Key, Source.Skeleton.Source.Sha256, bones.AsReadOnly()));
            _animationSkeleton = _animationSkeleton with { Descriptor = descriptor, Cached = cached };
            if (descriptor.Bones.Count != nodes.Count || descriptor.Bones.Where((bone, index) => bone.Name != bones[index].Name || bone.Parent != bones[index].Parent).Any())
                throw new InvalidDataException("Shared ragdoll animation skeleton changed its same current node names or parent order.");
            Check();
            // A fresh pose owns local reference copies and dirty flags. Its
            // model-space rows and following output buffer remain unwritten;
            // no identity/default poses are supplied for those allocations.
            _animationSkeleton = _animationSkeleton with
            {
                LocalPoses = descriptor.Bones.Select(bone => bone.ReferencePose).ToArray(),
                BoneFlags = Enumerable.Repeat(2u, nodes.Count).ToArray(),
                PoseFlags = 0x100,
                Returned = true
            };

            void Collect(FalloutNifSceneObject node)
            {
                Check();
                var parentIndex = node.Parent is { } actualParent && indices.TryGetValue(actualParent.Key, out var found) ? found : -1;
                var pose = FalloutRagdollAnimationPose.Read(node.Local, parentIndex < 0 ? 1 : parent.World.Scale);
                indices.Add(node.Key, nodes.Count); nodes.Add(node.Key);
                bones.Add(new(node.Name, parentIndex, pose, true));
                foreach (var child in node.Children)
                {
                    if (child is null) continue;
                    var collision = FirstCollision(child);
                    if (collision is null) continue;
                    if (collision.Key.Asset != 0 || !_scene.TryGetValue(collision.Key.Block, out var source))
                        throw new NotSupportedException("source-ragdoll-animation-part-collision-owner-unbound");
                    if (source.CollisionObject?.CurrentWrapper?.CurrentBody is not { } body)
                        throw new NotSupportedException("source-ragdoll-animation-nonbody-collision-owner-unbound");
                    if (!body.IsWeaponObject) Collect(child);
                }
            }
        }
        catch (Exception error) { _animationSkeleton = _animationSkeleton with { Failure = error.Message }; throw; }
        void Check() { RequireCurrent(); requireActual(load); }
        FalloutNifSceneObject? FirstCollision(FalloutNifSceneObject node)
        {
            Check();
            if (graph.ReadRagdollTemplateCollision(load, node, requireActual) is not null) return node;
            foreach (var child in node.Children)
                if (child is not null && FirstCollision(child) is { } found) return found;
            return null;
        }
    }
}
