using System.Text;
using OpenNV.Runtime.Formats.Gamebryo;

internal static class ExteriorNifContracts
{
    internal static void Run()
    {
        ParticleData();
        ParticleVolumes();
        foreach (var type in new[] { "BSRangeNode", "BSBlastNode", "BSDamageStage" })
        {
            var range = Bytes(w => { Av(w); w.Write(0U); w.Write(0U); w.Write(new byte[] { 2, 7, 3 }); });
            if (FalloutNifFile.Read(File([(type, range)])).ReadNode(0).Range is not { Minimum: 2, Maximum: 7, Current: 3 })
                throw new InvalidOperationException("Range-node byte fields changed.");
            Reject(() => FalloutNifFile.Read(File([(type, range[..^1])])).ReadNode(0));
        }
        var node = Bytes(w => { Av(w); w.Write(1U); w.Write(1); w.Write(0U); w.Write(2); });
        var shape = Bytes(w =>
        {
            Av(w); w.Write(-1); w.Write(-1); w.Write(0U); w.Write(-1); w.Write(false);
            w.Write(2U); w.Write((byte)1); w.Write(0U); w.Write(10U); w.Write((byte)0); w.Write(30U); w.Write(5U);
        });
        var morph = Bytes(w =>
        {
            w.Write((ushort)2);
            foreach (var value in new uint[] { 1, 1, 4, 8, 4, 0, 0 }) w.Write(value);
            w.Write((byte)2); w.Write(1U); w.Write(true);
            foreach (var value in new uint[] { 8, 1, 0, 1, 4 }) w.Write(value);
            w.Write(-20f); w.Write(200f);
        });
        var sky = Bytes(w =>
        {
            Net(w); w.Write((ushort)1); w.Write(1U); w.Write(0x200U); w.Write(0U);
            w.Write(1f); w.Write(3U); Text(w, "textures/sky/test.dds"); w.Write(3U);
        });
        (string, byte[])[] blocks = [("BSMultiBoundNode", node), ("BSSegmentedTriShape", shape),
            ("BSMultiBound", Bytes(w => w.Write(3))),
            ("BSMultiBoundAABB", Bytes(w => { foreach (var value in new float[] { 100, -20, 30, 10, 20, 5 }) w.Write(value); })),
            ("NiAdditionalGeometryData", morph), ("SkyShaderProperty", sky)];
        var input = File(blocks);
        var reads = new List<FalloutNifReadRange>();
        var nif = FalloutNifFile.Read(input, reads.Add);
        if (reads.Sum(read => read.Length) != input.Length || reads.First().Offset != 0 ||
            reads.Zip(reads.Skip(1)).Any(pair => pair.First.Offset + pair.First.Length != pair.Second.Offset))
            throw new InvalidOperationException("NIF envelope observation skipped or duplicated a byte extent.");
        reads.Clear();
        var root = nif.ReadNode(0);
        if (reads.Sum(read => read.Length) != nif.Blocks[0].Size || reads.First().Offset != nif.Blocks[0].Offset ||
            reads.Zip(reads.Skip(1)).Any(pair => pair.First.Offset + pair.First.Length != pair.Second.Offset) ||
            !reads.Any(read => read.Encoding == "ieee754-f32-le"))
            throw new InvalidOperationException("NIF field observation lost source offsets, scalar encoding or coverage.");
        reads.Clear();
        Parallel.For(0, 100, _ =>
        {
            if (!ReferenceEquals(root, nif.ReadNode(0))) throw new InvalidOperationException("Repeated NIF reads rebuilt an immutable declaration.");
        });
        if (reads.Count != 0) throw new InvalidOperationException("Cached decode manufactured new source-read events.");
        var segments = nif.ReadGeometry(1).Segments;
        if (root.MultiBound != 2 || root.Children is not [1] || nif.ReadObject(2) is not FalloutNifMultiBound { Data: 3 } ||
            nif.ReadObject(3) is not FalloutNifMultiBoundBox { Center.X: 100, Size.Z: 5 } ||
            segments is not [{ Flags: 1, FirstIndex: 0, Triangles: 10 }, { Flags: 0, FirstIndex: 30, Triangles: 5 }] ||
            nif.ReadObject(4) is not FalloutNifLandscapeMorphData { Heights: [-20, 200] } ||
            nif.ReadObject(5) is not FalloutNifSkyShaderProperty { SkyObjectType: 3 })
            throw new InvalidOperationException("Exterior NIF source fields or stream alignment differ.");
        blocks[1] = ("BSSegmentedTriShape", shape[..^1]);
        Reject(() => FalloutNifFile.Read(File(blocks)).ReadGeometry(1));
        blocks[1] = ("BSSegmentedTriShape", shape);
        var badMorph = morph.ToArray(); badMorph[30] = 3; // Unsupported stream flags.
        blocks[4] = ("NiAdditionalGeometryData", badMorph);
        Reject(() => FalloutNifFile.Read(File(blocks)).ReadObject(4));
        blocks[4] = ("NiAdditionalGeometryData", morph[..^1]);
        Reject(() => FalloutNifFile.Read(File(blocks)).ReadObject(4));
        Console.WriteLine("OPENNV_EXTERIOR_NIF_CONTRACT_PASS multiBound=true segmentsNineBytes=true morphStream=true skyObject=true malformedRejected=true");
    }

