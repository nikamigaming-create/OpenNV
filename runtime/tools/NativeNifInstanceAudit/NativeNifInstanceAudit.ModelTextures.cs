using System.Text;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Presentation.Rendering;

public partial class NativeNifInstanceAudit
{
    private static void ExerciseModelTextures()
    {
        var nif = FalloutNifFile.Read(ModelTextureFixture());
        var order = FalloutNifGeometryOrder.Read(nif);
        if (!order.Select(shape => shape.Block.Index).SequenceEqual([8, 1, 7]) ||
            !ReferenceEquals(order, FalloutNifGeometryOrder.Read(nif)))
            throw new InvalidDataException("Model texture indices used block order or included unreferenced geometry.");
        var owner = new FalloutFormKey("Test.esm", 1);
        FalloutNpcTextureOverride Entry(string name, int index, string path) => new(name, index, owner,
            new Dictionary<string, string> { ["TX00"] = path }, []);
        var part = new FalloutNpcAppearancePart("armor", owner, "meshes/test.nif", null, 4, 0,
            [Entry("obsolete label", 0, "textures/first.dds"), Entry("surface", 2, "textures/other.dds"),
                Entry("different label", 0, "textures/replacement.dds")]);
        var paths = order.Select(shape => NativeNpcMaterial.Alternate(part, nif, shape)).ToArray();
        if (paths[0]?[0] != "textures/replacement.dds" || paths[1] is not null || paths[2]?[0] != "textures/other.dds")
            throw new InvalidDataException("Model texture overrides redirected by names, omitted a repeated name or lost index replacement order.");
        foreach (var invalid in new[] { -1, 3 })
        {
            try
            {
                NativeNpcMaterial.Alternate(part with { AlternateTextures = [Entry("surface", invalid, "textures/no.dds")] }, nif, order[0]);
                throw new InvalidOperationException("Unowned model texture index was accepted.");
            }
            catch (InvalidDataException) { }
        }
        foreach (var shared in new[] { false, true })
        {
            try
            {
                FalloutNifGeometryOrder.Read(FalloutNifFile.Read(ModelTextureFixture(shared ? 1 : 0)));
                throw new InvalidOperationException("Cyclic or shared texture scene graph was accepted.");
            }
            catch (InvalidDataException) { }
        }
        var particles = FalloutNifFile.Read(ModelTextureFixture(particles: true));
        var particleOrder = FalloutNifGeometryOrder.Read(particles);
        if (!particleOrder.Select(shape => shape.Block.Index).SequenceEqual([8, 10, 1, 7]))
            throw new InvalidDataException("Particle geometry did not consume its source model index.");
        try
        {
            NativeNpcMaterial.Alternate(part with { AlternateTextures = [Entry("surface", 1, "textures/no.dds")] }, particles, particleOrder[0]);
            throw new InvalidOperationException("Unbound particle texture substitution was accepted.");
        }
        catch (NotSupportedException) { }
        GD.Print("OPENNV_MODEL_TEXTURE_INDEX_PASS sceneOrder=true repeatedNames=true labelsIgnored=true lastIndexWins=true unusedGeometryExcluded=true particlesCounted=true invalidOwnersRejected=true reuse=true");
    }

    private static byte[] ModelTextureFixture(int extraChild = -1, bool particles = false)
    {
        var templateBytes = SurfaceFixture(0x80000100);
        var template = FalloutNifFile.Read(templateBytes);
        var blocks = template.Blocks.Select(block => (Type: block.TypeName,
            Data: templateBytes.AsSpan(block.Offset, block.Size).ToArray())).ToList();
        byte[] Node(params int[] children) => Bytes(w =>
        {
            w.Write(0); w.Write(0); w.Write(-1); w.Write(14U);
            foreach (var value in new float[] { 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 1 }) w.Write(value);
            w.Write(0); w.Write(-1); w.Write(children.Length);
            foreach (var child in children) w.Write(child);
            w.Write(0);
        });
        blocks[0] = ("NiNode", Node(6, 7, extraChild));
        blocks.Add(("NiNode", particles ? Node(8, 10, 1) : Node(8, -1, 1)));
        for (var i = 0; i < 3; i++) blocks.Add(("NiTriShape", blocks[1].Data.ToArray()));
        if (particles) blocks.Add(("NiParticleSystem", Bytes(w => { w.Write(blocks[1].Data); w.Write(false); w.Write(0); })));
        return Bytes(w =>
        {
            w.Write("Gamebryo File Format, Version 20.2.0.7\n"u8); w.Write(FalloutNifFile.Version);
            w.Write((byte)1); w.Write(FalloutNifFile.UserVersion); w.Write(blocks.Count); w.Write(34U);
            w.Write(new byte[] { 1, 0, 1, 0, 1, 0 }); w.Write((ushort)blocks.Count);
            foreach (var block in blocks) { w.Write(block.Type.Length); w.Write(Encoding.ASCII.GetBytes(block.Type)); }
            for (var i = 0; i < blocks.Count; i++) w.Write((ushort)i);
            foreach (var block in blocks) w.Write(block.Data.Length);
            w.Write(1); w.Write(7); w.Write(7); w.Write("surface"u8); w.Write(0);
            foreach (var block in blocks) w.Write(block.Data);
            w.Write(1); w.Write(0);
        });
    }
}
