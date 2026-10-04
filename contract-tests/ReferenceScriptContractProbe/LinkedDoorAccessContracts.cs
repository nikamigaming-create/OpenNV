using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class LinkedDoorAccessContracts
{
    internal static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-linked-door-access-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var plugin = Fixture(); var patch = Patch();
            File.WriteAllBytes(Path.Combine(directory, "Links.esm"), plugin);
            File.WriteAllBytes(Path.Combine(directory, "LinksPatch.esp"), patch);
            using var records = FalloutPluginStack.Load(directory, ["Links.esm", "LinksPatch.esp"]);
            using var world = new FalloutReferenceWorld(records);
            var first = Key(0x90); var second = PatchKey(0x120);
            Require(world.GetLocked(first) == 0 && world.GetLockLevel(first) == 0 && world.Lock(first) is null &&
                world.UnlockWithKey(first, new FalloutPlayerInventory()) && world.InstanceCount == 1 &&
                world.ResidentCellCount == 0, "Source-unlocked pair created a lock or a destination instance/residency.");
            Require(world.GetLocked(second) == 0 && world.GetLockLevel(second) == 0 && world.Lock(second) is null,
                "Reciprocal ESP-local link did not adjust its declared master identities.");
            var effects = 0;
            var scripts = new FalloutReferenceScripts(records, world, new(records), new((_, _) => false, _ => ++effects));
            void Run(string source) => scripts.ExecuteProgram(records.GetEffective(first), records.GetEffective(Key(0x50)),
                FalloutGameModeProgram.Read("begin GameMode\n" + source + "\nend"), 0);
            Run("set sample to LinkedA.GetLocked\nLinkedA.Unlock");
            Require(world.Get(first).Read(1) == 0 && world.Get(first).LockState is null,
                "Source query or Unlock fabricated an absent teleport lock.");
            Run("LinkedA.Lock 75\nset sample to LinkedA.GetLocked");
            Require(world.GetLocked(first) == 1 && world.GetLockLevel(first) == 75 && world.Lock(first)?.Level == 75 &&
                world.Get(first).Read(1) == 1 && !world.Get(first).DoorOpen && effects == 0,
                "A real script lock on the calling side did not block access or changed door motion.");
            Reject(() => world.GetLocked(second)); Reject(() => world.Lock(second));
            Reject(() => world.UnlockWithKey(second, new FalloutPlayerInventory()));
            Require(world.GetLocked(first) == 1, "Opposite-side refusal silently unlocked the actual locked side.");
            var locked = RoundTrip(world.Capture());
            foreach (var snapshots in new[] { locked, locked.Reverse().ToArray() })
            {
                using var cold = new FalloutReferenceWorld(records); cold.Restore(snapshots);
                Require(cold.InstanceCount == 2 && cold.GetLocked(first) == 1 && cold.GetLockLevel(first) == 75,
                    "Cold linked lock changed state, instantiated an unsaved reference or depended on snapshot order.");
                Reject(() => cold.GetLocked(second));
                cold.UnlockReference(first);
                Require(cold.GetLocked(first) == 0 && cold.GetLockLevel(first) == 75,
                    "Cold Unlock discarded retained linked-door difficulty.");
                Reject(() => cold.GetLockLevel(second));
            }
            Run("LinkedA.Unlock\nLinkedA.Lock");
            Require(world.GetLocked(first) == 1 && world.GetLockLevel(first) == 75,
                "Local teleport relocking lost retained source-independent difficulty.");
            using (var opposite = new FalloutReferenceWorld(records))
            {
                opposite.LockReference(second, 50);
                Require(opposite.GetLocked(second) == 1 && opposite.GetLockLevel(second) == 50,
                    "Opposite-side actual lock was lost.");
                Reject(() => opposite.GetLocked(first));
                var both = locked.Single(snapshot => snapshot.Reference == first);
                var other = opposite.Capture().Single(snapshot => snapshot.Reference == second);
                foreach (var snapshots in new[] { new[] { both, other }, new[] { other, both } })
                {
                    using var rejected = new FalloutReferenceWorld(records);
                    Reject(() => rejected.Restore(snapshots));
                    Require(rejected.InstanceCount == 0, "Unowned simultaneous linked locks partially restored.");
                }
            }
            using (var legacy = new FalloutReferenceWorld(records))
            {
                legacy.Restore([locked.Single(snapshot => snapshot.Reference == first) with { LockState = null, Unlocked = true }]);
                Require(legacy.GetLocked(first) == 0 && legacy.GetLockLevel(first) == 0 &&
                    legacy.Get(first).LockState is null && legacy.GetLocked(second) == 0,
                    "Legacy unlock migration created a teleport lock where neither source declared one.");
            }
            Require(world.GetLocked(Key(0x92)) == 1 && world.GetLockLevel(Key(0x92)) == 100 &&
                world.Lock(Key(0x92))?.Key == Key(0x20), "Declared source-side key lock stopped blocking access.");
            Reject(() => world.GetLocked(Key(0x93)));
            foreach (var reference in Enumerable.Range(0x100, 16).Select(id => Key((uint)id)))
            {
                Reject(() => world.GetLocked(reference));
                Reject(() => world.LockReference(reference, 100));
                Require(world.Get(reference).LockState is null, "Rejected linked source created a dynamic lock.");
            }
            VerifyDrift(directory, plugin, patch, locked);
            Console.WriteLine("OPENNV_LINKED_DOOR_ACCESS_PASS reciprocalUnlocked=true masterAdjusted=true noDestinationInstance=true " +
                "scriptSelfLock=true oppositeSourceAndDynamicLock=refused malformedAndMissingLinks=refused coldBothOrders=true " +
                "legacyAbsent=true linkedSourceDrift=true masterContextDrift=true invalidAtomic=true effects=none " +
                "linkedLockInheritance=unbound nativeTraversalAndParity=unverified recording=false");
        }
        finally
        {
            foreach (var child in Directory.EnumerateDirectories(directory))
            { foreach (var file in Directory.EnumerateFiles(child)) File.Delete(file); Directory.Delete(child); }
            foreach (var file in Directory.EnumerateFiles(directory)) File.Delete(file);
            Directory.Delete(directory);
        }
    }

    private static void VerifyDrift(string directory, byte[] plugin, byte[] patch, FalloutReferenceSnapshot[] snapshots)
    {
        var drift = Path.Combine(directory, "drift"); Directory.CreateDirectory(drift);
        File.WriteAllBytes(Path.Combine(drift, "Links.esm"), plugin);
        File.WriteAllBytes(Path.Combine(drift, "LinksPatch.esp"), Patch("ChangedDestination"));
        using (var records = FalloutPluginStack.Load(drift, ["Links.esm", "LinksPatch.esp"]))
        {
            Require(records.GetEffective(Key(0x90)).ReadData().SequenceEqual(SourceRecordData(patch, 0x90)),
                "Linked source-drift fixture changed the calling reference.");
            using var rejected = new FalloutReferenceWorld(records);
            Reject(() => rejected.Restore(snapshots));
            Require(rejected.InstanceCount == 0, "Changed destination declaration partially restored a linked lock.");
        }
        File.WriteAllBytes(Path.Combine(drift, "Other.esm"), Join(Header("Links.esm"), Group(0x81,
            Reference(0x01000120, "OtherLinkedB", 1, Field("XTEL", Teleport(0x90))))));
        File.WriteAllBytes(Path.Combine(drift, "LinksPatch.esp"), Join(Header("Links.esm", "Other.esm"),
            patch[Header("Links.esm").Length..]));
        using var context = FalloutPluginStack.Load(drift, ["Links.esm", "Other.esm", "LinksPatch.esp"]);
        Require(context.GetEffective(Key(0x90)).ReadData().SequenceEqual(SourceRecordData(patch, 0x90)),
            "Linked master-context fixture changed the calling bytes.");
        using var rejectedContext = new FalloutReferenceWorld(context);
        Reject(() => rejectedContext.Restore(snapshots));
        Require(rejectedContext.InstanceCount == 0, "Changed linked master mapping partially restored access.");
    }

    private static byte[] Fixture()
    {
        byte[] Door(uint id, uint destination, params byte[][] fields) => Reference(id, "Invalid" + id, 1,
            [Field("XTEL", Teleport(destination)), .. fields]);
        var nan = Teleport(0x90); BinaryPrimitives.WriteSingleLittleEndian(nan.AsSpan(4), float.NaN);
        return Join(Header(), Record("DOOR", 1, Field("SCRI", BitConverter.GetBytes(0x50u))), Record("MISC", 2),
            Record("KEYM", 0x20), Script(), Record("CELL", 0x80, Field("DATA", [1])),
            Record("CELL", 0x81, Field("DATA", [1])), Record("CELL", 0x82, Field("DATA", [1])),
            Group(0x80, Reference(0x90, "LinkedA", 1),
                Door(0x92, 0x93, Field("XLOC", SourceLock(100, 0x20))), Door(0x93, 0x92)),
            Group(0x82,
                Reference(0x100, "ShortTeleport", 1, Field("XTEL", new byte[31])),
                Reference(0x101, "EmptyTeleport", 1, Field("XTEL", [])),
                Door(0x102, 0), Door(0x103, 0x200), Door(0x104, 0x20),
                Reference(0x105, "WrongSourceBase", 2, Field("XTEL", Teleport(0x90))),
                Door(0x106, 0x130), Reference(0x130, "WrongDestinationBase", 2, Field("XTEL", Teleport(0x106))),
                Door(0x107, 0x131), Reference(0x131, "MissingDestinationLink", 1),
                Door(0x108, 0x132), Door(0x132, 0x90), Door(0x109, 0x109),
                Reference(0x10a, "DuplicateTeleport", 1, Field("XTEL", Teleport(0x90)), Field("XTEL", Teleport(0x90))),
                Reference(0x10b, "NonfiniteTeleport", 1, Field("XTEL", nan)),
                Reference(0x10c, "UnknownTeleportFlags", 1, Field("XTEL", Teleport(0x90, 1))),
                Door(0x10d, 0x133), Door(0x133, 0x10d, Field("XLOC", new byte[11])),
                Door(0x10e, 0x134), Door(0x134, 0x10e, Field("XLOC", SourceLock(100, 2))),
                Door(0x10f, 0x135), Door(0x135, 0x10f, Field("XLOC", []))));
    }

    private static byte[] Patch(string name = "LinkedB") => Join(Header("Links.esm"),
        Group(0x80, Reference(0x90, "LinkedA", 1, Field("XTEL", Teleport(0x01000120)))),
        Group(0x81, Reference(0x01000120, name, 1, Field("XTEL", Teleport(0x90)))));
    private static byte[] Script() => Record("SCPT", 0x50, Field("SLSD", Local()), Field("SCVR", Text("sample")),
        Field("SCRO", BitConverter.GetBytes(0x90u)));
    private static byte[] Local() { var data = new byte[24]; UInt(data, 0, 1); data[16] = 1; return data; }
    private static FalloutReferenceSnapshot[] RoundTrip(IReadOnlyList<FalloutReferenceSnapshot> snapshots) =>
        JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(snapshots))!;
    private static FalloutFormKey Key(uint id) => new("Links.esm", id);
    private static FalloutFormKey PatchKey(uint id) => new("LinksPatch.esp", id);
    private static byte[] Teleport(uint destination, uint flags = 0)
    { var data = new byte[32]; UInt(data, 0, destination); UInt(data, 28, flags); return data; }
    private static byte[] SourceLock(byte difficulty, uint key)
    { var data = new byte[12]; data[0] = difficulty; UInt(data, 4, key); return data; }
    private static byte[] Reference(uint id, string name, uint basis, params byte[][] fields) => Record("REFR", id,
        [Field("EDID", Text(name)), Field("NAME", BitConverter.GetBytes(basis)), Field("DATA", new byte[24]), .. fields]);
    private static byte[] Header(params string[] masters) => Record("TES4", 0,
        [Field("HEDR", new byte[12]), .. masters.SelectMany(master => new[] { Field("MAST", Text(master)), Field("DATA", new byte[8]) })]);
    private static byte[] Group(uint cell, params byte[][] records)
    {
        var data = Join(records); var group = new byte[24 + data.Length]; Encoding.ASCII.GetBytes("GRUP").CopyTo(group, 0);
        UInt(group, 4, (uint)group.Length); UInt(group, 8, cell); UInt(group, 12, 6); data.CopyTo(group, 24); return group;
    }
    private static byte[] Record(string signature, uint id, params byte[][] fields)
    {
        var data = Join(fields); var record = new byte[24 + data.Length]; Encoding.ASCII.GetBytes(signature).CopyTo(record, 0);
        UInt(record, 4, (uint)data.Length); UInt(record, 12, id); data.CopyTo(record, 24); return record;
    }
    private static byte[] SourceRecordData(byte[] plugin, uint id)
    {
        var offset = Header("Links.esm").Length + 24;
        Require(BinaryPrimitives.ReadUInt32LittleEndian(plugin.AsSpan(offset + 12)) == id, "Unexpected source fixture identity.");
        return plugin.AsSpan(offset + 24, checked((int)BinaryPrimitives.ReadUInt32LittleEndian(plugin.AsSpan(offset + 4)))).ToArray();
    }
    private static byte[] Field(string signature, byte[] data)
    {
        var field = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(signature).CopyTo(field, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(field.AsSpan(4), checked((ushort)data.Length)); data.CopyTo(field, 6); return field;
    }
    private static byte[] Text(string value) => Encoding.ASCII.GetBytes(value + '\0');
    private static byte[] Join(params byte[][] data) => data.SelectMany(value => value).ToArray();
    private static void UInt(byte[] data, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset), value);
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException or KeyNotFoundException) { return; }
        throw new InvalidOperationException("Malformed or unowned linked-door access was accepted.");
    }
}
