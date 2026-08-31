using System.Text.Json;
using Godot;

using OpenNV.Runtime.SceneGraph;

namespace OpenNV.Runtime.World.Actors;

internal sealed class ActorPoseContract
{
    private const string FacingDerivation =
        "owned-gltf-animated-direct-skeleton-child-ancestor-of-all-skin-joints";
    private const string IdentityFacingDerivation =
        "owned-gltf-has-no-full-body-root-rotation-channel";

    private readonly Skeleton3D? _facingSkeleton;
    private readonly int _facingBoneIndex;
    private readonly Node3D? _facingNode;
    private readonly Skeleton3D? _headSkeleton;
    private readonly int _headBoneIndex;
    private readonly Node3D? _headNode;

    private ActorPoseContract(
        string skeletonRootNode,
        string headNode,
        string? facingNode,
        int skeletonRootNodeIndex,
        int headNodeIndex,
        int? facingNodeIndex,
        Skeleton3D? facingSkeleton,
        int facingBoneIndex,
        Node3D? facingRuntimeNode,
        Skeleton3D? headSkeleton,
        int headBoneIndex,
        Node3D? headRuntimeNode)
    {
        SkeletonRootNode = skeletonRootNode;
        HeadNode = headNode;
        FacingNode = facingNode;
        SkeletonRootNodeIndex = skeletonRootNodeIndex;
        HeadNodeIndex = headNodeIndex;
        FacingNodeIndex = facingNodeIndex;
        _facingSkeleton = facingSkeleton;
        _facingBoneIndex = facingBoneIndex;
        _facingNode = facingRuntimeNode;
        _headSkeleton = headSkeleton;
        _headBoneIndex = headBoneIndex;
        _headNode = headRuntimeNode;
    }

    internal string SkeletonRootNode { get; }

    internal string HeadNode { get; }

    internal string? FacingNode { get; }

    internal int SkeletonRootNodeIndex { get; }

    internal int HeadNodeIndex { get; }

    internal int? FacingNodeIndex { get; }

    internal string FacingSource => FacingNode is null
        ? IdentityFacingDerivation
        : FacingDerivation;

    internal string FacingRuntimeSource => FacingNode is null
        ? "identity"
        : _facingSkeleton is null
            ? "owned-gltf-runtime-node"
            : "owned-gltf-skeleton-bone";

    internal string HeadSource => _headSkeleton is null
        ? "owned-gltf-runtime-node"
        : "owned-gltf-skeleton-bone";

