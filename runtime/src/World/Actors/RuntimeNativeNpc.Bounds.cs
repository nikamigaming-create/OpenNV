using Godot;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Actors;

internal partial class RuntimeNativeNpc
{
    private readonly Dictionary<string, FalloutNifFile> _boundSources = new(StringComparer.OrdinalIgnoreCase);

    internal float SourceHeight
    {
        get
        {
            var source = Skeleton.Source;
            var bounds = source.Roots.Select(source.ReadNode).SelectMany(node => node.ExtraData)
                .Where(index => index >= 0).Select(source.ReadObject).OfType<FalloutNifBound>()
                .Where(bound => bound.Name == "BBX").ToArray();
            if (bounds.Length != 1 || !float.IsFinite(bounds[0].Dimensions.Z) || bounds[0].Dimensions.Z <= 0)
                throw new NotSupportedException("Actor height requires one authored BBX bound.");
            return bounds[0].Dimensions.Z * 2 * Appearance.RaceHeight;
        }
    }

    // NiSkinData stores each bone's bound in bone-local space. Recompute from
    // the current shared pose; neither source bind pose nor a fitted actor
    // radius can replace these bounds after animation.
    internal FalloutNifSphereBound CurrentWorldBound(RuntimeLiveContentSource content)
    {
        FalloutNifSphereBound? result = null;
        foreach (var part in Parts)
        {
            var path = part.Root.GetMeta("opennv_source_model").AsString();
            if (!_boundSources.TryGetValue(path, out var source))
            {
                if (!content.TryRead(path, null, out var bytes, out _)) throw new FileNotFoundException(path);
                _boundSources.Add(path, source = FalloutNifFile.Read(bytes));
            }
            foreach (var mesh in part.Root.FindChildren("*", "", true, false).OfType<MeshInstance3D>()
                .Where(mesh => mesh.Visible && mesh.HasMeta("opennv_nif_geometry_block")))
            {
                var geometry = source.ReadGeometry(mesh.GetMeta("opennv_nif_geometry_block").AsInt32());
                FalloutNifSphereBound? bound = null;
                if (geometry.SkinInstance >= 0)
                {
                    var instance = (FalloutNifSkinInstance)source.ReadObject(geometry.SkinInstance);
                    var skin = (FalloutNifSkinData)source.ReadObject(instance.Data);
                    if (skin.Bones.Length != instance.Bones.Length) throw new InvalidDataException("Skin bound palette extent differs.");
                    for (var index = 0; index < skin.Bones.Length; index++)
                    {
                        var bone = skin.Bones[index];
                        var pose = Skeleton.Node.GlobalTransform * Skeleton.Node.GetBoneGlobalPose(
                            Skeleton.BoneIndex(source.ReadNode(instance.Bones[index]).Name));
                        var next = Convert(bone.BoundingSphereCenter, bone.BoundingSphereRadius, pose);
                        bound = bound?.Merge(next) ?? next;
                    }
                }
                else
                {
                    var data = source.ReadMeshData(geometry.Data);
                    bound = Convert(data.Center, data.Radius, mesh.GlobalTransform);
                }
                if (bound is { } value) result = result?.Merge(value) ?? value;
            }
        }
        return result ?? throw new InvalidDataException("Presented actor has no posed source bounds.");

        FalloutNifSphereBound Convert(FalloutNifVector3 center, float radius, Transform3D pose)
        {
            var value = pose * (GamebryoCoordinate.ConvertVector(new(center.X, center.Y, center.Z)) * Skeleton.UnitsToMetres);
            return new(new(value.X, value.Y, value.Z), radius * Skeleton.UnitsToMetres * pose.Basis.Scale.X);
        }
    }
}
