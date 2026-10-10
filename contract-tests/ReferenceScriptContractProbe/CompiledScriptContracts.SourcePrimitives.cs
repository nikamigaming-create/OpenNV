using System.Security.Cryptography;
using System.Text;
using OpenNV.Runtime.Content;

internal static partial class CompiledScriptContracts
{
    internal static void RunSourcePrimitives()
    {
        const string engine = "c3f97c2255fa041a851c17cf372d69aaadd8694e2dc4230ba556001bbfbd2f3e";
        var source = FalloutDynamicPrimitiveSource.Read(engine);
        var directory = Path.Combine(Path.GetTempPath(), "opennv-source-primitives-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "Bytecode.esm");
        try
        {
            byte[] Primitive(float x, float y, float z, uint type) => Join(
                BitConverter.GetBytes(x), BitConverter.GetBytes(y), BitConverter.GetBytes(z), new byte[16], U32(type));
            byte[] Placed(uint id, uint basis, params byte[][] fields) => Record("REFR", id,
                Field("NAME", U32(basis)), Field("DATA", new byte[24]), Join(fields));
            byte[] Envelope(byte[] portal, byte[]? plane = null) => Join(Tes4(),
                Record("STAT", 0x1f, Field("EDID", Text("AuthoredRoomFactoryBase"))),
                Record("ACTI", 0x90, Field("EDID", Text("AuthoredSphereBase"))),
                Record("CELL", 0x800, Field("DATA", [1])), PrimitiveSourceGroup(
                    Placed(0x901, 0x1f, Field("XPRM", Primitive(9, 11, 13, 1))),
                    Placed(0x902, 0x1f, Field("XPRM", Primitive(17, 19, 23, 1))), portal,
                    plane ?? Placed(0x903, 0x17, Field("XPRM", Primitive(3, 1, 5, 3)),
                        Field("XMBO", Join(BitConverter.GetBytes(3f), BitConverter.GetBytes(0f), BitConverter.GetBytes(5f)))),
                    Placed(0x904, 0x90, Field("XPRM", Primitive(3, -7, 11, 2)))));
            var original = Envelope(Placed(0x900, 0x20, Field("XPRM", Primitive(3, 1, 5, 3)),
                Field("XPOD", Join(U32(0x902), U32(0x901))), Field("XRMR", [2, 0, 1, 0]),
                Field("XLRM", U32(0x901)), Field("XLRM", U32(0x902))));
            File.WriteAllBytes(path, original);
            var hash = SHA256.HashData(original);
            using (var records = FalloutPluginStack.Load(directory, ["Bytecode.esm"]))
            {
                var cell = FalloutCellSceneReader.Read(records, Key(0x800));
                var portal = FalloutDynamicPrimitive.Read(source, records, cell.References.Single(row => row.FormKey == Key(0x900)));
                var plane = FalloutDynamicPrimitive.Read(source, records, cell.References.Single(row => row.FormKey == Key(0x903)));
                var sphere = FalloutReferencePrimitive.Read(records.GetEffective(Key(0x904)))!;
                Require(portal.Role == FalloutDynamicPrimitiveRole.Portal &&
                    portal.PortalRooms.SequenceEqual([Key(0x902), Key(0x901)]) &&
                    portal.LinkedRooms.SequenceEqual([Key(0x901), Key(0x902)]) && portal.RoomFlags == 1 &&
                    plane.Role == FalloutDynamicPrimitiveRole.OcclusionPlane && plane.Primitive.IsPlanar &&
                    plane.Primitive.Y == 1 && plane.BoundHalfExtents!.SequenceEqual([3f, 0f, 5f]) &&
                    sphere.X == 3 && sphere.Y == -7 && sphere.Z == 11 && sphere.Type == 2 &&
                    source.Role(records, Key(0x90)) == FalloutDynamicPrimitiveRole.None,
                    "Full-reader primitive roles, ignored scalar lanes or physical room-slot order changed.");
                Require(portal.SourceSha256 == Convert.ToHexString(SHA256.HashData(records.GetEffective(Key(0x900)).ReadData())).ToLowerInvariant(),
                    "Primitive identity is not the exact winning source body.");
                Reject(() => FalloutDynamicPrimitiveSource.Read(new('f', 64)));
                Require(!FalloutDynamicPrimitiveSource.Supports(new('f', 64)), "Unknown engine factory gained source admission.");
            }
            Require(hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(path))), "Primitive reader changed its authored source file.");
            var negativeFields = new byte[][][]
            {
                [Field("XPRM", Primitive(3, 1, 5, 3)), Field("XPOD", U32(0x901))],
                [Field("XPRM", Primitive(3, 1, 5, 3)), Field("XPOD", Join(U32(0x901), U32(0x902))), Field("XPOD", Join(U32(0x902), U32(0x901)))],
                [Field("XPRM", Primitive(3, 1, 5, 3)), Field("XPOD", Join(U32(0x90), U32(0x902)))],
                [Field("XPRM", Primitive(3, 1, 5, 3)), Field("XPOD", Join(U32(0x901), U32(0x902))), Field("XRMR", [2, 0, 0, 0]), Field("XLRM", U32(0x901))],
                [Field("XPRM", Primitive(3, 1, 5, 1)), Field("XPOD", Join(U32(0x901), U32(0x902)))],
                [Field("XPRM", Primitive(3, 1, 5, 3)), Field("XPOD", Join(U32(0x901), U32(0x902))), Field("XORD", new byte[16])],
                [Field("XPRM", Primitive(3, 1, 5, 3)), Field("XPRM", Primitive(3, 1, 5, 3)), Field("XPOD", Join(U32(0x901), U32(0x902)))],
                [Field("XPRM", Primitive(0, 1, 5, 3)), Field("XPOD", Join(U32(0x901), U32(0x902)))],
                [Field("XPRM", Primitive(3, float.NaN, 5, 3)), Field("XPOD", Join(U32(0x901), U32(0x902)))],
            };
            foreach (var fields in negativeFields)
            {
                File.WriteAllBytes(path, Envelope(Placed(0x900, 0x20, fields)));
                using var records = FalloutPluginStack.Load(directory, ["Bytecode.esm"]);
                Reject(() =>
                {
                    var cell = FalloutCellSceneReader.Read(records, Key(0x800));
                    FalloutDynamicPrimitive.Read(source, records, cell.References.Single(row => row.FormKey == Key(0x900)));
                });
            }
            File.WriteAllBytes(path, Envelope(Placed(0x900, 0x20, Field("XPRM", Primitive(3, 1, 5, 3)),
                Field("XPOD", Join(U32(0), U32(0x901))))));
            using (var records = FalloutPluginStack.Load(directory, ["Bytecode.esm"]))
            {
                var cell = FalloutCellSceneReader.Read(records, Key(0x800));
                var nullable = FalloutDynamicPrimitive.Read(source, records, cell.References.Single(row => row.FormKey == Key(0x900)));
                Require(nullable.PortalRooms.SequenceEqual([null, Key(0x901)]), "Null physical XPOD slot was removed or retargeted.");
            }
            Console.WriteLine("OPENNV_SOURCE_DYNAMIC_PRIMITIVES_PASS fullReader=true engineRole=true " +
                "planeStoredMiddleBound=true sphereFirstBound=true orderedRooms=true nullSlotPreserved=true " +
                "exactSourceBody=true negativeReaders=9 originalNativePortalCulling=UNEXECUTED");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static byte[] PrimitiveSourceGroup(params byte[][] bodies)
    {
        var payload = Join(bodies); var value = new byte[24 + payload.Length];
        Encoding.ASCII.GetBytes("GRUP").CopyTo(value, 0); UInt(value, 4, (uint)value.Length);
        UInt(value, 8, 0x800); UInt(value, 12, 6); payload.CopyTo(value, 24); return value;
    }
}
