using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class FactionRelationContracts
{
    internal static void Run()
    {
        var directory = Directory.CreateTempSubdirectory("opennv-faction-relations-");
        try
        {
            File.WriteAllBytes(Path.Combine(directory.FullName, "Relations.esm"), Join(Header(),
                Faction(0x10, "FirstFaction", 1), Faction(0x11, "SecondFaction"),
                Actor(7, 0x10), Actor(8, 0x11), Record("ACTI", 9, Field("SCRI", U32(0x50))),
                Record("PACK", 0x40, Field("PTDT", Join(U32(0), U32(0x92), new byte[8]))),
                Record("SCPT", 0x50, Field("SCHR", new byte[20]), Field("SCRO", U32(0x10)), Field("SCRO", U32(0x11)),
                    Field("SCTX", Text("begin OnActivate\nSetAlly FirstFaction SecondFaction 1 0\nend"))),
                Record("QUST", 0x60, Field("DATA", [1, 0]), Field("SCRI", U32(0x51))),
                Record("SCPT", 0x51, Field("SCHR", QuestHeader()), Field("SCRO", U32(0x10)), Field("SCRO", U32(0x11)),
                    Field("SCTX", Text("begin GameMode\nSetEnemy FirstFaction SecondFaction 1 0\nend"))),
                Record("CELL", 0x80, Field("DATA", [1])), Group(0x80,
                    Reference("REFR", 0x90, 9), Reference("ACHR", 0x91, 7), Reference("ACHR", 0x92, 8))));
            File.WriteAllBytes(Path.Combine(directory.FullName, "Patch.esp"), Join(Header("Relations.esm"), Faction(0x10, "FirstFaction", 3)));
            using var records = FalloutPluginStack.Load(directory.FullName, ["Relations.esm", "Patch.esp"]);
            using var world = new FalloutReferenceWorld(records);
            world.LoadCell(FalloutCellSceneReader.Read(records, Key(0x80)));
            Require(world.FactionCombatReaction(Key(0x10), Key(0x11)) == 3 && world.ActorRelation(Key(0x91), Key(0x92)) == 3,
                "Faction reaction ignored the winning override or declaring master.");
            var scripts = new FalloutReferenceScripts(records, world, new(records), new((_, _) => false,
                _ => throw new InvalidOperationException("Faction command escaped shared state.")));
            Require(scripts.Activate(Key(0x90), records.RuntimeFormKey(0x14)).Error is null &&
                world.FactionCombatReaction(Key(0x10), Key(0x11)) == 3 && world.FactionCombatReaction(Key(0x11), Key(0x10)) == 2,
                "Object SetAlly lost independent directional flags.");
            world.SetFactionRelationship(Key(0x10), Key(0x11), true);
            Require(world.ActorRelation(Key(0x91), Key(0x92)) == 2, "Default SetAlly did not update combat's live relation.");
            var before = JsonSerializer.Serialize(world.CaptureFactionRelations());
            Reject(() => world.SetFactionRelationship(Key(0x10), Key(0x11), true, 0, 2));
            Reject(() => world.SetFactionRelationship(Key(0x10), Key(7), false));
            Require(JsonSerializer.Serialize(world.CaptureFactionRelations()) == before, "Rejected relation partly changed shared state.");
            var quests = new FalloutQuestState(records);
            var questScripts = new FalloutQuestScripts(records, quests, new HashSet<FalloutFormKey>(), new(),
                defaultProcessingDelay: 0, references: world);
            questScripts.Advance(0.1);
            Require(questScripts.Capture().Instances.Single().Error is null &&
                world.ActorRelation(Key(0x91), Key(0x92)) == 0 && world.ActorRelation(Key(0x92), Key(0x91)) == 1,
                "Quest SetEnemy lost directional neutral/enemy state.");
            var saved = JsonSerializer.Deserialize<FalloutFactionRelationSnapshot[]>(JsonSerializer.Serialize(world.CaptureFactionRelations()))!;
            using var cold = new FalloutReferenceWorld(records);
            cold.RestoreFactionRelations(saved);
            Require(cold.ActorRelation(Key(0x91), Key(0x92)) == 0 && cold.ActorRelation(Key(0x92), Key(0x91)) == 1,
                "Cold reaction overlay restored source defaults.");
            foreach (var invalid in new[] { saved.Append(saved[0]).ToArray(),
                saved.Select(value => value with { FromSha256 = new string('0', 64) }).ToArray(),
                saved.Select(value => value with { CombatReaction = 4 }).ToArray() })
            {
                Reject(() => cold.RestoreFactionRelations(invalid));
                Require(cold.ActorRelation(Key(0x92), Key(0x91)) == 1, "Rejected cold reaction partly mutated state.");
            }

            var owner = records.GetEffective(Key(0x40));
            var value = new FalloutCondition(owner, 0, 0, 14, 63, 0, 0, 0);
            Require(world.EvaluateActorReferenceCondition(Key(0x91), value) == 0, "Initial user actor value was not source zero.");
            world.ChangeActorValue(Key(0x91), "variable02", "setav", 5);
            world.ChangeActorValue(Key(0x92), "variable02", "setav", 9);
            Require(world.EvaluateActorReferenceCondition(Key(0x91), value) == 5 &&
                world.EvaluateActorReferenceCondition(Key(0x91), value with { RunOn = 1 }) == 9 &&
                world.EvaluateActorReferenceCondition(Key(0x91), value with { RunOn = 2, Reference = 0x92 }) == 9,
                "Actor-value scope did not resolve actual self, package target and explicit reference.");
            Reject(() => world.EvaluateActorReferenceCondition(Key(0x91), value with { Argument1 = 47 }));
            Reject(() => world.EvaluateActorReferenceCondition(Key(0x91), value with { RunOn = 3 }));
            var distance = value with { Function = 1, Argument1 = 0x14 };
            world.Get(Key(0x91)).QuerySpatialPlacement = () => new(Key(0x80), [3, 4, 12], [0, 0, 0]);
            Require(world.EvaluateActorReferenceCondition(Key(0x91), distance, new(Key(0x80), [0, 0, 0], [0, 0, 0])) == 13,
                "Distance used stale editor placement instead of current native actor and player poses.");
            Reject(() => world.EvaluateActorReferenceCondition(Key(0x91), distance));
            Console.WriteLine("OPENNV_FACTION_AI_REFERENCE_CONTRACT_PASS winningSource=true directional=true objectAndQuest=true liveCombat=true cold=true atomicReject=true actualActorValues=true scopedQueries=true nativePoseDistance=true unsupportedRefused=true");
        }
        finally { directory.Delete(true); }
    }

    private static FalloutFormKey Key(uint id) => new("Relations.esm", id);
    private static byte[] Faction(uint id, string name, uint? reaction = null) => Record("FACT", id, Field("EDID", Text(name)),
        reaction is { } value ? Field("XNAM", Join(U32(0x11), U32(0), U32(value))) : []);
    private static byte[] Actor(uint id, uint faction) => Record("NPC_", id, Field("ACBS", new byte[24]),
        Field("AIDT", new byte[20]), Field("SNAM", Join(U32(faction), new byte[4])));
    private static byte[] Reference(string signature, uint id, uint form) => Record(signature, id, Field("NAME", U32(form)), Field("DATA", new byte[24]));
    private static byte[] Header(string? master = null) => Record("TES4", 0, Field("HEDR", new byte[12]),
        master is null ? [] : Join(Field("MAST", Text(master)), Field("DATA", new byte[8])));
    private static byte[] QuestHeader() { var bytes = new byte[20]; bytes[16] = 1; return bytes; }
    private static byte[] Group(uint cell, params byte[][] records)
    {
        var data = Join(records); var bytes = new byte[24 + data.Length]; Encoding.ASCII.GetBytes("GRUP").CopyTo(bytes, 0);
        U32((uint)bytes.Length).CopyTo(bytes, 4); U32(cell).CopyTo(bytes, 8); U32(6).CopyTo(bytes, 12); data.CopyTo(bytes, 24); return bytes;
    }
    private static byte[] Record(string signature, uint id, params byte[][] fields)
    {
        var data = Join(fields); var bytes = new byte[24 + data.Length]; Encoding.ASCII.GetBytes(signature).CopyTo(bytes, 0);
        U32((uint)data.Length).CopyTo(bytes, 4); U32(id).CopyTo(bytes, 12); data.CopyTo(bytes, 24); return bytes;
    }
    private static byte[] Field(string signature, byte[] data)
    {
        var bytes = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(signature).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), checked((ushort)data.Length)); data.CopyTo(bytes, 6); return bytes;
    }
    private static byte[] Text(string value) => Encoding.ASCII.GetBytes(value + '\0');
    private static byte[] U32(uint value) => BitConverter.GetBytes(value);
    private static byte[] Join(params byte[][] values) => values.SelectMany(value => value).ToArray();
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidOperationException("Unsupported faction or actor query was accepted.");
    }
}
