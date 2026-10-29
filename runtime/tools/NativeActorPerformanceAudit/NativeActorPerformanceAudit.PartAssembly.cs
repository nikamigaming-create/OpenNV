using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Presentation.Rendering;
using OpenNV.Runtime.World.Actors;

public partial class NativeActorPerformanceAudit
{
    private static void VerifyActorPartAssembly(FalloutNpcPreparedGeometry prepared, RuntimeNativeNpc staged,
        RuntimeLiveContentSource content, FalloutPluginStack records)
    {
        var skeleton = NativeNifMeshBuilder.BuildActorSkeleton(prepared.Skeleton, .0142875f);
        try
        {
            var checkedParts = 0;
            for (var index = 0; index < prepared.Parts.Count; index++)
            {
                var input = prepared.Parts[index]; var part = input.Part;
                using var eligible = RuntimeNativeNifMeshBuilder.ActorPartAssembly.TryCreate(input.Model, skeleton, content,
                    null, null, FalloutNpcFaceAttachment.UsesHeadModelSpace(part.Role) ? prepared.HeadBinds : null,
                    input.SelectedShape, null, part.Role is "armor" or "armor-addon" ? part.BipedSlots : 0);
                if (eligible is null) continue;
                var direct = NativeNifMeshBuilder.AddActorPart(input.Model, skeleton,
                    materialOverride: (nif, geometry) => NativeNpcMaterial.Resolve(prepared.Appearance, part, nif, geometry, records, new Color(.3f, .3f, .3f), content),
                    geometryOwner: input.Geometry.Count == 0 ? null : (_, geometry, mesh) => input.Geometry.GetValueOrDefault(geometry.Block.Index, mesh),
                    selectedGeometryName: input.SelectedShape,
                    morphOwner: input.Morphs.Count == 0 ? null : (_, geometry, _) => input.Morphs[geometry.Block.Index],
                    contentSource: content, bipedSlots: part.Role is "armor" or "armor-addon" ? part.BipedSlots : 0);
                try
                {
                    var actual = staged.Parts[index];
                    if (direct.Nodes != actual.Nodes || direct.Surfaces != actual.Surfaces ||
                        direct.Vertices != actual.Vertices || direct.Triangles != actual.Triangles)
                        throw new InvalidDataException("Incremental actor part changed geometry counts.");
                    Compare(direct.Root, actual.Root);
                    checkedParts++;
                }
                finally { direct.Root.Free(); }
            }
            if (checkedParts == 0) throw new InvalidDataException("No selected actor parts exercised incremental skin assembly.");
            GD.Print($"OPENNV_NPC_PART_ASSEMBLY_COMPARE_PASS parts={checkedParts} hierarchy=true skin=true mesh=true gpuAndGameplayTiming=unverified");
        }
        finally { skeleton.Node.Free(); }
    }

    private static void Compare(Node a, Node b)
    {
        if (a.GetType() != b.GetType() || a.GetChildCount() != b.GetChildCount() ||
            a is Node3D x && b is Node3D y && (!x.Transform.IsEqualApprox(y.Transform) || x.Visible != y.Visible) ||
            a is BoneAttachment3D boneA && b is BoneAttachment3D boneB &&
                (boneA.BoneName != boneB.BoneName || boneA.UseExternalSkeleton != boneB.UseExternalSkeleton || boneA.ExternalSkeleton != boneB.ExternalSkeleton) ||
            a.GetMeta("opennv_nif_geometry_block", -1).AsInt32() != b.GetMeta("opennv_nif_geometry_block", -1).AsInt32() ||
            a.GetMeta("opennv_nif_skin_partition", -1).AsInt32() != b.GetMeta("opennv_nif_skin_partition", -1).AsInt32())
            throw new InvalidDataException($"Incremental actor hierarchy differs at {a.Name}: " +
                $"type={a.GetType().Name}/{b.GetType().Name} children={a.GetChildCount()}/{b.GetChildCount()} " +
                $"names=[{string.Join(',', a.GetChildren().Select(child => child.Name.ToString()))}]/[{string.Join(',', b.GetChildren().Select(child => child.Name.ToString()))}].");
        if (a is MeshInstance3D ma && b is MeshInstance3D mb)
        {
            if (ma.Skin?.GetBindCount() != mb.Skin?.GetBindCount() || ma.Mesh?.GetSurfaceCount() != mb.Mesh?.GetSurfaceCount())
                throw new InvalidDataException("Incremental actor skin or surface count differs.");
            if (ma.Skin is { } sa && mb.Skin is { } sb)
                for (var i = 0; i < sa.GetBindCount(); i++)
                    if (sa.GetBindBone(i) != sb.GetBindBone(i) || sa.GetBindName(i) != sb.GetBindName(i) || !sa.GetBindPose(i).IsEqualApprox(sb.GetBindPose(i)))
                        throw new InvalidDataException("Incremental actor bone palette differs.");
            if (ma.Mesh is { } meshA && mb.Mesh is { } meshB)
                for (var i = 0; i < meshA.GetSurfaceCount(); i++)
                {
                    var materialA = meshA.SurfaceGetMaterial(i);
                    var materialB = meshB.SurfaceGetMaterial(i);
                    if (materialA is ShaderMaterial shaderA &&
                        (materialB is not ShaderMaterial shaderB || shaderA.Shader?.Code != shaderB.Shader?.Code) ||
                        materialA is not ShaderMaterial && materialB is ShaderMaterial)
                        throw new InvalidDataException("Incremental actor shader source differs from the synchronous source.");
                    var aa = meshA.SurfaceGetArrays(i); var ab = meshB.SurfaceGetArrays(i);
                    if (!aa[(int)Mesh.ArrayType.Vertex].AsVector3Array().SequenceEqual(ab[(int)Mesh.ArrayType.Vertex].AsVector3Array()) ||
                        !aa[(int)Mesh.ArrayType.Index].AsInt32Array().SequenceEqual(ab[(int)Mesh.ArrayType.Index].AsInt32Array()) ||
                        !aa[(int)Mesh.ArrayType.Bones].AsInt32Array().SequenceEqual(ab[(int)Mesh.ArrayType.Bones].AsInt32Array()) ||
                        !aa[(int)Mesh.ArrayType.Weights].AsFloat32Array().SequenceEqual(ab[(int)Mesh.ArrayType.Weights].AsFloat32Array()))
                        throw new InvalidDataException("Incremental actor vertex or skin arrays differ.");
                }
        }
        for (var i = 0; i < a.GetChildCount(); i++) Compare(a.GetChild(i), b.GetChild(i));
    }
}
