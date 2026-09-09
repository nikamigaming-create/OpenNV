using System.Text;
using Godot;
using OpenNV.Runtime.Formats.Gamebryo;

public partial class NativeNifInstanceAudit
{
    private static void ExerciseAuthoredDecalSurfaces()
    {
        foreach (var decal in new[] { 1U << 26, 1U << 27, (1U << 26) | (1U << 27) })
        {
            var flags = 0x80000100 | decal;
            var scene = RuntimeNativeNifMeshBuilder.Build(FalloutNifFile.Read(SurfaceFixture(flags)), .02f);
            try
            {
                var mesh = scene.Root.FindChildren("*", "", true, false).OfType<MeshInstance3D>().Single();
                if (scene.Surfaces != 1 || scene.Vertices != 3 || mesh.Mesh.SurfaceGetMaterial(0) is not ShaderMaterial material ||
                    material.GetMeta("opennv_nif_shader_flags").AsUInt32() != flags ||
                    material.GetMeta("opennv_nif_alpha_flags").AsUInt32() != 4845 ||
                    !material.HasMeta("opennv_decal_owner") || material.NextPass is not null ||
                    !material.Shader.Code.Contains("depth_draw_always", StringComparison.Ordinal) ||
                    material.Shader.Code.Contains("depth_test_disabled", StringComparison.Ordinal))
                    throw new InvalidDataException("Authored decal lost geometry, alpha, depth or single-pass ownership.");
            }
            finally { scene.Root.Free(); }
        }
        try
        {
            var scene = RuntimeNativeNifMeshBuilder.Build(FalloutNifFile.Read(SurfaceFixture(0x90000100)), .02f);
            scene.Root.Free();
            throw new InvalidDataException("Unsupported parallax-occlusion semantics were accepted as ordinary decals.");
        }
        catch (NotSupportedException) { }
        GD.Print("OPENNV_AUTHORED_DECAL_SURFACE_PASS geometryRetained=true alphaDepthPreserved=true singlePass=true unrelatedFlagsRejected=true");
    }

    private static void ExerciseDormantEnvironmentMask()
    {
        string? shaderCode = null;
        foreach (var mask in new[] { "", "textures\\unused_mask.dds", "textures\\another_unused_mask.dds" })
        {
            var scene = RuntimeNativeNifMeshBuilder.Build(FalloutNifFile.Read(SurfaceFixture(0x80000100, mask)), .02f);
            try
            {
                var mesh = scene.Root.FindChildren("*", "", true, false).OfType<MeshInstance3D>().Single();
                if (scene.Surfaces != 1 || scene.Vertices != 3 || mesh.Mesh.SurfaceGetMaterial(0) is not ShaderMaterial material ||
                    material.NextPass is not null || shaderCode is not null && material.Shader.Code != shaderCode)
                    throw new InvalidDataException("Dormant environment-mask metadata changed the ordinary source draw.");
                shaderCode = material.Shader.Code;
            }
            finally { scene.Root.Free(); }
        }
        try
        {
            var scene = RuntimeNativeNifMeshBuilder.Build(FalloutNifFile.Read(SurfaceFixture(0x80000180, "textures\\unused_mask.dds")), .02f);
            scene.Root.Free();
            throw new InvalidDataException("Active environment mapping accepted missing source inputs.");
        }
        catch (NotSupportedException error) when (error.Message.Contains("no tangent-space normal map", StringComparison.Ordinal)) { }
        GD.Print("OPENNV_DORMANT_ENVIRONMENT_MASK_PASS geometryRetained=true inactiveMaskNotRead=true activeInputsRequired=true");
    }

    private static byte[] SurfaceFixture(uint flags, string environmentMask = "")
    {
        void Net(BinaryWriter w) { w.Write(0); w.Write(0); w.Write(-1); }
        void Av(BinaryWriter w, params int[] properties)
        {
            Net(w); w.Write((ushort)14); w.Write((ushort)0);
            foreach (var value in new float[] { 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 1 }) w.Write(value);
            w.Write(properties.Length); foreach (var property in properties) w.Write(property); w.Write(-1);
        }
        (string Type, byte[] Data)[] blocks =
        [
            ("NiNode", Bytes(w => { Av(w); w.Write(1); w.Write(1); w.Write(0); })),
            ("NiTriShape", Bytes(w => { Av(w, 3, 5); w.Write(2); w.Write(-1); w.Write(0); w.Write(-1); w.Write(false); })),
            ("NiTriShapeData", Bytes(w =>
            {
                w.Write(0); w.Write((ushort)3); w.Write((byte)0); w.Write((byte)0); w.Write(true);
                foreach (var value in new float[] { 0, 0, 0, 1, 0, 0, 0, 1, 0 }) w.Write(value);
                w.Write((byte)1); w.Write((byte)16); w.Write(true);
                foreach (var axis in new[] { new Vector3(0, 0, 1), new Vector3(1, 0, 0), new Vector3(0, 1, 0) })
                    for (var i = 0; i < 3; i++) { w.Write(axis.X); w.Write(axis.Y); w.Write(axis.Z); }
                w.Write(0f); w.Write(0f); w.Write(0f); w.Write(2f); w.Write(false);
                foreach (var value in new float[] { 0, 0, 1, 0, 0, 1 }) w.Write(value);
                w.Write((ushort)0); w.Write(-1); w.Write((ushort)1); w.Write(3U); w.Write(true);
                w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)2); w.Write((ushort)0);
            })),
            ("BSShaderPPLightingProperty", Bytes(w =>
            {
                Net(w); w.Write((ushort)1); w.Write(1U); w.Write(flags); w.Write(1U);
                w.Write(1f); w.Write(3U); w.Write(4); w.Write(0f); w.Write(0); w.Write(0f); w.Write(0f);
            })),
            ("BSShaderTextureSet", Bytes(w =>
            {
                w.Write(6);
                for (var i = 0; i < 6; i++)
                {
                    var bytes = Encoding.ASCII.GetBytes(i == 5 ? environmentMask : string.Empty);
                    w.Write(bytes.Length); w.Write(bytes);
                }
            })),
            ("NiAlphaProperty", Bytes(w => { Net(w); w.Write((ushort)4845); w.Write((byte)128); })),
        ];
        return Bytes(w =>
        {
            w.Write(Encoding.ASCII.GetBytes("Gamebryo File Format, Version 20.2.0.7\n"));
            w.Write(FalloutNifFile.Version); w.Write((byte)1); w.Write(FalloutNifFile.UserVersion);
            w.Write(blocks.Length); w.Write(34U); w.Write(new byte[] { 1, 0, 1, 0, 1, 0 });
            w.Write((ushort)blocks.Length);
            foreach (var block in blocks) { w.Write(block.Type.Length); w.Write(Encoding.ASCII.GetBytes(block.Type)); }
            for (var i = 0; i < blocks.Length; i++) w.Write((ushort)i);
            foreach (var block in blocks) w.Write(block.Data.Length);
            w.Write(1); w.Write(7); w.Write(7); w.Write("surface"u8); w.Write(0);
            foreach (var block in blocks) w.Write(block.Data);
            w.Write(1); w.Write(0);
        });
    }
}
