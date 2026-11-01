using System.Runtime.CompilerServices;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.World.Actors;

internal static class NativeActorPosedBounds
{
    private sealed record Sphere(int Bone, Vector3 Center, float Radius);
    private sealed record Geometry(RuntimeNativeNifSkeleton Skeleton, IReadOnlyList<Sphere> Spheres);
    private static readonly ConditionalWeakTable<MeshInstance3D, Geometry> GeometryCache = new();
    private sealed record PartMeshes(string Path, IReadOnlyList<MeshInstance3D> Meshes);
    private static readonly ConditionalWeakTable<RuntimeNativeNifScene, PartMeshes> PartCache = new();

    internal static FalloutNifSphereBound Current(IReadOnlyList<RuntimeNativeNifScene> parts,
        RuntimeNativeNifSkeleton skeleton, RuntimeLiveContentSource content, IDictionary<string, FalloutNifFile> sources)
        => Sample(parts, skeleton, content, sources, null) ?? throw new InvalidDataException("Presented actor has no posed source bounds.");

    internal static FalloutNifSphereBound? CurrentBone(IReadOnlyList<RuntimeNativeNifScene> parts,
        RuntimeNativeNifSkeleton skeleton, RuntimeLiveContentSource content, IDictionary<string, FalloutNifFile> sources, int rootBone)
        => Sample(parts, skeleton, content, sources, rootBone);

    internal static FalloutNifSphereBound? CurrentMesh(IReadOnlyList<RuntimeNativeNifScene> parts,
        RuntimeNativeNifSkeleton skeleton, RuntimeLiveContentSource content, IDictionary<string, FalloutNifFile> sources,
        MeshInstance3D mesh) => Sample(parts, skeleton, content, sources, null, mesh);

    private static FalloutNifSphereBound? Sample(IReadOnlyList<RuntimeNativeNifScene> parts,
        RuntimeNativeNifSkeleton skeleton, RuntimeLiveContentSource content, IDictionary<string, FalloutNifFile> sources, int? rootBone,
        MeshInstance3D? selectedMesh = null)
    {
        FalloutNifSphereBound? result = null;
        // This synchronous sample cannot advance or publish the skeleton.
        // Read its world transform once, without retaining a pose across calls.
        Transform3D? skeletonTransform = null;
        foreach (var part in parts)
        {
            var prepared = PartCache.GetValue(part, scene => new(scene.Root.GetMeta("opennv_source_model").AsString(),
                scene.Root.FindChildren("*", "", true, false).OfType<MeshInstance3D>()
                    .Where(mesh => mesh.HasMeta("opennv_nif_geometry_block")).ToArray()));
            var path = prepared.Path;
            if (selectedMesh is not null && !prepared.Meshes.Contains(selectedMesh)) continue;
            if (!sources.TryGetValue(path, out var source))
            {
                if (!content.TryRead(path, null, out var bytes, out _)) throw new FileNotFoundException(path);
                sources.Add(path, source = FalloutNifFile.Read(bytes));
            }
            foreach (var mesh in prepared.Meshes)
            {
                if (selectedMesh is not null && mesh != selectedMesh) continue;
                if (!GodotObject.IsInstanceValid(mesh) || mesh.IsQueuedForDeletion())
                    throw new InvalidOperationException("Posed source bounds retain a retired part mesh.");
                if (!mesh.Visible) continue;
                var geometry = GeometryCache.GetValue(mesh, item => Prepare(item, source, skeleton));
                if (!ReferenceEquals(geometry.Skeleton, skeleton)) throw new InvalidOperationException("Posed bounds changed their skeleton owner.");
                foreach (var sphere in geometry.Spheres)
                {
                    if (rootBone is { } selected)
                    {
                        var ancestor = sphere.Bone;
                        while (ancestor >= 0 && ancestor != selected) ancestor = skeleton.Node.GetBoneParent(ancestor);
                        if (ancestor < 0) continue;
                    }
                    Transform3D pose;
                    if (sphere.Bone < 0) pose = mesh.GlobalTransform;
                    else
                    {
                        var transform = skeletonTransform ?? skeleton.Node.GlobalTransform;
                        skeletonTransform = transform;
                        pose = transform * skeleton.Node.GetBoneGlobalPose(sphere.Bone);
                    }
                    var center = pose * sphere.Center;
                    var next = new FalloutNifSphereBound(new(center.X, center.Y, center.Z), sphere.Radius * pose.Basis.Scale.X);
                    result = result?.Merge(next) ?? next;
                }
            }
        }
        return result;
    }

    private static Geometry Prepare(MeshInstance3D mesh, FalloutNifFile source, RuntimeNativeNifSkeleton skeleton)
    {
        var geometry = source.ReadGeometry(mesh.GetMeta("opennv_nif_geometry_block").AsInt32());
        var spheres = new List<Sphere>();
        if (geometry.SkinInstance >= 0)
        {
            var instance = (FalloutNifSkinInstance)source.ReadObject(geometry.SkinInstance);
            var skin = (FalloutNifSkinData)source.ReadObject(instance.Data);
            if (skin.Bones.Length != instance.Bones.Length) throw new InvalidDataException("Skin bound palette extent differs.");
            for (var index = 0; index < skin.Bones.Length; ++index)
            {
                var bone = skin.Bones[index];
                var boneIndex = skeleton.BoneIndex(source.ReadNode(instance.Bones[index]).Name);
                if (boneIndex < 0) throw new InvalidDataException("Posed bounds lost a source skin bone.");
                spheres.Add(Convert(boneIndex, bone.BoundingSphereCenter, bone.BoundingSphereRadius));
            }
        }
        else
        {
            var data = source.ReadMeshData(geometry.Data);
            spheres.Add(Convert(-1, data.Center, data.Radius));
        }
        return new(skeleton, spheres);

        Sphere Convert(int bone, FalloutNifVector3 center, float radius) => new(bone,
            GamebryoCoordinate.ConvertVector(new(center.X, center.Y, center.Z)) * skeleton.UnitsToMetres,
            radius * skeleton.UnitsToMetres);
    }
}