    internal static ActorPoseContract Load(
        JsonElement sidecar,
        string gltfPath,
        Node3D actorRoot,
        IReadOnlyList<Skeleton3D> skeletons)
    {
        var skeleton = sidecar.GetProperty("skeleton");
        var skeletonRootName = RequireText(skeleton, "rootNode", gltfPath);
        var headName = RequireText(skeleton, "bipedHeadNode", gltfPath);
        using var gltf = JsonDocument.Parse(File.ReadAllText(gltfPath));
        var source = gltf.RootElement;
        var nodes = source.GetProperty("nodes").EnumerateArray().ToArray();
        var parents = BuildParentIndex(nodes, gltfPath);
        var skins = source.GetProperty("skins").EnumerateArray().ToArray();
        if (skins.Length < 1)
            throw new InvalidOperationException(
                $"Owned actor glTF has no skin contract: {gltfPath}");
        var skeletonRoots = skins
            .Select(value => value.GetProperty("skeleton").GetInt32())
            .Distinct()
            .ToArray();
        if (skeletonRoots.Length != 1)
            throw new InvalidOperationException(
                $"Owned actor glTF skins disagree on their skeleton root: {gltfPath}");
        var skeletonRootIndex = skeletonRoots[0];
        ValidateNodeIndex(skeletonRootIndex, nodes.Length, gltfPath);
        if (!NodeName(nodes[skeletonRootIndex]).Equals(
                skeletonRootName,
                StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Owned actor sidecar and glTF disagree on skeleton root {skeletonRootName}: {gltfPath}");

        var skinJoints = skins
            .SelectMany(value => value.GetProperty("joints").EnumerateArray())
            .Select(value => value.GetInt32())
            .Distinct()
            .ToArray();
        if (skinJoints.Length < 1)
            throw new InvalidOperationException(
                $"Owned actor glTF skins contain no joints: {gltfPath}");
        foreach (var joint in skinJoints)
        {
            ValidateNodeIndex(joint, nodes.Length, gltfPath);
            if (!IsAncestorOrSelf(skeletonRootIndex, joint, parents))
                throw new InvalidOperationException(
                    $"Owned actor glTF skin joint {joint} is outside its skeleton root: {gltfPath}");
        }

        var headMatches = Enumerable.Range(0, nodes.Length)
            .Where(index =>
                NodeName(nodes[index]).Equals(headName, StringComparison.Ordinal) &&
                IsAncestorOrSelf(skeletonRootIndex, index, parents))
            .ToArray();
        if (headMatches.Length != 1)
            throw new InvalidOperationException(
                $"Owned actor glTF resolves head node {headName} to {headMatches.Length} skeleton nodes: {gltfPath}");
        var headNodeIndex = headMatches[0];

        var animatedRotationNodes = source.GetProperty("animations")
            .EnumerateArray()
            .SelectMany(animation => animation.GetProperty("channels").EnumerateArray())
            .Where(channel => channel.GetProperty("target").GetProperty("path").GetString() == "rotation")
            .Select(channel => channel.GetProperty("target").GetProperty("node").GetInt32())
            .Distinct()
            .ToHashSet();
        var facingCandidates = Enumerable.Range(0, nodes.Length)
            .Where(index =>
                parents[index] == skeletonRootIndex &&
                animatedRotationNodes.Contains(index) &&
                skinJoints.All(joint => IsAncestorOrSelf(index, joint, parents)))
            .ToArray();
        if (facingCandidates.Length > 1)
            throw new InvalidOperationException(
                $"Owned actor glTF has multiple full-body facing nodes: {gltfPath}");
        var facingNodeIndex = facingCandidates.Length == 1
            ? facingCandidates[0]
            : (int?)null;
        var facingName = facingNodeIndex is int resolvedFacingIndex
            ? NodeName(nodes[resolvedFacingIndex])
            : null;

        Skeleton3D? facingSkeleton = null;
        var facingBoneIndex = -1;
        Node3D? facingRuntimeNode = null;
        if (facingName is not null)
        {
            var boneMatches = FindBoneMatches(skeletons, facingName);
            var nodeMatches = NodeTraversal.Descendants<Node3D>(actorRoot)
                .Where(node => node is not Skeleton3D &&
                    node.Name.ToString().Equals(facingName, StringComparison.Ordinal))
                .ToArray();
            var facingIsSkinJoint = skinJoints.Contains(facingNodeIndex!.Value);
            if (facingIsSkinJoint && boneMatches.Length != 1)
                throw new InvalidOperationException(
                    $"Godot resolves owned skin-joint facing node {facingName} to " +
                    $"{boneMatches.Length} skeleton bones: {gltfPath}");
            if (!facingIsSkinJoint &&
                (boneMatches.Length != 0 || nodeMatches.Length != 1))
                throw new InvalidOperationException(
                    $"Godot resolves owned non-joint facing node {facingName} to " +
                    $"{boneMatches.Length} bones and {nodeMatches.Length} nodes: {gltfPath}");
            if (facingIsSkinJoint)
            {
                facingSkeleton = boneMatches[0].Skeleton;
                facingBoneIndex = boneMatches[0].BoneIndex;
            }
            else
                facingRuntimeNode = nodeMatches[0];
        }

        var headBoneMatches = FindBoneMatches(skeletons, headName);
        var headNodeMatches = NodeTraversal.Descendants<Node3D>(actorRoot)
            .Where(node => node is not Skeleton3D &&
                node.Name.ToString().Equals(headName, StringComparison.Ordinal))
            .ToArray();
        var headIsSkinJoint = skinJoints.Contains(headNodeIndex);
        if (headIsSkinJoint && headBoneMatches.Length != 1)
            throw new InvalidOperationException(
                $"Godot resolves owned skin-joint head {headName} to " +
                $"{headBoneMatches.Length} skeleton bones: {gltfPath}");
        if (!headIsSkinJoint &&
            (headBoneMatches.Length != 0 || headNodeMatches.Length != 1))
            throw new InvalidOperationException(
                $"Godot resolves owned non-joint head {headName} to " +
                $"{headBoneMatches.Length} bones and {headNodeMatches.Length} nodes: {gltfPath}");
        var headSkeleton = headIsSkinJoint
            ? headBoneMatches[0].Skeleton
            : null;
        var headBoneIndex = headIsSkinJoint
            ? headBoneMatches[0].BoneIndex
            : -1;
        var headRuntimeNode = headIsSkinJoint
            ? null
            : headNodeMatches[0];

        return new ActorPoseContract(
            skeletonRootName,
            headName,
            facingName,
            skeletonRootIndex,
            headNodeIndex,
            facingNodeIndex,
            facingSkeleton,
            facingBoneIndex,
            facingRuntimeNode,
            headSkeleton,
            headBoneIndex,
            headRuntimeNode);
    }

    internal Pose Resolve()
    {
        var facingRotation = _facingSkeleton is not null
            ? _facingSkeleton.GetBonePoseRotation(_facingBoneIndex).Normalized()
            : _facingNode is not null
                ? _facingNode.Quaternion.Normalized()
                : Quaternion.Identity;
        var headPosition = _headSkeleton is null
            ? _headNode!.GlobalPosition
            : _headSkeleton.ToGlobal(
                _headSkeleton.GetBoneGlobalPose(_headBoneIndex).Origin);
        if (!headPosition.IsFinite() ||
            !float.IsFinite(facingRotation.X) ||
            !float.IsFinite(facingRotation.Y) ||
            !float.IsFinite(facingRotation.Z) ||
            !float.IsFinite(facingRotation.W))
            throw new InvalidOperationException(
                "Owned actor pose produced a non-finite gallery framing value.");
        return new Pose(headPosition, facingRotation);
    }

    private static (Skeleton3D Skeleton, int BoneIndex)[] FindBoneMatches(
        IReadOnlyList<Skeleton3D> skeletons,
        string boneName) =>
        skeletons
            .Select(skeleton => (Skeleton: skeleton, BoneIndex: skeleton.FindBone(boneName)))
            .Where(value => value.BoneIndex >= 0)
            .ToArray();

    private static int[] BuildParentIndex(JsonElement[] nodes, string gltfPath)
    {
        var parents = Enumerable.Repeat(-1, nodes.Length).ToArray();
        for (var parent = 0; parent < nodes.Length; parent++)
        {
            if (!nodes[parent].TryGetProperty("children", out var children))
                continue;
            foreach (var childValue in children.EnumerateArray())
            {
                var child = childValue.GetInt32();
                ValidateNodeIndex(child, nodes.Length, gltfPath);
                if (parents[child] >= 0)
                    throw new InvalidOperationException(
                        $"Owned actor glTF node {child} has multiple parents: {gltfPath}");
                parents[child] = parent;
            }
        }
        return parents;
    }

    private static bool IsAncestorOrSelf(int ancestor, int node, IReadOnlyList<int> parents)
    {
        var current = node;
        while (current >= 0)
        {
            if (current == ancestor)
                return true;
            current = parents[current];
        }
        return false;
    }

    private static string NodeName(JsonElement node) =>
        node.TryGetProperty("name", out var name)
            ? name.GetString() ?? ""
            : "";

    private static string RequireText(JsonElement source, string property, string path)
    {
        if (!source.TryGetProperty(property, out var value) ||
            value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
            throw new InvalidOperationException(
                $"Owned actor sidecar has no skeleton.{property}: {path}");
        return value.GetString()!;
    }

    private static void ValidateNodeIndex(int index, int nodeCount, string path)
    {
        if (index < 0 || index >= nodeCount)
            throw new InvalidOperationException(
                $"Owned actor glTF node index {index} is outside {nodeCount} nodes: {path}");
    }

    internal readonly record struct Pose(
        Vector3 HeadWorldPosition,
        Quaternion FacingRotation);
}
