using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static class ScriptDeathContracts
{
    internal static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-script-death-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var header = new byte[12]; BinaryPrimitives.WriteSingleLittleEndian(header, 1.34f);
            var references = Join(Reference(0x90, 1), Reference(0x91, 2), Reference(0x92, 3),
                Reference(0x93, 4), Reference(0x94, 5), Reference(0x95, 6), Reference(0x96, 7), Reference(0x97, 8));
            var group = new byte[24 + references.Length]; Encoding.ASCII.GetBytes("GRUP").CopyTo(group, 0);
            UInt(group, 4, (uint)group.Length); UInt(group, 8, 0x80); UInt(group, 12, 6); references.CopyTo(group, 24);
            var entry = new byte[12]; entry[0] = 1; UInt(entry, 4, 0x30); entry[8] = 2;
            var part = new byte[84]; BinaryPrimitives.WriteSingleLittleEndian(part, 1); part[5] = 0; part[6] = 25;
            File.WriteAllBytes(Path.Combine(directory, "Death.esm"), Join(Record("TES4", 0, Field("HEDR", header)),
                Creature(1, 0x50), Creature(2, 0x51), Creature(3, 0x52), Creature(4, 0x53, flags: 2),
                Creature(5, 0x53, flags: 0x40000000), Creature(6, 0x53, deathItem: 0x20), Creature(7, 0x53, health: 0), Creature(8, 0, template: 1),
                Record("BPTD", 0x20, Field("BPNN", Text("Root")), Field("BPNT", Text("Root")), Field("BPND", part)),
                Record("MISC", 0x30, Field("EDID", Text("SourceDeathLoot")), Field("DATA", new byte[8])),
                Record("LVLI", 0x31, Field("LVLD", [0]), Field("LVLF", [0]), Field("LVLO", entry)),
                Script(0x50, "Kill"), Script(0x51, "KillActor player"), Script(0x52, "set loaded to loaded + 1\nKill"),
                Script(0x53, "Kill"), Record("CELL", 0x80, Field("DATA", [1])), group));
            using var records = FalloutPluginStack.Load(directory, ["Death.esm"]);
            var cell = FalloutCellSceneReader.Read(records, Key(0x80));
            using var world = new FalloutReferenceWorld(records); world.LoadCell(cell);
            world.InitializeActorTemplates(Key(0x97), 1);
            _ = world.DamageActor(Key(0x90), Key(0x91), 0, 5, 2, 1);
            world.Get(Key(0x90)).ActorValues["health"] = world.Health(Key(0x90)) with { Permanent = 10, Temporary = 5 };
            const string oldFault = "OnLoad: Reached native script command Kill (0 arguments) has no owner.";
            world.Get(Key(0x90)).ScriptError = oldFault;
            world.Get(Key(0x91)).ScriptError = "OnLoad: Reached object-script command KillActor (1 arguments) has no owner.";
            world.Get(Key(0x92)).ScriptError = oldFault; world.Get(Key(0x92)).Write(1, 41);
            using var restored = new FalloutReferenceWorld(records); restored.Restore(Snapshot(world)); restored.LoadCell(cell);
            var quests = new FalloutQuestState(records);
            var scripts = Scripts(records, restored, quests);
            Require(scripts.Dispatch(Key(0x97), "OnLoad") is { Error: null, Blocks: 1 } && restored.Get(Key(0x97)).Read(1) == 1,
                "Inherited actor script could not resolve its own authoritative local.");
            scripts.ExecuteProgram(records.GetEffective(Key(0x92)), records.GetEffective(Key(0x52)),
                FalloutGameModeProgram.Read("begin GameMode\nset loaded to CreatureRef97.loaded\nend"), 0);
            Require(restored.Get(Key(0x92)).Read(1) == 1, "Qualified script local ignored retained template ownership.");
            restored.Get(Key(0x92)).Write(1, 41);
            Require(scripts.Dispatch(Key(0x90), "GameMode").Error == oldFault, "Unrelated event cleared script death fault.");
            var result = scripts.Dispatch(Key(0x90), "OnLoad");
            var state = restored.Get(Key(0x90));
            Require(result is { Error: null, Blocks: 1, RecoveredError: oldFault } && state.Read(1) == 1 &&
                restored.Health(Key(0x90)) is { Current: 0, Base: 50, Permanent: 10, Temporary: 5 } &&
                state.Injury is { Dead: true, Killer: null, DeathEventPending: true } && state.Injury.LimbDamage[0] == 10 &&
                restored.Inventory(Key(0x90), 1).Contents.Item(Key(0x30))?.Count == 2,
                "Recovered Kill lost modifier pools, limbs, unknown killer, source loot or following statement: " + result.Error);
            var knownResult = scripts.Dispatch(Key(0x91), "OnLoad");
            Require(knownResult is { Error: null, RecoveredError: not null } &&
                restored.Get(Key(0x91)).Injury?.Killer == records.RuntimeFormKey(0x14), "KillActor did not bind explicit killer: " + knownResult.Error);
            Require(scripts.Dispatch(Key(0x92), "OnLoad").Error == oldFault && restored.Get(Key(0x92)).Read(1) == 41 &&
                !restored.IsDead(Key(0x92)), "Command recovery repeated an earlier mutation.");
            restored.Inventory(Key(0x90), 1).Contents.TransferTo(new(), Key(0x30), 1);
            Require(!restored.KillActor(Key(0x90), records.RuntimeFormKey(0x14), 1) &&
                restored.Inventory(Key(0x90), 1).Contents.Item(Key(0x30))?.Count == 1 && state.Injury!.Killer is null,
                "Repeated death changed killer or granted loot again.");
            Require(!restored.AdvanceDeathEvent(Key(0x90), .75, 2, false), "Unknown-killer death ignored delay.");
            using var cold = new FalloutReferenceWorld(records); cold.Restore(Snapshot(restored)); cold.LoadCell(cell);
            var coldScripts = Scripts(records, cold, quests);
            Require(cold.Get(Key(0x90)).Injury is { DeathEventPending: true, DeathEventElapsed: .75, Killer: null } &&
                !cold.AdvanceDeathEvent(Key(0x90), 1.25, 2, true) && cold.AdvanceDeathEvent(Key(0x90), 0, 2, false),
                "Cold death lost unknown killer, delay or speech guard.");
            Require(coldScripts.Dispatch(Key(0x90), "OnDeath") is { Error: null, Blocks: 1 } &&
                cold.Get(Key(0x90)).Read(2) == 1 && cold.Get(Key(0x90)).Read(3) == 0 && cold.Get(Key(0x90)).Read(4) == 0,
                "Unknown killer ran a player-filtered death block or invented a reference.");
            Require(cold.AdvanceDeathEvent(Key(0x91), 2, 2, false) &&
                coldScripts.Dispatch(Key(0x91), "OnDeath", records.RuntimeFormKey(0x14)) is { Error: null, Blocks: 2 } &&
                cold.Get(Key(0x91)).Read(4) == 1 && cold.Get(Key(0x91)).Read(3) == 0x14, "Known killer lost filtered death block.");
            using var consumed = new FalloutReferenceWorld(records); consumed.Restore(Snapshot(cold)); consumed.LoadCell(cell);
            Require(!consumed.AdvanceDeathEvent(Key(0x90), 10, 2, false) &&
                consumed.Inventory(Key(0x90), 1).Contents.Item(Key(0x30))?.Count == 1, "Cold corpse repeated event or loot.");
            Reject(() => cold.KillActor(Key(0x93), null, 1));
            Require(!cold.IsDead(Key(0x93)) && cold.Get(Key(0x93)).Inventory is null, "Essential boundary mutated health/inventory.");
            Require(cold.KillActor(Key(0x94), null, 1), "Direct script death was treated as an invulnerable weapon hit.");
            Reject(() => cold.KillActor(Key(0x95), null, 1));
            Require(!cold.IsDead(Key(0x95)) && cold.Health(Key(0x95)).Current == 50, "Failed loot published partial death.");
            cold.InitializeSourceCorpse(Key(0x96));
            Require(!cold.KillActor(Key(0x96), null, 1) && cold.Get(Key(0x96)).Injury is
            { Dead: true, DeathEventPending: false, DeathInventoryGranted: false }, "Kill invented a new death for an authored corpse.");
            Reject(() => cold.KillActor(Key(0x92), null, 0));
            Reject(() => cold.KillActor(Key(0x92), Key(0x30), 1));
            foreach (var command in new[] { "KillActor player 1", "KillActor player 1 2", "player.Kill", "KillActor player 1 2 3" })
            {
                Reject(() => coldScripts.ExecuteProgram(records.GetEffective(Key(0x92)), records.GetEffective(Key(0x52)),
                    FalloutGameModeProgram.Read("begin GameMode\n" + command + "\nend"), 0));
                Require(!cold.IsDead(Key(0x92)), "Unsupported death parameters mutated the target.");
            }
            Console.WriteLine("OPENNV_SCRIPT_DEATH_CONTRACT_PASS aliases=true inheritedAndQualifiedLocals=true unknownKiller=true filteredEvents=true lootOnce=true coldState=true recoveryBeforeMutation=true unsupportedBeforeMutation=true");
        }
        finally { Directory.Delete(directory, true); }
    }

    private static FalloutReferenceScripts Scripts(FalloutPluginStack records, FalloutReferenceWorld world, FalloutQuestState quests) =>
        new(records, world, quests, new((_, _) => false, _ => throw new InvalidDataException("Script death invented a host effect."), PlayerLevel: () => 1));
    private static FalloutReferenceSnapshot[] Snapshot(FalloutReferenceWorld world) =>
        JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!;
    private static FalloutFormKey Key(uint id) => new("Death.esm", id);
    private static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException or KeyNotFoundException or ArgumentOutOfRangeException) { return; }
        throw new InvalidDataException("Invalid script death state was admitted.");
    }
    private static byte[] Creature(uint id, uint script, uint flags = 0, uint deathItem = 0x31, short health = 50, uint? template = null)
    {
        var stats = new byte[17]; BinaryPrimitives.WriteInt16LittleEndian(stats.AsSpan(4), health);
        var acbs = new byte[24]; UInt(acbs, 0, flags); acbs[8] = 1;
        if (template is not null) BinaryPrimitives.WriteUInt16LittleEndian(acbs.AsSpan(22), 512);
        return Record("CREA", id, Field("ACBS", acbs), Field("DATA", stats), Field("SCRI", BitConverter.GetBytes(script)),
            template is { } baseId ? Field("TPLT", BitConverter.GetBytes(baseId)) : [],
            Field("PNAM", BitConverter.GetBytes(0x20u)), Field("INAM", BitConverter.GetBytes(deathItem)), Field("NAM4", BitConverter.GetBytes(6u)));
    }
    private static byte[] Script(uint id, string command) => Record("SCPT", id, Local(1, "loaded"), Local(2, "deaths"),
        Local(3, "killer", 1), Local(4, "playerDeaths"), Field("SCRO", BitConverter.GetBytes(0x14u)), Field("SCRO", BitConverter.GetBytes(0x97u)), Field("SCTX", Text("begin OnLoad\n" + command +
            "\nset loaded to loaded + 1\nend\nbegin OnDeath\nset deaths to deaths + 1\nset killer to GetKiller\nend\n" +
            "begin OnDeath player\nset playerDeaths to playerDeaths + 1\nend")));
    private static byte[] Local(uint index, string name, byte kind = 0)
    {
        var data = new byte[24]; UInt(data, 0, index); data[16] = kind;
        return Join(Field("SLSD", data), Field("SCVR", Text(name)));
    }
    private static byte[] Reference(uint id, uint baseId) => Record("ACRE", id, Field("EDID", Text($"CreatureRef{id:x}")), Field("NAME", BitConverter.GetBytes(baseId)), Field("DATA", new byte[24]));
    private static byte[] Text(string value) => Encoding.ASCII.GetBytes(value + '\0');
    private static byte[] Join(params byte[][] fields) => fields.SelectMany(value => value).ToArray();
    private static void UInt(byte[] bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
    private static byte[] Field(string signature, byte[] data)
    {
        var bytes = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(signature).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), checked((ushort)data.Length)); data.CopyTo(bytes, 6); return bytes;
    }
    private static byte[] Record(string signature, uint id, params byte[][] fields)
    {
        var payload = Join(fields); var bytes = new byte[24 + payload.Length]; Encoding.ASCII.GetBytes(signature).CopyTo(bytes, 0);
        UInt(bytes, 4, (uint)payload.Length); UInt(bytes, 12, id); payload.CopyTo(bytes, 24); return bytes;
    }
}
