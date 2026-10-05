using System.Text;
using Godot;
using OpenNV.Runtime.Formats.Gamebryo;

public partial class NativeNifInstanceAudit
{
    private static void ExerciseNoLightingTextureOwner()
    {
        for (uint mode = 0; mode < 4; mode++)
        {
            var bytes = NoLightingTextureFixture(mode);
            var original = bytes.ToArray();
            var nif = FalloutNifFile.Read(bytes);
            var source = (FalloutNifSourceTexture)nif.ReadObject(6);
            var shader = (FalloutNifNoLightingProperty)nif.ReadObject(3);
            var legacy = (FalloutNifTexturingProperty)nif.ReadObject(4);
            var scene = RuntimeNativeNifMeshBuilder.Build(nif, .0142875f);
            try
            {
                var mesh = scene.Root.FindChildren("*", "MeshInstance3D", true, false).Cast<MeshInstance3D>().Single();
                var material = mesh.Mesh.SurfaceGetMaterial(0) as ShaderMaterial ??
                    throw new InvalidDataException("No-lighting texture fixture lost its source shader.");
                if (shader.FileName != "" || source.FileName != "textures/unused-legacy.dds" ||
                    legacy.BaseTexture?.Source != 6 || legacy.BaseTexture.Flags != 0x3200 ||
                    material.GetShaderParameter("source_has_texture").AsBool() ||
                    material.GetMeta("opennv_nif_texture_clamp_mode").AsUInt32() != mode ||
                    !bytes.AsSpan().SequenceEqual(original))
                    throw new InvalidDataException("Dormant legacy texture replaced the shader texture/sampler or source bytes.");
            }
            finally { scene.Root.Free(); }
        }
        try
        {
            RuntimeNativeNifMeshBuilder.Build(FalloutNifFile.Read(NoLightingTextureFixture(3, transform: true)), .0142875f).Root.Free();
            throw new InvalidOperationException("An unmatched active legacy texture transform was admitted.");
        }
        catch (NotSupportedException failure) when (failure.Message.Contains("no matching no-lighting shader texture", StringComparison.Ordinal)) { }
        GD.Print("OPENNV_NO_LIGHTING_TEXTURE_OWNER_PASS emptyShaderRetained=true dormantLegacyNotLoaded=true shaderSampler=true activeMismatchRefused=true sourceBytesPreserved=true pixels=unverified");
    }

    private static byte[] NoLightingTextureFixture(uint mode, bool transform = false)
    {
        var template = SurfaceFixture(0x80000100);
        var file = FalloutNifFile.Read(template);
        var blocks = file.Blocks.Select(block => (Type: block.TypeName,
            Data: template.AsSpan(block.Offset, block.Size).ToArray())).ToList();
        void Net(BinaryWriter writer) { writer.Write(0); writer.Write(0); writer.Write(-1); }
        blocks[1] = ("NiTriShape", Bytes(writer =>
        {
            Net(writer); writer.Write(14U);
            foreach (var value in new float[] { 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 1 }) writer.Write(value);
            writer.Write(3); writer.Write(3); writer.Write(4); writer.Write(5); writer.Write(-1);
            writer.Write(2); writer.Write(-1); writer.Write(0); writer.Write(-1); writer.Write(false);
        }));
        blocks[3] = ("BSShaderNoLightingProperty", Bytes(writer =>
        {
            Net(writer); writer.Write((ushort)1); writer.Write(33U); writer.Write(0x82000000U);
            writer.Write(1U); writer.Write(1f); writer.Write(mode); writer.Write(0);
            writer.Write(1f); writer.Write(0f); writer.Write(1f); writer.Write(0f);
        }));
        blocks[4] = ("NiTexturingProperty", Bytes(writer =>
        {
            Net(writer); writer.Write((ushort)4); writer.Write(9); writer.Write(true);
            writer.Write(6); writer.Write((ushort)0x3200); writer.Write(transform);
            if (transform)
            {
                writer.Write(.25f); writer.Write(.5f); writer.Write(1f); writer.Write(1f);
                writer.Write(0f); writer.Write(0U); writer.Write(0f); writer.Write(0f);
            }
            for (var index = 1; index < 9; index++) writer.Write(false);
            writer.Write(0);
        }));
        blocks.Add(("NiSourceTexture", Bytes(writer =>
        {
            Net(writer); writer.Write((byte)1); writer.Write(1); writer.Write(-1);
            writer.Write(6U); writer.Write(1U); writer.Write(3U); writer.Write((byte)1);
            writer.Write(true); writer.Write(false);
        })));
        return Bytes(writer =>
        {
            writer.Write("Gamebryo File Format, Version 20.2.0.7\n"u8); writer.Write(FalloutNifFile.Version);
            writer.Write((byte)1); writer.Write(FalloutNifFile.UserVersion); writer.Write(blocks.Count);
            writer.Write(34U); writer.Write(new byte[] { 1, 0, 1, 0, 1, 0 }); writer.Write((ushort)blocks.Count);
            foreach (var block in blocks) { writer.Write(block.Type.Length); writer.Write(Encoding.ASCII.GetBytes(block.Type)); }
            for (var index = 0; index < blocks.Count; index++) writer.Write((ushort)index);
            foreach (var block in blocks) writer.Write(block.Data.Length);
            string[] strings = ["surface", "textures/unused-legacy.dds"];
            writer.Write(strings.Length); writer.Write(strings.Max(value => value.Length));
            foreach (var value in strings) { writer.Write(value.Length); writer.Write(Encoding.ASCII.GetBytes(value)); }
            writer.Write(0); foreach (var block in blocks) writer.Write(block.Data);
            writer.Write(1); writer.Write(0);
        });
    }
}
