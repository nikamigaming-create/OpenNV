using System.Text;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

internal static partial class PlayerOpacityContracts
{
    private static FalloutActorSourceModel Model()
    {
        var nif = FalloutNifFile.Read(SceneBytes());
        var key = new FalloutFormKey("OpacityFixture.esm", 7);
        var face = new FalloutNpcFaceGen(key, [], [], []);
        var appearance = new FalloutNpcAppearance(key, null, key, key, key, false, new("OpacityFixture.esm", 8),
            "meshes/authored/opacity-skeleton.nif", 1, 1, [], [], [], [], face, face, [], [], [], [], [], [], null, null);
        return new(appearance, FalloutNifPhysicalModel.Read(nif), []);
    }
    private static byte[] SceneBytes()
    {
        (string Type, byte[] Data)[] blocks =
        [
            ("NiNode", Bytes(w => { Av(w, 0, []); Refs(w, [1, -1, 2, 4]); Refs(w, []); })),
            ("NiTriShape", Bytes(w => Geometry(w, 1, [5, 7]))),
            ("NiNode", Bytes(w => { Av(w, 2, []); Refs(w, [3, -1]); Refs(w, []); })),
            ("NiTriShape", Bytes(w => Geometry(w, 3, [6, 7]))),
            ("NiTriShape", Bytes(w => Geometry(w, 4, [5, 7]))),
            ("BSShaderPPLightingProperty", Shader()),
            ("BSShaderPPLightingProperty", Shader()),
            ("NiMaterialProperty", Bytes(w => { Net(w, 5); for (var i = 0; i < 6; i++) w.Write(0f); w.Write(10f); w.Write(.375f); w.Write(1f); })),
            ("NiTriShapeData", Bytes(w =>
            {
                w.Write(0); w.Write((ushort)3); w.Write((byte)0); w.Write((byte)0); w.Write(true);
                foreach (var value in new float[] { -1, 0, 0, 1, 0, 0, 0, 1, 0 }) w.Write(value);
                w.Write((byte)0); w.Write((byte)0); w.Write(false);
                w.Write(0f); w.Write(0f); w.Write(0f); w.Write(1f); w.Write(false);
                w.Write((ushort)0); w.Write(-1); w.Write((ushort)1); w.Write(3U); w.Write(true);
                w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)2); w.Write((ushort)0);
            }))
        ];
        return Bytes(w =>
        {
            w.Write(Encoding.ASCII.GetBytes("Gamebryo File Format, Version 20.2.0.7\n"));
            w.Write(FalloutNifFile.Version); w.Write((byte)1); w.Write(FalloutNifFile.UserVersion);
            w.Write(blocks.Length); w.Write(34U); w.Write(new byte[] { 1, 0, 1, 0, 1, 0 });
            w.Write((ushort)blocks.Length);
            foreach (var block in blocks) Text(w, block.Type);
            for (var index = 0; index < blocks.Length; index++) w.Write((ushort)index);
            foreach (var block in blocks) w.Write(block.Data.Length);
            string[] names = ["Root", "A", "Branch", "B", "C", "Material"];
            w.Write(names.Length); w.Write(8U); foreach (var name in names) Text(w, name);
            w.Write(0U); foreach (var block in blocks) w.Write(block.Data); w.Write(1U); w.Write(0);
        });
    }
    private static byte[] Shader() => Bytes(w =>
    {
        Net(w, -1); w.Write((ushort)1); w.Write(123U); w.Write(0U); w.Write(0U); w.Write(1f); w.Write(3U); w.Write(-1);
        w.Write(0f); w.Write(0); w.Write(4f); w.Write(1f);
    });
    private static void Geometry(BinaryWriter w, int name, int[] properties)
    { Av(w, name, properties); w.Write(8); w.Write(-1); w.Write(0U); w.Write(-1); w.Write(false); }
    private static void Av(BinaryWriter w, int name, int[] properties)
    {
        Net(w, name); w.Write(0U);
        w.Write(0f); w.Write(0f); w.Write(0f);
        foreach (var value in new float[] { 1, 0, 0, 0, 1, 0, 0, 0, 1 }) w.Write(value);
        w.Write(1f); Refs(w, properties); w.Write(-1);
    }
    private static void Net(BinaryWriter w, int name) { w.Write(name); Refs(w, []); w.Write(-1); }
    private static void Refs(BinaryWriter w, int[] references) { w.Write(references.Length); foreach (var reference in references) w.Write(reference); }
    private static void Text(BinaryWriter w, string value) { w.Write(value.Length); w.Write(Encoding.ASCII.GetBytes(value)); }
    private static byte[] Bytes(Action<BinaryWriter> write)
    { using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream); write(writer); return stream.ToArray(); }
}
