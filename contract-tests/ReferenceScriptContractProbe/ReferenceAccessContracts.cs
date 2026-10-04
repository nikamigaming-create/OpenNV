using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class ReferenceAccessContracts
{
    internal static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-reference-access-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var plugin = Fixture();
            File.WriteAllBytes(Path.Combine(directory, "Access.esm"), plugin);
            var patch = Join(Header("Access.esm"), Group(0x80, Reference("REFR", 0x90, 1, "DoorA",
                Field("XLOC", Lock(255, 0x01000120, 0, 12)))), Item("KEYM", 0x01000120, "PatchKey"),
                Record("FACT", 0x01000121, Field("EDID", Text("PatchFaction"))),
                Record("TERM", 6, Field("DNAM", [5, 0, 0, 0]), Field("PNAM", BitConverter.GetBytes(0x01000122u))),
                Item("NOTE", 0x01000122, "TerminalPassword"), Script(true));
            File.WriteAllBytes(Path.Combine(directory, "AccessPatch.esp"), patch);
            using var records = FalloutPluginStack.Load(directory, ["Access.esm", "AccessPatch.esp"]);
            var cell = FalloutCellSceneReader.Read(records, Key(0x80));
            using var world = new FalloutReferenceWorld(records); world.LoadCell(cell);
            var quests = new FalloutQuestState(records);
            var effects = 0;
            FalloutReferenceScripts Scripts(FalloutReferenceWorld owner) => new(records, owner, quests,
                new((_, _) => false, _ => ++effects));
            var scripts = Scripts(world);
            void Run(string source, uint caller = 0x91) => scripts.ExecuteProgram(records.GetEffective(Key(caller)),
                records.GetEffective(Key(0x50)), FalloutGameModeProgram.Read("begin GameMode\n" + source + "\nend"), 0);

            Require(world.GetLocked(Key(0x90)) == 1 && world.GetLockLevel(Key(0x90)) == 255 &&
                world.Lock(Key(0x90))!.Key == PatchKey(0x120), "Winning FF level/key or master adjustment was lost.");
            Run("DoorA.Lock 100");
            Require(world.GetLockLevel(Key(0x90)) == 100 && world.GetLocked(Key(0x90)) == 1 &&
                world.GetLockLevel(Key(0x91)) == 25 && !world.Get(Key(0x90)).DoorOpen && effects == 0,
                "Lock changed another placed instance or opened/activated the source door.");
            Run("DoorA.Unlock\nset sample to DoorA.GetLockLevel");
            Require(world.GetLocked(Key(0x90)) == 0 && world.Lock(Key(0x90)) is null &&
                world.Get(Key(0x91)).Read(1) == 100, "Unlock erased difficulty or did not change shared access.");
            Run("DoorA.Lock\nDoorA.Lock 0\nset receiver to DoorA\nset sample to (receiver).GetLocked");
            Require(world.Get(Key(0x91)).Read(1) == 1 && world.GetLockLevel(Key(0x90)) == 100,
                "Default Lock or typed postfix query changed retained difficulty.");
            Run("DoorA.Lock 256"); Require(world.GetLockLevel(Key(0x90)) == 0, "Nonzero Lock did not retain byte conversion.");
            Run("DoorA.Lock -1"); Require(world.GetLockLevel(Key(0x90)) == 255, "Signed Lock conversion discarded FF.");
            Run("DoorA.Lock 100");
            Run("EmptyDoor.Unlock\nset sample to EmptyDoor.GetLockLevel");
            Require(world.Get(Key(0x92)).LockState is null && world.Get(Key(0x91)).Read(1) == 0,
                "Unlock created data for an absent source lock.");
            Run("EmptyDoor.Lock"); Require(world.GetLocked(Key(0x92)) == 1 && world.GetLockLevel(Key(0x92)) == 0,
                "Default Lock could not create a general per-reference lock.");
            Require(world.GetLocked(Key(0xa0)) == 0 && world.GetLockLevel(Key(0xa0)) == 0 &&
                world.GetLocked(Key(0xa1)) == 1 && world.GetLockLevel(Key(0xa1)) == 255,
                "Source terminal unlocked/required-key state did not join placed lock ownership.");
            world.LockReference(Key(0xa0), 255);
            Require(world.GetLockLevel(Key(0xa0)) == 255 && world.GetLocked(Key(0xa0)) == 1 &&
                world.GetLocked(Key(0xa2)) == 0, "Terminal locking changed another instance of the same base.");
            world.UnlockReference(Key(0xa0)); world.LockReference(Key(0xa0));
            Require(world.GetLockLevel(Key(0xa0)) == 255, "Terminal default relock lost its reached difficulty.");
            var passwordInventory = new FalloutPlayerInventory();
            Require(!world.UnlockWithKey(Key(0xa1), passwordInventory), "Terminal admitted an absent password.");
            passwordInventory.Add(records, PatchKey(0x122), 1, 1, true);
            Require(world.UnlockWithKey(Key(0xa1), passwordInventory) && world.GetLocked(Key(0xa1)) == 0 &&
                passwordInventory.Item(PatchKey(0x122))!.Count == 1, "Terminal lost its adjusted NOTE password or consumed it.");
            foreach (var reference in new uint[] { 0xa3, 0xa4, 0xa5, 0xa6 })
                Reject(() => world.LockReference(Key(reference), 255));
            Run("Lock 75\nset sample to GetLockLevel", 0x9a);
            Require(world.Get(Key(0x9a)).Read(1) == 75 && world.GetLocked(Key(0x9a)) == 1,
                "Calling ACHR lost its actual per-reference lock owner.");
            Run("if 0\nset sample to TerminalRef.GetLocked\nendif");
            foreach (var invalid in new[] { "DoorA.Lock 0.5", "DoorA.Lock 100 1", "DoorA.Unlock 1",
                "DoorA.Lock 1 0 0", "DoorA.Unlock 0 0", "TerminalRef.Lock 100", "LinkedDoor.Lock 100",
                "set sample to LeveledDoor.GetLockLevel", "BadExtent.Lock 100", "BadKey.Lock 100", "AccessQuest.Lock 100" })
            {
                var before = world.Get(Key(0x90)).LockState;
                Reject(() => Run(invalid));
                Require(world.Get(Key(0x90)).LockState == before, "Rejected command changed the live lock.");
            }
            Reject(() => Run("set sample to DoorBase.GetLocked"));
            var keyring = new FalloutPlayerInventory();
            Require(!world.UnlockWithKey(Key(0x90), keyring), "Locked reference admitted a missing key.");
            keyring.Add(records, PatchKey(0x120), 1, 1, true);
            Require(world.UnlockWithKey(Key(0x90), keyring) && world.GetLocked(Key(0x90)) == 0 &&
                world.GetLockLevel(Key(0x90)) == 100 && keyring.Item(PatchKey(0x120))!.Count == 1,
                "Key access did not join Unlock or consumed the key.");
            Run("DoorA.Lock 0");

            Run("ItemA.SetOwnership"); Require(world.Ownership(Key(0x93)).Owner == Key(7), "Default owner was not PlayerBASE.");
            Run("ItemA.SetOwnership 0"); Require(world.Ownership(Key(0x93)).Owner == Key(7), "Zero owner cleared ownership.");
            Run("ItemA.SetOwnership OtherNpc"); Require(world.Ownership(Key(0x93)).Owner == Key(8), "NPC_ ownership was lost.");
            Run("ItemB.SetOwnership PatchFaction"); Require(world.Ownership(Key(0x94)).Owner == PatchKey(0x121),
                "Typed ESP-local FACT owner did not resolve through compiled slots.");
            foreach (var invalid in new[] { "ItemA.SetOwnership ActualActor", "ItemA.SetOwnership CreatureBase",
                "ItemA.SetOwnership DoorBase", "ItemA.SetOwnership player", "ItemA.SetOwnership 0.5",
                "ItemA.SetOwnership OtherNpc PatchFaction" })
            {
                var before = world.Get(Key(0x93)).OwnershipOverride;
                Reject(() => Run(invalid));
                Require(world.Get(Key(0x93)).OwnershipOverride == before, "Rejected owner changed the live reference.");
            }
            var player = new FalloutPlayerInventory(); world.Take(Key(0x93), player, 1, null);
            var acquired = player.Item(Key(3))!.Variants!.Single();
            Require(acquired.Owner == Key(8) && acquired.FactionRank == 3 && acquired.Global == Key(0x40) &&
                acquired.Count == 2 && acquired.Condition == 1 && world.Get(Key(0x93)).Taken,
                "Pickup used static XOWN or discarded retained ownership/item extras.");
            var fault = scripts.Dispatch(Key(0x91), "GameMode");
            var prefix = world.Get(Key(0x91)).Read(1);
            Require(fault.Error is not null && prefix != 99 && world.GetLockLevel(Key(0x91)) == 100,
                "A failed source access program discarded its prefix or ran its suffix.");
            _ = scripts.Dispatch(Key(0x91), "GameMode");
            Require(world.Get(Key(0x91)).Read(1) == prefix, "Failed access prefix replayed on GameMode.");

            var snapshots = JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!;
            using var cold = new FalloutReferenceWorld(records); cold.Restore(snapshots); cold.LoadCell(cell);
            Require(cold.GetLocked(Key(0x90)) == 1 && cold.GetLockLevel(Key(0x90)) == 100 &&
                cold.Ownership(Key(0x94)).Owner == PatchKey(0x121) && cold.Get(Key(0x93)).Taken &&
                cold.Get(Key(0x91)).ScriptError == fault.Error, "Cold access, ownership, pickup or source fault changed.");
            Require(cold.GetLocked(Key(0xa0)) == 1 && cold.GetLockLevel(Key(0xa0)) == 255 &&
                cold.GetLocked(Key(0xa1)) == 0 && cold.GetLockLevel(Key(0xa1)) == 255 &&
                cold.GetLocked(Key(0xa2)) == 0, "Cold terminal locks lost independent difficulty/password access.");
            _ = Scripts(cold).Dispatch(Key(0x91), "GameMode");
            Require(cold.Get(Key(0x91)).Read(1) == prefix, "Cold restoration replayed a failed source prefix.");
            var original = snapshots.Single(snapshot => snapshot.Reference == Key(0x90));
            foreach (var invalid in new[]
            {
                original with { LockState = original.LockState! with { Level = -1 } },
                original with { LockState = original.LockState! with { ReferenceSha256 = new string('0', 64) } },
                original with { Unlocked = true },
                original with { OwnershipOverride = new(original.LockState!.ReferenceSha256, Key(3)) },
            })
            {
                using var rejected = new FalloutReferenceWorld(records);
                Reject(() => rejected.Restore(snapshots.Where(snapshot => snapshot.Reference != original.Reference).Append(invalid).ToArray()));
                Require(rejected.InstanceCount == 0, "Rejected late reference restore partially committed.");
            }
            using (var legacy = new FalloutReferenceWorld(records))
            {
                legacy.Restore([original with { LockState = null, Unlocked = true }]);
                Require(legacy.GetLocked(Key(0x90)) == 0 && legacy.GetLockLevel(Key(0x90)) == 255,
                    "Legacy Unlocked migration invented difficulty or lost source FF.");
                legacy.LockReference(Key(0x90)); Require(legacy.GetLockLevel(Key(0x90)) == 255,
                    "Relocking a legacy unlock lost source difficulty.");
            }
            VerifySourceDrift(directory, plugin, patch, snapshots);
            Require(effects == 0, "Reference access emitted a native presentation effect.");
            Console.WriteLine("OPENNV_REFERENCE_ACCESS_PASS ff=true defaults=true byteLevel=true absent=true actualCaller=true " +
                "typedQueries=true lazy=true perInstance=true typedOwners=true playerBaseDefault=true effectivePickup=true " +
                "keyUnlock=true cold=true legacyUnlock=true sourceDrift=true invalidAtomic=true retainedFault=true " +
                "terminal=true terminalPassword=true terminalPerInstance=true linkedAndLeveledDifficultyAndCellAccess=unbound lockpickAndInheritedCrime=unverified");
        }
        finally
        {
            foreach (var child in Directory.EnumerateDirectories(directory))
            { foreach (var file in Directory.EnumerateFiles(child)) File.Delete(file); Directory.Delete(child); }
            foreach (var file in Directory.EnumerateFiles(directory)) File.Delete(file);
            Directory.Delete(directory);
        }
        LinkedDoorAccessContracts.Run();
    }

    private static void VerifySourceDrift(string directory, byte[] plugin, byte[] patch, FalloutReferenceSnapshot[] snapshots)
    {
        var drift = Path.Combine(directory, "drift"); Directory.CreateDirectory(drift);
        File.WriteAllBytes(Path.Combine(drift, "Access.esm"), plugin);
        var changed = (byte[])patch.Clone();
        var marker = Encoding.ASCII.GetBytes("XLOC");
        var offset = Enumerable.Range(0, changed.Length - marker.Length)
            .Single(index => changed.AsSpan(index, marker.Length).SequenceEqual(marker));
        changed[offset + 6] = 75;
        File.WriteAllBytes(Path.Combine(drift, "AccessPatch.esp"), changed);
        using (var records = FalloutPluginStack.Load(drift, ["Access.esm", "AccessPatch.esp"]))
        {
            using var rejected = new FalloutReferenceWorld(records);
            Reject(() => rejected.Restore(snapshots)); Require(rejected.InstanceCount == 0, "Source drift partially committed reference state.");
        }
        // A terminal's difficulty/password belongs to its winning base. The
        // placed reference can be byte-identical while that declaration drifts.
        var terminalChanged = (byte[])patch.Clone();
        var terminalMarker = Encoding.ASCII.GetBytes("DNAM");
        var terminalOffset = Enumerable.Range(0, terminalChanged.Length - terminalMarker.Length)
            .Single(index => terminalChanged.AsSpan(index, terminalMarker.Length).SequenceEqual(terminalMarker));
        terminalChanged[terminalOffset + 6] = 4;
        File.WriteAllBytes(Path.Combine(drift, "AccessPatch.esp"), terminalChanged);
        using (var terminalRecords = FalloutPluginStack.Load(drift, ["Access.esm", "AccessPatch.esp"]))
        {
            using var rejected = new FalloutReferenceWorld(terminalRecords);
            Reject(() => rejected.Restore(snapshots));
            Require(rejected.InstanceCount == 0, "Terminal base drift partially committed saved references.");
        }
        // Keep the complete winning record bytes, but change what their raw
        // master indices mean. The source context must reject this too.
        File.WriteAllBytes(Path.Combine(drift, "Otherx.esm"), Join(Header("Access.esm"),
            Item("KEYM", 0x01000120, "OtherKey"), Record("FACT", 0x01000121)));
        var newHeader = Record("TES4", 0, Field("HEDR", new byte[12]), Field("MAST", Text("Access.esm")),
            Field("DATA", new byte[8]), Field("MAST", Text("Otherx.esm")), Field("DATA", new byte[8]));
        File.WriteAllBytes(Path.Combine(drift, "AccessPatch.esp"), Join(newHeader, patch[Header("Access.esm").Length..]));
        using var context = FalloutPluginStack.Load(drift, ["Access.esm", "Otherx.esm", "AccessPatch.esp"]);
        Require(context.GetEffective(Key(0x90)).ReadData().SequenceEqual(RecordData(patch, "REFR")),
            "Master-context probe changed the winning reference bytes.");
        using var rejectedContext = new FalloutReferenceWorld(context);
        Reject(() => rejectedContext.Restore(snapshots));
        Require(rejectedContext.InstanceCount == 0, "Master adjustment drift partially committed reference state.");
    }

    private static byte[] Fixture() => Join(Header(),
        Record("DOOR", 1, Field("EDID", Text("DoorBase")), Field("SCRI", BitConverter.GetBytes(0x50u))),
        Item("MISC", 3, "AccessItem"), Record("TERM", 4),
        Record("TERM", 5, Field("DNAM", [0, 2, 5, 0])),
        Record("TERM", 6, Field("DNAM", [5, 0, 0, 0])), Record("NPC_", 7),
        Record("TERM", 0x30, Field("DNAM", [6, 0, 0, 0])),
        Record("TERM", 0x31, Field("DNAM", [0, 16, 0, 0])),
        Record("TERM", 0x32, Field("DNAM", [0, 0, 0])),
        Record("TERM", 0x33, Field("DNAM", [0, 0, 0, 0]), Field("PNAM", BitConverter.GetBytes(8u))),
        Record("NPC_", 8, Field("EDID", Text("OtherNpc")), Field("SCRI", BitConverter.GetBytes(0x50u))),
        Record("CREA", 9, Field("EDID", Text("CreatureBase"))), Record("FACT", 11), Item("KEYM", 20, "SourceKey"),
        Record("GLOB", 0x40, Field("FLTV", BitConverter.GetBytes(.5f))),
        Script(false),
        Record("QUST", 0x60, Field("EDID", Text("AccessQuest"))), Record("CELL", 0x80, Field("DATA", [1])),
        Group(0x80, Reference("ACHR", 14, 7, "PlayerRef"),
            Reference("REFR", 0x90, 1, "DoorA", Field("XLOC", Lock(255, 20, 0, 12))),
            Reference("REFR", 0x91, 1, "DoorB", Field("XLOC", Lock(25, 20, 0, 20))),
            Reference("REFR", 0x92, 1, "EmptyDoor"),
            Reference("REFR", 0x93, 3, "ItemA", Field("XOWN", BitConverter.GetBytes(11u)),
                Field("XRNK", BitConverter.GetBytes(3)), Field("XGLB", BitConverter.GetBytes(0x40u)),
                Field("XCNT", BitConverter.GetBytes(2)), Field("XHLP", BitConverter.GetBytes(100f))),
            Reference("REFR", 0x94, 3, "ItemB"), Reference("REFR", 0x95, 4, "TerminalRef"),
            Reference("REFR", 0x96, 1, "LinkedDoor", Field("XTEL", Teleport())),
            Reference("REFR", 0x97, 1, "LeveledDoor", Field("XLOC", Lock(15, 20, 4, 20))),
            Reference("REFR", 0x98, 1, "BadExtent", Field("XLOC", new byte[11])),
            Reference("REFR", 0x99, 1, "BadKey", Field("XLOC", Lock(100, 8, 0, 12))),
            Reference("ACHR", 0x9a, 8, "ActualActor"),
            Reference("REFR", 0xa0, 5, "SourceTerminal"), Reference("REFR", 0xa1, 6, "PasswordTerminal"),
            Reference("REFR", 0xa2, 5, "OtherTerminal"),
            Reference("REFR", 0xa3, 0x30, "BadTerminalDifficulty"), Reference("REFR", 0xa4, 0x31, "BadTerminalFlags"),
            Reference("REFR", 0xa5, 0x32, "BadTerminalExtent"), Reference("REFR", 0xa6, 0x33, "BadTerminalPassword")));

    private static FalloutFormKey Key(uint id) => new("Access.esm", id);
    private static FalloutFormKey PatchKey(uint id) => new("AccessPatch.esp", id);
    private static byte[] Script(bool patch) => Record("SCPT", 0x50,
        [.. Local(1, "sample", 1), .. Local(2, "receiver", 0),
        Field("SCTX", Text("short sample\nref receiver\nbegin GameMode\nset sample to sample + 1\nLock 100\nUnsupportedAccessOwner\nset sample to 99\nend")),
        .. new uint[] { 1, 8, 9, 14, 0x60, 0x90, 0x91, 0x92, 0x93, 0x94, 0x95, 0x96, 0x97, 0x98, 0x99, 0x9a }
            .Concat(patch ? [0x01000121u] : Array.Empty<uint>()).Select(id => Field("SCRO", BitConverter.GetBytes(id)))]);
    private static byte[] Item(string type, uint id, string name) => Record(type, id, Field("EDID", Text(name)),
        Field("FULL", Text(name)), Field("DATA", new byte[8]));
    private static byte[] Lock(byte level, uint key, byte flags, int extent)
    { var data = new byte[extent]; data[0] = level; UInt(data, 4, key); data[8] = flags; return data; }
    private static byte[] Teleport() { var data = new byte[32]; UInt(data, 0, 0x90); return data; }
    private static byte[][] Local(uint index, string name, byte flags)
    { var data = new byte[24]; UInt(data, 0, index); data[16] = flags; return [Field("SLSD", data), Field("SCVR", Text(name))]; }
    private static byte[] Reference(string type, uint id, uint basis, string name, params byte[][] fields) =>
        Record(type, id, [Field("EDID", Text(name)), Field("NAME", BitConverter.GetBytes(basis)), Field("DATA", new byte[24]), .. fields]);
    private static byte[] Header(string? master = null) => Record("TES4", 0, master is null
        ? [Field("HEDR", new byte[12])] : [Field("HEDR", new byte[12]), Field("MAST", Text(master)), Field("DATA", new byte[8])]);
    private static byte[] Group(uint cell, params byte[][] records)
    {
        var data = Join(records); var group = new byte[24 + data.Length]; Encoding.ASCII.GetBytes("GRUP").CopyTo(group, 0);
        UInt(group, 4, (uint)group.Length); UInt(group, 8, cell); UInt(group, 12, 6); data.CopyTo(group, 24); return group;
    }
    private static byte[] Record(string name, uint id, params byte[][] fields)
    {
        var data = Join(fields); var bytes = new byte[24 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        UInt(bytes, 4, (uint)data.Length); UInt(bytes, 12, id); data.CopyTo(bytes, 24); return bytes;
    }
    private static byte[] Field(string name, byte[] data)
    {
        var bytes = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), checked((ushort)data.Length)); data.CopyTo(bytes, 6); return bytes;
    }
    private static byte[] Text(string value) => Encoding.ASCII.GetBytes(value + '\0');
    private static byte[] RecordData(byte[] plugin, string signature)
    {
        var name = Encoding.ASCII.GetBytes(signature);
        var offset = Enumerable.Range(0, plugin.Length - 24)
            .Single(index => plugin.AsSpan(index, 4).SequenceEqual(name));
        var extent = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(plugin.AsSpan(offset + 4)));
        return plugin.AsSpan(offset + 24, extent).ToArray();
    }
    private static byte[] Join(params byte[][] fields) => fields.SelectMany(value => value).ToArray();
    private static void UInt(byte[] data, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset), value);
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is InvalidDataException or NotSupportedException or InvalidOperationException or KeyNotFoundException) { return; }
        throw new InvalidOperationException("Invalid or unowned reference access was accepted.");
    }
}
