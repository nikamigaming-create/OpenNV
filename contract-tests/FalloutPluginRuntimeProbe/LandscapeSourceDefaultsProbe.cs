using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using OpenNV.Runtime.Content;

internal static class LandscapeSourceDefaultsProbe
{
    private static readonly FalloutLandscapeTexture DefaultTexture = new(null, "", null, "",
        "textures/landscape/authored-contract.dds", null);

    internal static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-land-source-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            Planes(directory);
            SparseAndAuthored(directory);
            Inheritance(directory);
            Malformed(directory);
            Console.WriteLine("OPENNV_LAND_REFERENCE_SOURCE_PASS planes=true constantWorldHeights=true sparseColorsAndLayers=true sourceNormals=true noPersistentRequirement=true exactParentAndOverride=true malformedRefused=true sourceUnchanged=true native=unverified");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static void Planes(string directory)
    {
        var file = Path.Combine(directory, "Rectangle.esm");
        var bodies = new List<byte[]>();
        for (uint index = 0; index < 3; index++)
            bodies.Add(Record("REFR", 0x900 + index, 0, Field("XPRM", Primitive(3, 3 + index, 0, 8 + index))));
        bodies.Add(Record("REFR", 0x910, 0, Field("XPRM", Primitive(0, 0, 0, 0))));
        bodies.Add(Record("REFR", 0x911, 0, Field("XPRM", Primitive(1, 2, 3, 4))));
        bodies.Add(Record("REFR", 0x912, 0, Field("XPRM", Primitive(2, 5, 5, 5))));
        Write(file, [], Join(bodies));
        var original = Hash(file);
        using var stack = Load(file);
        for (uint index = 0; index < 3; index++)
        {
            var source = stack.GetEffective(new("Rectangle.esm", 0x900 + index));
            var plane = FalloutReferencePrimitive.Read(source)!;
            Require(plane is { Type: 3, Y: 0, IsPlanar: true } && plane.X == 3 + index && plane.Z == 8 + index,
                "The full record reader rejected or inflated an authored planar primitive.");
            Require(source.ReadSubrecords().Single().Data.Span.SequenceEqual(Primitive(3, 3 + index, 0, 8 + index)),
                "Primitive admission modified the original declared bytes.");
        }
        Require(FalloutReferencePrimitive.Read(stack.GetEffective(new("Rectangle.esm", 0x910))) is { Type: 0, X: 0, Y: 0, Z: 0 },
            "A declared None primitive acquired nonzero bounds.");
        Require(FalloutReferencePrimitive.Read(stack.GetEffective(new("Rectangle.esm", 0x911))) is { Type: 1, X: 2, Y: 3, Z: 4 } &&
            FalloutReferencePrimitive.Read(stack.GetEffective(new("Rectangle.esm", 0x912))) is { Type: 2, X: 5, Y: 5, Z: 5 },
            "Plane admission changed independent box/sphere declarations.");
        Require(Hash(file) == original, "Primitive source bytes changed.");
    }

