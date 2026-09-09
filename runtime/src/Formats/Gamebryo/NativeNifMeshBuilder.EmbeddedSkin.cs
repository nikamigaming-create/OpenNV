using Godot;

namespace OpenNV.Runtime.Formats.Gamebryo;

internal static partial class RuntimeNativeNifMeshBuilder
{
    private sealed partial class BuildState
    {
        private readonly List<(RuntimeNifEmbeddedSkin Skeleton, FalloutNifSkinInstance Source)> _embeddedSkins = [];

        private Node3D BuildEmbeddedSkin(FalloutNifGeometry geometry, FalloutNifMeshData data,
            FalloutNifSkinInstance instance, FalloutNifCollisionObject? collision)
        {
            var skinData = (FalloutNifSkinData)_source.ReadObject(instance.Data);
            var partitions = FalloutNifHardwareSkin.Read(instance, skinData,
                (FalloutNifSkinPartition)_source.ReadObject(instance.SkinPartition), data.Vertices.Length, data.Triangles);
            var result = CreateNode(geometry.Name, geometry.Block.Index, geometry.Transform, geometry.Flags);
            try
            {
                // Preserve the same skin-space product used by equipped actor
                // parts. These bones live in this NIF's visual/physics tree.
                result.Transform *= ConvertTransform(skinData.SkinTransform);
                var skeleton = new RuntimeNifEmbeddedSkin { Name = "EmbeddedSkin", ProcessPriority = 100 };
                result.AddChild(skeleton);
                for (var bone = 0; bone < instance.Bones.Length; bone++)
                    skeleton.AddBone($"SourceBone{instance.Bones[bone]}");
                var material = BuildMaterial(geometry);
                foreach (var partition in partitions)
                {
                    var skin = new Skin();
                    skin.SetBindCount(partition.BonePalette.Length);
                    for (var bind = 0; bind < partition.BonePalette.Length; bind++)
                    {
                        var bone = partition.BonePalette[bind];
                        skin.SetBindBone(bind, bone);
                        skin.SetBindName(bind, skeleton.GetBoneName(bone));
                        skin.SetBindPose(bind, ConvertTransform(skinData.Bones[bone].SkinTransform));
                    }
                    var arrays = BuildHardwareSkinArrays(geometry, data, partition);
                    var mesh = new ArrayMesh();
                    mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays,
                        BuildMorphArrays(mesh, geometry, data, arrays, partition.VertexMap.Select(value => (int)value).ToArray()),
                        flags: partition.InfluencesPerVertex == 8 ? Mesh.ArrayFormat.FlagUse8BoneWeights : 0);
                    mesh.SurfaceSetMaterial(0, material);
                    var rendered = new MeshInstance3D
                    {
                        Name = $"Partition{partition.PartitionIndex}",
                        Mesh = mesh,
                        Skin = skin,
                        Skeleton = new NodePath("../EmbeddedSkin"),
                        Visible = FalloutNifHardwareSkin.VisibleOnIntactBody(partition.BodyPart)
                    };
                    rendered.SetMeta("opennv_nif_geometry_block", geometry.Block.Index);
                    rendered.SetMeta("opennv_nif_skin_instance", instance.Block.Index);
                    rendered.SetMeta("opennv_nif_skin_partition", partition.PartitionIndex);
                    rendered.SetMeta("opennv_nif_skin_vertex_map", partition.VertexMap.Select(value => (int)value).ToArray());
                    result.AddChild(rendered);
                    SurfaceCount++; VertexCount += partition.VertexMap.Length;
                    TriangleCount += arrays[(int)Mesh.ArrayType.Index].AsInt32Array().Length / 3;
                }
                _nodes.Add(geometry.Block.Index, result);
                _embeddedSkins.Add((skeleton, instance));
                PreserveExtraDataMetadata(result, geometry.ExtraData);
                PreserveCollisionMetadata(result, collision); AddCollision(result, collision);
                NodeCount++;
                return result;
            }
            catch { result.Free(); throw; }
        }

        internal void BindEmbeddedSkins()
        {
            foreach (var (skeleton, source) in _embeddedSkins)
            {
                if (!_nodes.TryGetValue(source.SkeletonRoot, out var root) ||
                    source.Bones.Any(bone => !_nodes.ContainsKey(bone)))
                    throw new InvalidDataException("Embedded skin references a bone outside its built source hierarchy.");
                skeleton.Bind(root, source.Bones.Select(bone => _nodes[bone]).ToArray());
            }
        }
    }
}
