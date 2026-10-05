using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static class PackageDataContracts
{
    internal static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-package-data-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var packages = new List<byte[]>();
            foreach (var extent in new[] { 8, 12 })
            {
                var first = extent == 8 ? 0x400u : 0x420u;
                packages.AddRange(new[]
                {
                    Package(first, extent, 0, 0, Location(0), Target(2, 11)),
                    Package(first + 1, extent, 1, 0, Target(0, 0x900, 100)),
                    Package(first + 2, extent, 2, 2, Location(0), Target(0, 0x900), Field("PKE2", BitConverter.GetBytes(100u))),
                    Package(first + 3, extent, 6, 0, Location(3)),
                    Package(first + 4, extent, 13, 0, Location(0), Field("PKPT", [0, 0])),
                    Package(first + 5, extent, 14, 0x10001000, Location(3)),
                    Package(first + 6, extent, 15, 0, Target(0, 0x900, 100), Field("PKDD", Dialogue())),
                    Record("PACK", first + 7, Field("PKDT", Data(extent, 17, 0x89abcdef, 0x1234, 0x5678))),
                });
            }
            var lengths = new[] { 0, 1, 4, 5, 6, 7, 9, 10, 11, 13, 16 };
            packages.AddRange(lengths.Select((size, index) => Record("PACK", 0x500u + (uint)index, Field("PKDT", new byte[size]))));
            packages.Add(Record("PACK", 0x510));
            packages.Add(Record("PACK", 0x511, Field("PKDT", Data(8, 6)), Field("PKDT", Data(12, 6))));
            packages.Add(Package(0x480, 12, 6, 0, Location(3), Field("CTDA", Condition())));
            packages.Add(Package(0x481, 8, 6, 0, Location(3)));
            packages.Add(Package(0x482, 8, 6, 0, Location(3), behavior: 1));
            packages.Add(Package(0x483, 12, 6, 0, Location(3), specific: 1));
            packages.Add(Record("PACK", 0x484, Field("EDID", "NoSchedule\0"u8.ToArray()), Field("PKDT", Data(8, 6)), Location(3)));
            File.WriteAllBytes(Path.Combine(directory, "Base.esm"), Join(
                Record("TES4", 0, Field("HEDR", new byte[12])),
                Record("NPC_", 0x700, Field("ACBS", new byte[24]),
                    Field("PKID", BitConverter.GetBytes(0x480u)), Field("PKID", BitConverter.GetBytes(0x481u))),
                Record("STAT", 0x3b), Record("CELL", 0x800, Field("DATA", [1])), References(), Join(packages.ToArray())));
            using var records = FalloutPluginStack.Load(directory, ["Base.esm"]);
            FalloutFormKey Key(uint id) => new("Base.esm", id);
            var hashes = records.EffectiveRecords("PACK").ToDictionary(record => record.FormKey,
                record => Convert.ToHexString(SHA256.HashData(record.ReadData())));
            foreach (var extent in new[] { 8, 12 })
            {
                var first = extent == 8 ? 0x400u : 0x420u;
                FalloutPluginRecord Source(uint offset) => records.GetEffective(Key(first + offset));
                var data = FalloutPackageData.Read(Source(7));
                Require(data.Flags == 0x89abcdef && data.Procedure == 17 && data.BehaviorFlags == 0x1234 &&
                    data.SourceExtent == extent && data.SpecificFlags == (extent == 8 ? null : (ushort?)0x5678),
                    "Package data lost common fields, source extent or absent type-specific flags.");
                Require(FalloutScriptPackage.Read(Source(3)) is { Procedure: 6, LocationType: 3, Flags: 0 },
                    "Generic package declaration rejected a supported source extent.");
                Require(FalloutFindFurniturePackage.Read(Source(0)).TargetType == 2 &&
                    FalloutFollowPackage.Read(Source(1)).Distance == 100 &&
                    FalloutEscortPackage.Read(Source(2)).Distance == 100 &&
                    FalloutEditorTravelPackage.Read(Source(3)).Radius == 0 &&
                    FalloutTravelPackage.Read(Source(3)).LocationType == 3 &&
                    FalloutPatrolRoute.Read(records, Source(4), Key(0x900)).Points.Single().Reference == Key(0x902) &&
                    !FalloutGuardPackage.Read(Source(5)).WarnAndAttack &&
                    FalloutDialoguePackage.Read(Source(6)).ActivationDistance == 100,
                    "A procedure reader retained its obsolete package-data extent restriction.");
            }
            foreach (var id in lengths.Select((_, index) => 0x500u + (uint)index).Append(0x510u).Append(0x511u).Append(0x700u))
                Reject(() => FalloutPackageData.Read(records.GetEffective(Key(id))));
            Reject(() => FalloutEditorTravelPackage.Read(records.GetEffective(Key(0x482))));
            Reject(() => FalloutTravelPackage.Read(records.GetEffective(Key(0x482))));
            Reject(() => FalloutEditorTravelPackage.Read(records.GetEffective(Key(0x483))));
            Reject(() => FalloutTravelPackage.Read(records.GetEffective(Key(0x483))));
            Require(FalloutPackageData.Read(records.GetEffective(Key(0x484))).SourceExtent == 8,
                "Package data admission substituted schedule validation for its source extent.");
            Reject(() => FalloutPackageSchedule.Read(records.GetEffective(Key(0x484))));
            using var world = new FalloutReferenceWorld(records);
            var queries = 0; var eligible = new List<FalloutFormKey>();
            var selected = FalloutAiPackages.Select(records, Key(0x700), _ => { queries++; return 0; },
                eligible: record => { eligible.Add(record.FormKey); return world.PackageEligible(Key(0x900), record, null, null, false); });
            Require(selected?.FormKey == Key(0x481) && queries == 1 && eligible.SequenceEqual([Key(0x480), Key(0x481)]),
                "Priority selection invented a schedule or skipped the shorter package declaration.");
            Require(hashes.All(pair => pair.Value == Convert.ToHexString(SHA256.HashData(records.GetEffective(pair.Key).ReadData()))),
                "Package data admission changed source bytes.");
            Console.WriteLine("OPENNV_PACKAGE_DATA_CONTRACT_PASS extents=8,12 absentSpecificFlags=true procedures=9 malformedRejected=true unsupportedFlagsRejected=true priority=true authoredSchedule=true sourceUnchanged=true");
        }
        finally { File.Delete(Path.Combine(directory, "Base.esm")); Directory.Delete(directory); }
    }

    private static byte[] Data(int extent, byte procedure, uint flags = 0, ushort behavior = 0, ushort specific = 0)
    {
        var data = new byte[extent]; BinaryPrimitives.WriteUInt32LittleEndian(data, flags); data[4] = procedure; data[5] = 0xcd;
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(6), behavior);
        if (extent == 12) { BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(8), specific); data[10] = data[11] = 0xcd; }
        return data;
    }
    private static byte[] Package(uint id, int extent, byte procedure, uint flags, byte[]? location = null,
        byte[]? target = null, byte[]? extra = null, ushort behavior = 0, ushort specific = 0) => Record("PACK", id,
        Field("EDID", Encoding.ASCII.GetBytes("SyntheticPackage" + id + '\0')), Field("PKDT", Data(extent, procedure, flags, behavior, specific)),
        Field("PSDT", [255, 255, 0, 255, 0, 0, 0, 0]), location ?? [], target ?? [], extra ?? []);
    private static byte[] Location(int type)
    {
        var data = new byte[12]; BinaryPrimitives.WriteInt32LittleEndian(data, type);
        if (type == 0) BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), 0x902); return Field("PLDT", data);
    }
    private static byte[] Target(int type, uint form, int distance = 0)
    {
        var data = new byte[16]; BinaryPrimitives.WriteInt32LittleEndian(data, type);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), form); BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8), distance);
        return Field("PTDT", data);
    }
    private static byte[] Dialogue() { var data = new byte[24]; BinaryPrimitives.WriteSingleLittleEndian(data, 45); return data; }
    private static byte[] Condition()
    {
        var data = new byte[28]; BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(4), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(8), 14); return data;
    }
    private static byte[] References()
    {
        var pose = new float[] { 1, 2, 3, 0, 0, 0 }.SelectMany(BitConverter.GetBytes).ToArray();
        var references = Join(Record("ACHR", 0x900, Field("NAME", BitConverter.GetBytes(0x700u)), Field("DATA", pose)),
            Record("REFR", 0x902, Field("NAME", BitConverter.GetBytes(0x3bu)), Field("DATA", pose)));
        var group = new byte[24 + references.Length]; Encoding.ASCII.GetBytes("GRUP").CopyTo(group, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(4), (uint)group.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(8), 0x800); BinaryPrimitives.WriteInt32LittleEndian(group.AsSpan(12), 6);
        references.CopyTo(group, 24); return group;
    }
    private static byte[] Field(string name, byte[] data)
    {
        var bytes = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), checked((ushort)data.Length)); data.CopyTo(bytes, 6); return bytes;
    }
    private static byte[] Record(string name, uint id, params byte[][] fields)
    {
        var data = Join(fields); var bytes = new byte[24 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)data.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), id); data.CopyTo(bytes, 24); return bytes;
    }
    private static byte[] Join(params byte[][] values) => values.SelectMany(value => value).ToArray();
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidOperationException("Unowned package declaration was accepted.");
    }
}