    private static void ParticleData()
    {
        var data = Bytes(w =>
        {
            w.Write(0); w.Write((ushort)31); w.Write((byte)0); w.Write((byte)0); w.Write(true);
            w.Write((ushort)0); w.Write(false);
            foreach (var value in new[] { 1f, 2f, 3f, 5f }) w.Write(value);
            w.Write(true); w.Write((ushort)0); w.Write(-1); w.Write(true); w.Write((ushort)0);
            w.Write(true); w.Write(false); w.Write(true); w.Write(false); w.Write(true); w.Write((byte)2);
            foreach (var value in new[] { 0f, .5f, 0f, 1f, .5f, .5f, 0f, 1f }) w.Write(value);
            w.Write(false);
        });
        var file = FalloutNifFile.Read(File([("NiPSysData", data)]));
        if (file.ReadObject(0) is not FalloutNifParticleData { Maximum: 31, Active: 0, HasVertices: true,
            HasRadii: true, HasSizes: true, HasColors: true, HasTextureIndices: true, Subtextures.Length: 2 } value ||
            value.Subtextures[1] != new FalloutNifVector4(.5f, .5f, 0f, 1f))
            throw new InvalidOperationException("Particle runtime-capacity bits or atlas offsets were misread as saved arrays.");
        Reject(() => FalloutNifFile.Read(File([("NiPSysData", data[..^1])])).ReadObject(0));
        Reject(() => FalloutNifFile.Read(File([("NiPSysData", [.. data, 0])])).ReadObject(0));
    }

    private static void ParticleVolumes()
    {
        foreach (var type in new[] { "NiPSysCylinderEmitter", "NiPSysSphereEmitter", "NiPSysDragModifier" })
        {
            var data = Bytes(w =>
            {
                w.Write(0); w.Write(4000U); w.Write(0); w.Write(true);
                if (type == "NiPSysDragModifier")
                {
                    w.Write(0);
                    foreach (var value in new[] { 1f, 0f, 0f, .7f, 25f, 10f }) w.Write(value);
                }
                else
                {
                    foreach (var value in new[] { 20f, 3f, .2f, .1f, .5f, .3f, 1f, .5f, .2f, .8f, 2f, 1f, 3f, .4f }) w.Write(value);
                    w.Write(0); w.Write(7f);
                    if (type == "NiPSysCylinderEmitter") w.Write(11f);
                }
            });
            var parsed = FalloutNifFile.Read(File([(type, data)])).ReadObject(0);
            var valid = parsed switch
            {
                FalloutNifParticleCylinderEmitter cylinder => cylinder.CylinderRadius == 7 && cylinder.Height == 11 && cylinder.Emitter.Speed == 20 && cylinder.Emitter.Life == 3,
                FalloutNifParticleSphereEmitter sphere => sphere.SphereRadius == 7 && sphere.Emitter.Radius == 2,
                FalloutNifParticleDrag drag => drag.Axis.X == 1 && drag.Percentage == .7f && drag.Range == 25 && drag.Falloff == 10,
                _ => false,
            };
            if (!valid) throw new InvalidOperationException("Particle volume/drag fields lost their source meaning.");
            Reject(() => FalloutNifFile.Read(File([(type, data[..^1])])).ReadObject(0));
            Reject(() => FalloutNifFile.Read(File([(type, [.. data, 0])])).ReadObject(0));
        }
        Console.WriteLine("OPENNV_PARTICLE_VOLUME_CONTRACT_PASS cylinder=true sphere=true directionalDrag=true malformedRejected=true");
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidOperationException("Malformed exterior NIF was admitted.");
    }
    private static void Net(BinaryWriter w) { w.Write(0); w.Write(0U); w.Write(-1); }
    private static void Av(BinaryWriter w)
    {
        Net(w); w.Write((ushort)14); w.Write((ushort)0);
        foreach (var value in new float[] { 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 1 }) w.Write(value);
        w.Write(0U); w.Write(-1);
    }
    private static void Text(BinaryWriter w, string text) { w.Write(text.Length); w.Write(Encoding.ASCII.GetBytes(text)); }
    private static byte[] Bytes(Action<BinaryWriter> write)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        write(writer); return stream.ToArray();
    }
    private static byte[] File((string Type, byte[] Payload)[] blocks) => Bytes(w =>
    {
        w.Write(Encoding.ASCII.GetBytes("Gamebryo File Format, Version 20.2.0.7\n"));
        w.Write(FalloutNifFile.Version); w.Write((byte)1); w.Write(FalloutNifFile.UserVersion);
        w.Write(blocks.Length); w.Write(34U); w.Write(new byte[] { 1, 0, 1, 0, 1, 0 });
        w.Write((ushort)blocks.Length);
        foreach (var block in blocks) Text(w, block.Type);
        for (var index = 0; index < blocks.Length; index++) w.Write((ushort)index);
        foreach (var block in blocks) w.Write(block.Payload.Length);
        w.Write(1U); w.Write(7U); Text(w, "fixture"); w.Write(0U);
        foreach (var block in blocks) w.Write(block.Payload);
        w.Write(1U); w.Write(0);
    });
}