    private static void SparseAndAuthored(string directory)
    {
        var file = Path.Combine(directory, "Aster.esm");
        var colors = Enumerable.Repeat((byte)54, 33 * 33 * 3).ToArray();
        var heights = new byte[sizeof(float) + 33 * 33 + 3];
        BinaryPrimitives.WriteSingleLittleEndian(heights, 2.5f);
        heights[4] = 1; heights[5] = 255;
        var normals = new byte[33 * 33 * 3];
        for (var vertex = 0; vertex < 33 * 33; vertex++) normals[vertex * 3 + 2] = 127;
        var layer = new byte[8]; // Explicit null texture selects its quadrant default.
        var opacity = new byte[8];
        BinaryPrimitives.WriteSingleLittleEndian(opacity.AsSpan(4), .25f);
        var lands = new[]
        {
            CellAndLand(0x400, 0x500, 0x600, 0, Field("DATA", U32(0))),
            CellAndLand(0x400, 0x501, 0x601, 1, Join(Field("DATA", U32(2)), Field("VCLR", colors))),
            CellAndLand(0x400, 0x502, 0x602, 2, Join(Field("DATA", U32(4)), Field("BTXT", layer), Field("ATXT", layer), Field("VTXT", opacity))),
            CellAndLand(0x400, 0x503, 0x603, 3, Join(Field("DATA", U32(1)), Field("VHGT", heights), Field("VNML", normals))),
        };
        Write(file, [], Join(World(0x400, 91.25f), Join(lands)));
        var original = Hash(file);
        using var stack = Load(file);
        for (uint index = 0; index < 3; index++)
        {
            var cell = FalloutCellSceneReader.ReadDefinition(stack, new("Aster.esm", 0x500 + index));
            var land = FalloutLandscapeTransportResolver.ResolveCell(stack, cell, DefaultTexture);
            Require(land.ActiveCell == cell.FormKey && land.Worldspace == new FalloutFormKey("Aster.esm", 0x400) &&
                land.Heights.Length == 1089 && land.Heights.All(height => height == 91.25f),
                "Sparse LAND did not use its exact declared world default height.");
            for (var vertex = 0; vertex < 1089; vertex++)
                Require(land.Normals[vertex * 3] == 0 && land.Normals[vertex * 3 + 1] == 0 && land.Normals[vertex * 3 + 2] == 1,
                    "The source constant-height plane normal differs.");
            if (index == 1) Require(land.Colors.SequenceEqual(colors), "Sparse LAND discarded authored colors.");
            if (index == 2) Require(land.AlphaLayers.Single() is { Quadrant: 0, UsesQuadrantDefault: true } &&
                land.AlphaLayers.Single().Opacities.Single().Opacity == .25f,
                "Sparse LAND discarded or fabricated authored material opacity.");
        }
        var authored = FalloutLandscapeTransportResolver.ResolveCell(stack,
            FalloutCellSceneReader.ReadDefinition(stack, new("Aster.esm", 0x503)), DefaultTexture);
        Require(authored.Heights[0] == 28 && authored.Heights[1] == 20 && authored.Heights[2] == 20 &&
            authored.Heights[33] == 28 && authored.Normals[2] == 1,
            "A world default replaced an independent authored height/normal declaration.");
        Require(Hash(file) == original, "LAND source bytes changed.");

        // A different identity and negative world height use the same source owner.
        var cedar = Path.Combine(directory, "Cedar.esm");
        Write(cedar, [], Join(World(0x7a0, -58.5f), CellAndLand(0x7a0, 0x7b0, 0x7c0, -7, Field("DATA", U32(0)))));
        using var other = Load(cedar);
        var independent = FalloutLandscapeTransportResolver.ResolveCell(other,
            FalloutCellSceneReader.ReadDefinition(other, new("Cedar.esm", 0x7b0)), DefaultTexture);
        Require(independent.Heights.All(height => height == -58.5f) && independent.ActiveCoordinates == (-7, 0),
            "Landscape defaults depend on a named identity or positive coordinate/height.");
    }

    private static void Inheritance(string directory)
    {
        var cedar = Path.Combine(directory, "FirstMaster.esm");
        var aster = Path.Combine(directory, "SecondMaster.esm");
        var child = Path.Combine(directory, "Child.esp");
        Write(cedar, [], World(0x400, -900));
        Write(aster, [], World(0x400, 71.5f));
        Write(child, ["FirstMaster.esm", "SecondMaster.esm"], Join(
            World(0x02000410, 99, 0x01000400, 1),
            World(0x02000411, 33, 0x01000400, 4),
            CellAndLand(0x02000410, 0x02000510, 0x02000610, 0, Field("DATA", U32(0))),
            CellAndLand(0x02000411, 0x02000511, 0x02000611, 1, Field("DATA", U32(0)))));
        var original = Hash(child);
        using (var stack = Load(cedar, aster, child))
        {
            var inherited = FalloutWorldspaceLandDefaults.Read(stack, new("Child.esp", 0x410));
            Require(inherited.LandHeight == 71.5f && inherited.OwnerWorldspace == new FalloutFormKey("SecondMaster.esm", 0x400) &&
                inherited.Selection.Count == 2 && inherited.Selection[0].Parent == inherited.OwnerWorldspace,
                "WRLD land inheritance guessed the first master or the child's ignored default.");
            Require(FalloutWorldspaceLandDefaults.Read(stack, new("Child.esp", 0x411)).LandHeight == 33,
                "Use Map Data inherited unrelated landscape data.");
            var land = FalloutLandscapeTransportResolver.ResolveCell(stack,
                FalloutCellSceneReader.ReadDefinition(stack, new("Child.esp", 0x510)), DefaultTexture);
            Require(land.Heights.All(height => height == 71.5f), "LAND lost exact master-scoped inherited world heights.");
        }
        var patch = Path.Combine(directory, "WorldOverride.esp");
        Write(patch, ["SecondMaster.esm"], World(0x400, 128.75f));
        using (var stack = Load(cedar, aster, child, patch))
        {
            var inherited = FalloutWorldspaceLandDefaults.Read(stack, new("Child.esp", 0x410));
            Require(inherited.LandHeight == 128.75f && inherited.Selection[^1].Winner == "WorldOverride.esp" &&
                inherited.OwnerWorldspace == new FalloutFormKey("SecondMaster.esm", 0x400),
                "WRLD defaults lost the winning override or rewrote its declaring master identity.");
        }
        Write(patch, ["SecondMaster.esm"], Record("WRLD", 0x400, 0x20, []));
        using (var stack = Load(cedar, aster, child, patch))
            Reject(() => FalloutWorldspaceLandDefaults.Read(stack, new("Child.esp", 0x410)), "Deleted parent owner was substituted.");
        Require(Hash(child) == original, "Parent/override inspection changed the child source.");
    }

