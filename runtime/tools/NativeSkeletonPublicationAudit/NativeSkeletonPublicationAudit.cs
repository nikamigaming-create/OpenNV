using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

public partial class NativeSkeletonPublicationAudit : Node
{
    public override void _Ready()
    {
        RuntimeNativeNifSkeleton? skeleton = null;
        try
        {
            var args = OS.GetCmdlineUserArgs();
            if (args.Length != 1) throw new ArgumentException("Skeleton publication audit needs an owned root.");
            RuntimeLiveContentSource.Configure(args[0], RuntimeLiveContentSource.FalloutNewVegasGame);
            using var content = RuntimeLiveContentSource.Current!;
            if (!content.TryRead("meshes/characters/_male/skeleton.nif", null, out var bytes, out _))
                throw new FileNotFoundException("Owned actor skeleton is absent.");
            skeleton = new(FalloutNifFile.Read(bytes), .01f);
            var node = skeleton.Node;
            // Seed the same global cache used by skin/contact assembly, then
            // publish different local animation poses while still off-tree.
            for (var bone = 0; bone < node.GetBoneCount(); bone++) _ = node.GetBoneGlobalPose(bone);
            for (var bone = 0; bone < node.GetBoneCount(); bone++) node.SetBonePose(bone, Transform3D.Identity);
            AddChild(node);
            for (var bone = 0; bone < node.GetBoneCount(); bone++)
                if (!node.GetBoneGlobalPose(bone).IsEqualApprox(Transform3D.Identity))
                    throw new InvalidOperationException($"Bone {node.GetBoneName(bone)} published stale construction/rest pose.");
            RemoveChild(node);
            var root = node.GetParentlessBones()[0];
            var moved = new Transform3D(Basis.Identity, new Vector3(.2f, .3f, .4f));
            node.SetBonePose(root, moved);
            AddChild(node);
            if (!node.GetBoneGlobalPose(root).IsEqualApprox(moved)) throw new InvalidOperationException("Reentry lost a retained off-tree pose.");
            GD.Print($"OPENNV_NATIVE_SKELETON_PUBLICATION_PASS bones={node.GetBoneCount()} initialCache=true authoredPoseRetained=true reentry=true pixels=unverified");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
        finally { skeleton?.Node.Free(); }
    }
}
