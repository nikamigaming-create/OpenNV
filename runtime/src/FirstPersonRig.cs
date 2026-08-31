using System.Security.Cryptography;
using Godot;

using OpenNV.Runtime.SceneGraph;

namespace OpenNV.Runtime;

internal static class FirstPersonRig
{
    internal const string Schema = "opennv-first-person-rig/v1";
    internal const string Status = "owned-data-skinned-hands";
    internal const string Provider = "retail-first-person-skinned-hands";

    internal static LoadedRig Attach(
        Contract contract,
        Node3D leftAnchor,
        Node3D rightAnchor,
        bool trackedHands,
        float unitsToMeters,
        RuntimeConfiguration configuration)
    {
        if (contract.Schema != Schema || contract.Status != Status || contract.Provider != Provider)
            throw new InvalidOperationException("Unexpected OpenNV first-person rig contract.");
        var left = AttachHand(
            contract.Left,
            leftAnchor,
            trackedHands ? contract.Left.GripBone : contract.CameraBone,
            unitsToMeters,
            configuration);
        var right = AttachHand(
            contract.Right,
            rightAnchor,
            trackedHands ? contract.Right.GripBone : contract.CameraBone,
            unitsToMeters,
            configuration);
        var weaponBone = FrameWorld(right.Root, right.Skeleton, contract.WeaponBone);
        return new LoadedRig(left, right, weaponBone);
    }

    private static LoadedHand AttachHand(
        HandContract contract,
        Node3D anchor,
        string alignmentBone,
        float unitsToMeters,
        RuntimeConfiguration configuration)
    {
        VerifyHash(contract.ModelPath, contract.ModelSha256);
        VerifyHash(contract.SidecarPath, contract.SidecarSha256);
        var loaded = ActorModelSlice.Load(
            contract.ModelPath,
            contract.SidecarPath,
            anchor,
            configuration,
            true,
            ActorModelSlice.BoundsContract.FirstPersonHand);
        loaded.AnimationPlayer.Seek(0.0, true);
        loaded.AnimationPlayer.Pause();
        var skeletons = NodeTraversal.Descendants<Skeleton3D>(loaded.Root).ToArray();
        if (skeletons.Length != 1)
            throw new InvalidOperationException(
                $"First-person hand must contain exactly one skeleton, found {skeletons.Length}.");
        var skeleton = skeletons[0];
        var boneWorld = FrameWorld(loaded.Root, skeleton, alignmentBone);
        var rootToBone = loaded.Root.GlobalTransform.AffineInverse() * boneWorld;
        var targetBasis = anchor.GlobalBasis.Orthonormalized().Scaled(Vector3.One * unitsToMeters);
        var targetBone = new Transform3D(targetBasis, anchor.GlobalPosition);
        loaded.Root.GlobalTransform = targetBone * rootToBone.AffineInverse();

        var aligned = FrameWorld(loaded.Root, skeleton, alignmentBone);
        var positionError = aligned.Origin.DistanceTo(anchor.GlobalPosition);
        var rotationError = aligned.Basis.Orthonormalized().GetRotationQuaternion().AngleTo(
            anchor.GlobalBasis.Orthonormalized().GetRotationQuaternion());
        if (positionError > configuration.Xr.HandAlignmentPositionToleranceMeters ||
            rotationError > configuration.Xr.HandAlignmentRotationToleranceRadians)
            throw new InvalidOperationException(
                $"First-person hand alignment failed: bone={alignmentBone} " +
                $"position={positionError:F6} rotation={rotationError:F6}");
        var visibleMeshes = NodeTraversal.Descendants<MeshInstance3D>(loaded.Root)
            .Count(mesh => mesh.Mesh is not null && mesh.Visible);
        if (visibleMeshes < 1)
            throw new InvalidOperationException($"First-person hand has no visible geometry: {alignmentBone}");
        return new LoadedHand(loaded.Root, skeleton, alignmentBone, visibleMeshes, positionError, rotationError);
    }

    private static Transform3D FrameWorld(Node3D root, Skeleton3D skeleton, string frameName)
    {
        var index = skeleton.FindBone(frameName);
        if (index >= 0)
            return skeleton.GlobalTransform * skeleton.GetBoneGlobalPose(index);
        var authoredFrames = NodeTraversal.Descendants<Node3D>(root)
            .Where(node => node.Name.ToString().Equals(frameName, StringComparison.Ordinal))
            .ToArray();
        if (authoredFrames.Length != 1)
            throw new InvalidOperationException(
                $"First-person rig requires one authored frame {frameName}, found {authoredFrames.Length}.");
        return authoredFrames[0].GlobalTransform;
    }

    private static void VerifyHash(string path, string expected)
    {
        var resolved = VerifiedGltfLoader.ResolvePath(path);
        using var stream = File.OpenRead(resolved);
        var actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"First-person rig hash mismatch: {resolved}");
    }

    internal sealed record Contract(
        string Schema,
        string Status,
        string Provider,
        string CameraBone,
        string WeaponBone,
        HandContract Left,
        HandContract Right);

    internal sealed record HandContract(
        string ModelPath,
        string SidecarPath,
        string ModelSha256,
        string SidecarSha256,
        string GripBone);

    internal readonly record struct LoadedRig(
        LoadedHand Left,
        LoadedHand Right,
        Transform3D WeaponBoneWorld);

    internal readonly record struct LoadedHand(
        Node3D Root,
        Skeleton3D Skeleton,
        string AlignmentBone,
        int VisibleMeshes,
        float PositionErrorMeters,
        float RotationErrorRadians);
}