    private static void Malformed(string directory)
    {
        var path = Path.Combine(directory, "Malformed.esm");
        foreach (var primitive in new[] { Primitive(1, 2, 0, 4), Primitive(3, 0, 0, 4), Primitive(3, 2, -1, 4),
            Primitive(3, 2, 0, float.NaN), Primitive(3, float.PositiveInfinity, 0, 4), new byte[31] })
        {
            Write(path, [], Record("REFR", 0x900, 0, Field("XPRM", primitive)));
            using var stack = Load(path);
            Reject(() => FalloutReferencePrimitive.Read(stack.GetEffective(new("Malformed.esm", 0x900))), "Malformed primitive was admitted.");
        }
        Write(path, [], Record("REFR", 0x900, 0, Join(Field("XPRM", Primitive(3, 2, 0, 4)), Field("XPRM", Primitive(3, 2, 0, 4)))));
        using (var stack = Load(path))
            Reject(() => FalloutReferencePrimitive.Read(stack.GetEffective(new("Malformed.esm", 0x900))), "Duplicate primitive was admitted.");
        foreach (var fields in new[]
        {
            Join(Field("DATA", U32(1))),
            Join(Field("DATA", U32(0)), Field("VHGT", new byte[1095])),
            Join(Field("DATA", U32(0)), Field("VNML", new byte[3266])),
            Join(Field("DATA", U32(0)), Field("VCLR", new byte[3266])),
            Join(Field("DATA", U32(0)), Field("ATXT", new byte[8])),
            Join(Field("DATA", U32(0)), Field("VTXT", new byte[8])),
            Join(Field("DATA", U32(0)), Field("VHGT", new byte[1096]), Field("VHGT", new byte[1096])),
        })
        {
            Write(path, [], Join(World(0x400, 10), CellAndLand(0x400, 0x500, 0x600, 0, fields)));
            using var stack = Load(path);
            Reject(() => FalloutLandscapeTransportResolver.ResolveCell(stack,
                FalloutCellSceneReader.ReadDefinition(stack, new("Malformed.esm", 0x500)), DefaultTexture), "Malformed sparse LAND was admitted.");
        }
        foreach (var fields in new[]
        {
            Array.Empty<byte>(), Field("DNAM", new byte[7]),
            Join(Field("DNAM", Pair(10, 0)), Field("DNAM", Pair(20, 0))),
            Field("DNAM", Pair(float.NaN, 0)), Field("DNAM", Pair(10, float.PositiveInfinity)),
            Join(Field("PNAM", U16(1)), Field("DNAM", Pair(10, 0))),
            Join(Field("WNAM", U32(0x999)), Field("PNAM", U16(1))),
            Join(Field("WNAM", U32(0x401)), Field("PNAM", U16(1)), Field("PNAM", U16(1))),
            Join(Field("WNAM", new byte[3]), Field("DNAM", Pair(10, 0))),
            Join(Field("WNAM", U32(0x401)), Field("PNAM", U16(1)), Field("DNAM", new byte[7])),
            Join(Field("WNAM", U32(0x401)), Field("PNAM", U16(1)), Field("DNAM", Pair(float.NaN, 0))),
        })
        {
            Write(path, [], Join(Record("WRLD", 0x400, 0, fields), World(0x401, 20)));
            using var stack = Load(path);
            Reject(() => FalloutWorldspaceLandDefaults.Read(stack, new("Malformed.esm", 0x400)), "Malformed/missing world default owner was admitted.");
        }
        Write(path, [], Join(World(0x400, 10, 0x401, 1), World(0x401, 20, 0x400, 1)));
        using (var stack = Load(path))
            Reject(() => FalloutWorldspaceLandDefaults.Read(stack, new("Malformed.esm", 0x400)), "Cyclic land inheritance was admitted.");
        Write(path, [], Join(World(0x400, 10, 0x401, 1), Record("GLOB", 0x401, 0, Field("FLTV", Pair(0, 0)))));
        using (var stack = Load(path))
            Reject(() => FalloutWorldspaceLandDefaults.Read(stack, new("Malformed.esm", 0x400)), "Non-WRLD parent was admitted.");
    }

    private static byte[] Primitive(uint type, float x, float y, float z)
    {
        var result = new byte[32];
        BinaryPrimitives.WriteSingleLittleEndian(result, x);
        BinaryPrimitives.WriteSingleLittleEndian(result.AsSpan(4), y);
        BinaryPrimitives.WriteSingleLittleEndian(result.AsSpan(8), z);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(28), type);
        return result;
    }
    private static byte[] World(uint form, float height, uint? parent = null, ushort parentFlags = 0) =>
        Record("WRLD", form, 0, Join(Field("DATA", [1]), Field("DNAM", Pair(height, 0)),
            parent is { } link ? Join(Field("WNAM", U32(link)), Field("PNAM", U16(parentFlags))) : []));
    private static byte[] CellAndLand(uint world, uint cell, uint land, int x, byte[] fields) =>
        Group(world, 1, Join(Record("CELL", cell, 0, Join(Field("DATA", U16(0)), Field("XCLC", Coordinates(x, 0)))),
            Group(cell, 6, Record("LAND", land, 0, fields))));
    private static byte[] Coordinates(int x, int y)
    {
        var bytes = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, x);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), y);
        return bytes;
    }
    private static byte[] Pair(float x, float y)
    {
        var bytes = new byte[8];
        BinaryPrimitives.WriteSingleLittleEndian(bytes, x);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(4), y);
        return bytes;
    }
    private static byte[] U32(uint value) { var bytes = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(bytes, value); return bytes; }
    private static byte[] U16(ushort value) { var bytes = new byte[2]; BinaryPrimitives.WriteUInt16LittleEndian(bytes, value); return bytes; }
    private static byte[] Field(string name, byte[] value)
    {
        var bytes = new byte[6 + value.Length];
        Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), checked((ushort)value.Length));
        value.CopyTo(bytes, 6);
        return bytes;
    }
    private static byte[] Record(string name, uint id, uint flags, byte[] value)
    {
        var bytes = new byte[24 + value.Length];
        Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)value.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), flags);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), id);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(20), 15);
        value.CopyTo(bytes, 24);
        return bytes;
    }
    private static byte[] Group(uint label, int type, byte[] contents)
    {
        var bytes = new byte[24 + contents.Length];
        "GRUP"u8.CopyTo(bytes);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), label);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), type);
        contents.CopyTo(bytes, 24);
        return bytes;
    }
    private static byte[] Join(params byte[][] values) => Join((IEnumerable<byte[]>)values);
    private static byte[] Join(IEnumerable<byte[]> values) => values.SelectMany(value => value).ToArray();
    private static void Write(string file, string[] masters, byte[] records)
    {
        var header = new byte[12];
        BinaryPrimitives.WriteSingleLittleEndian(header, 1.34f);
        File.WriteAllBytes(file, Join(Record("TES4", 0, 1, Join(Field("HEDR", header), Join(masters.Select(master =>
            Join(Field("MAST", Encoding.ASCII.GetBytes(master + "\0")), Field("DATA", new byte[8])))))), records));
    }
    private static FalloutPluginStack Load(params string[] paths) => FalloutPluginStack.Load(paths
        .Select(path => new FalloutPluginSource(Path.GetFileName(path), path)).ToArray());
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    private static void Reject(Action action, string message)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or FalloutPluginFormatException or KeyNotFoundException) { return; }
        throw new InvalidDataException(message);
    }
}
