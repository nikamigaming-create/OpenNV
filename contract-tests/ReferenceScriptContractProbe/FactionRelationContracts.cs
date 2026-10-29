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
                ReactionFaction(0x12, "EnemyInput", 0x16, 1, int.MaxValue),
                ReactionFaction(0x13, "FriendInput", 0x16, 3, int.MinValue),
                ReactionFaction(0x14, "RemovedAllyInput", 0x16, 2, int.MaxValue),
                ReactionFaction(0x15, "NeutralInput", 0x16, 0, 100),
                ReactionFaction(0x17, "InvalidReaction", 0x16, 4, 0),
                Record("FACT", 0x18, Field("XNAM", Join(U32(0x16), U32(0)))),
                Faction(0x19, "EmptySharedFaction"),
                ReactionFaction(0x1a, "SelfAlly", 0x1a, 2, 0),
                ReactionFaction(0x1b, "SelfFriend", 0x1b, 3, 0),
                ReactionFaction(0x1c, "SelfEnemy", 0x1c, 1, 0),
                ReactionFaction(0x1d, "SelfNeutral", 0x1d, 0, 0),
                Record("FACT", 0x16, Field("EDID", Text("DirectedTarget")),
                    Field("XNAM", Join(U32(0x12), BitConverter.GetBytes(int.MinValue), U32(1))),
                    Field("XNAM", Join(U32(0x13), BitConverter.GetBytes(int.MaxValue), U32(0)))),
                FactionActor(0x70, (0x12, 0), (0x13, 127), (0x14, -1), (0x15, 0), (0x19, 0)),
                FactionActor(0x71, (0x16, 1), (0x19, 0)),
                Actor(7, 0x10), Actor(8, 0x11), Record("ACTI", 9, Field("SCRI", U32(0x50))),
                Record("PACK", 0x40, Field("PTDT", Join(U32(0), U32(0x92), new byte[8]))),
                Record("SCPT", 0x50, Field("SCHR", new byte[20]), Field("SCRO", U32(0x10)), Field("SCRO", U32(0x11)),
                    Field("SCTX", Text("begin OnActivate\nSetAlly FirstFaction SecondFaction 1 0\nend"))),
                Record("QUST", 0x60, Field("DATA", [1, 0]), Field("SCRI", U32(0x51))),
                Record("SCPT", 0x51, Field("SCHR", QuestHeader()), Field("SCRO", U32(0x10)), Field("SCRO", U32(0x11)),
                    Field("SCTX", Text("begin GameMode\nSetEnemy FirstFaction SecondFaction 1 0\nend"))),
                Record("CELL", 0x80, Field("DATA", [1])), Group(0x80,
                    Reference("REFR", 0x90, 9), Reference("ACHR", 0x91, 7), Reference("ACHR", 0x92, 8),
                    Reference("ACHR", 0x94, 0x70), Reference("ACHR", 0x95, 0x71)),
                Record("CELL", 0x81, Field("DATA", [1])), Group(0x81, Reference("ACHR", 0x93, 8))));
            File.WriteAllBytes(Path.Combine(directory.FullName, "Patch.esp"), Join(Header("Relations.esm"), Faction(0x10, "FirstFaction", 3),
                Record("PACK", 0x40, Field("PTDT", Join(U32(0), U32(0x92), new byte[8])),
                    Field("CTDA", SameCellCondition(0x14)))));
            using var records = FalloutPluginStack.Load(directory.FullName, ["Relations.esm", "Patch.esp"]);
            ExerciseArbitration(records);
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

            float Query(FalloutReferenceWorld current, FalloutCondition condition, FalloutReferencePlacement? player = null) =>
                new FalloutConditionRuntime(records, new(records), current) { PlayerPlacement = () => player }
                    .Evaluate(condition, new(Key(0x91)));
            var owner = records.GetEffective(Key(0x40));
            var value = new FalloutCondition(owner, 0, 0, 14, 63, 0, 0, 0);
            Require(Query(world, value) == 0, "Initial user actor value was not source zero.");
            world.ChangeActorValue(Key(0x91), "variable02", "setav", 5);
            world.ChangeActorValue(Key(0x92), "variable02", "setav", 9);
            Require(Query(world, value) == 5 &&
                Query(world, value with { RunOn = 1 }) == 9 &&
                Query(world, value with { RunOn = 2, Reference = 0x92 }) == 9,
                "Actor-value scope did not resolve actual self, package target and explicit reference.");
            Reject(() => Query(world, value with { Argument1 = 47 }));
            Reject(() => Query(world, value with { RunOn = 3 }));
            var distance = value with { Function = 1, Argument1 = 0x14 };
            world.Get(Key(0x91)).QuerySpatialPlacement = () => new(Key(0x80), [3, 4, 12], [0, 0, 0]);
            Require(Query(world, distance, new(Key(0x80), [0, 0, 0], [0, 0, 0])) == 13,
                "Distance used stale editor placement instead of current native actor and player poses.");
            Reject(() => Query(world, distance));
            // Read the winning condition from complete binary records. Its placed
            // target is independent of the package's scoped subject.
            var sameCell = FalloutCondition.Read(owner).Single();
            Require(owner.Plugin.Name == "Patch.esp" && sameCell.Function == 32 && sameCell.FormArgument1 == records.RuntimeFormKey(0x14),
                "Same-cell condition ignored its winning source record/master adjustment.");
            var playerHere = new FalloutReferencePlacement(Key(0x80), [0, 0, 0], [0, 0, 0]);
            var playerElsewhere = playerHere with { Cell = Key(0x81) };
            Require(Query(world, sameCell, playerHere) == 1 &&
                Query(world, sameCell, playerElsewhere) == 0 &&
                FalloutCondition.AllPass([sameCell], condition => Query(world, condition, playerHere)),
                "Same-cell package predicate did not follow actual player cell ownership.");
            Require(Query(world, sameCell with { Argument1 = 0x92 }) == 1 &&
                Query(world, sameCell with { Argument1 = 0x93 }) == 0,
                "Reference-target same-cell query used the package target or residency in place of its source argument.");
            world.SetPlacement(Key(0x92), playerElsewhere);
            Require(Query(world, sameCell with { RunOn = 1 }, playerHere) == 0 &&
                Query(world, sameCell with { RunOn = 2, Reference = 0x93 }, playerElsewhere) == 1 &&
                Query(world, sameCell with { Argument1 = 0x92 }) == 0,
                "Same-cell scope ignored current moved placement, package target or explicit source reference.");
            Reject(() => Query(world, sameCell));
            Reject(() => Query(world, sameCell with { Argument2 = 1 }, playerHere));
            Reject(() => Query(world, sameCell with { RunOn = 3 }, playerHere));
            Reject(() => Query(world, sameCell with { RunOn = 2, Reference = 0 }, playerHere));
            world.Get(Key(0x91)).QuerySpatialPlacement = () => playerElsewhere;
            Reject(() => Query(world, sameCell, playerElsewhere));
            world.Get(Key(0x91)).QuerySpatialPlacement = null;
            var spatialState = world.Capture();
            using var spatialCold = new FalloutReferenceWorld(records);
            spatialCold.Restore(JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(spatialState))!);
            var coldBeforeQuery = JsonSerializer.Serialize(spatialCold.Capture());
            Require(Query(spatialCold, sameCell, playerHere) == 1 &&
                Query(spatialCold, sameCell with { RunOn = 1 }, playerHere) == 0 &&
                Query(spatialCold, sameCell with { Argument1 = 0x92 }) == 0 &&
                JsonSerializer.Serialize(spatialCold.Capture()) == coldBeforeQuery,
                "Cold same-cell query lost moved cell state or mutated authoritative state.");
            Reject(() => Query(world, sameCell with { Function = 45 }, playerHere));
            Console.WriteLine("OPENNV_AI_SAME_CELL_CONTRACT_PASS fullBinaryReader=true winningMaster=true actualPlayerCell=true placedReferenceTarget=true scopedSubject=true movedCold=true nativeCellMismatchRefused=true unknownDetectionRefused=true");
            Console.WriteLine("OPENNV_FACTION_AI_REFERENCE_CONTRACT_PASS winningSource=true directional=true objectAndQuest=true liveCombat=true cold=true atomicReject=true actualActorValues=true scopedQueries=true nativePoseDistance=true unsupportedRefused=true");
        }
        finally { directory.Delete(true); }
    }

    private static FalloutFormKey Key(uint id) => new("Relations.esm", id);
    private static void ExerciseArbitration(FalloutPluginStack records)
    {
        // Explicit contract table, indexed by reaction labels 0..3. This
        // deliberately differs from numeric maximum (ally 2 beats friend 3).
        uint[,] expected = { { 0, 1, 2, 3 }, { 1, 1, 2, 3 }, { 2, 2, 2, 2 }, { 3, 3, 2, 3 } };
        var to = new Dictionary<FalloutFormKey, sbyte> { [Key(0x16)] = 0 };
        for (uint first = 0; first < 4; first++)
            for (uint second = 0; second < 4; second++)
                foreach (var reverse in new[] { false, true })
                {
                    var from = new Dictionary<FalloutFormKey, sbyte>();
                    foreach (var key in reverse ? new uint[] { 0x13, 0x12 } : new uint[] { 0x12, 0x13 }) from.Add(Key(key), 0);
                    Require(FalloutActorThreat.Relation(records, from, to,
                        (faction, _) => faction == Key(0x12) ? first : second) == expected[first, second],
                        "Mixed faction reaction ignored the contract table or depended on membership order.");
                }
        uint[][] orders = [[0x12, 0x13, 0x14], [0x12, 0x14, 0x13], [0x13, 0x12, 0x14],
            [0x13, 0x14, 0x12], [0x14, 0x12, 0x13], [0x14, 0x13, 0x12]];
        foreach (var order in orders)
        {
            var from = order.ToDictionary(Key, _ => (sbyte)0);
            Require(FalloutActorThreat.Relation(records, from, to,
                (faction, _) => faction == Key(0x12) ? 1u : faction == Key(0x13) ? 3u : 2u) == 2,
                "Three-way actor arbitration did not retain ally priority.");
        }
        var active = new Dictionary<FalloutFormKey, sbyte> { [Key(0x12)] = 0, [Key(0x13)] = 127, [Key(0x14)] = -1 };
        Require(FalloutActorThreat.Relation(records, active, to,
            (faction, _) => faction == Key(0x14) ? throw new InvalidOperationException("Removed membership was queried.") : faction == Key(0x12) ? 1u : 3u) == 3,
            "Removed rank participated or active rank magnitude replaced reaction priority.");
        active[Key(0x12)] = 127; active[Key(0x13)] = 0;
        Require(FalloutActorThreat.Relation(records, active, to,
            (faction, _) => faction == Key(0x12) ? 1u : 3u) == 3,
            "A higher active membership rank replaced the stronger combat relation.");
        Reject(() => FalloutActorThreat.Relation(records, active, to, (faction, _) => faction == Key(0x12) ? 2u : 4u));
        foreach (var invalid in new uint[] { 0x17, 0x18 })
            Reject(() => FalloutActorThreat.Relation(records,
                new Dictionary<FalloutFormKey, sbyte> { [Key(0x1a)] = 0, [Key(invalid)] = 0 },
                new Dictionary<FalloutFormKey, sbyte> { [Key(0x1a)] = 0 }));
        var shared = new Dictionary<FalloutFormKey, sbyte> { [Key(0x19)] = 0 };
        Require(FalloutActorThreat.Relation(records, shared, shared) == 0 &&
            FalloutActorThreat.Relation(records,
                new Dictionary<FalloutFormKey, sbyte> { [Key(0x12)] = 0, [Key(0x19)] = 0 },
                new Dictionary<FalloutFormKey, sbyte> { [Key(0x16)] = 0, [Key(0x19)] = 0 }) == 1,
            "Shared empty faction invented an ally or masked a real enemy pair.");
        foreach (var (faction, reaction) in new (uint, uint)[] { (0x1a, 2), (0x1b, 3), (0x1c, 1), (0x1d, 0) })
        {
            var self = new Dictionary<FalloutFormKey, sbyte> { [Key(faction)] = 0 };
            Require(FalloutActorThreat.Relation(records, self, self) == reaction,
                "Authored self XNAM was replaced by an implicit shared-membership reaction.");
        }
        for (uint reaction = 0; reaction < 4; reaction++)
            Require(FalloutActorThreat.Relation(records, shared, shared, (_, _) => reaction) == reaction,
                "Directed current self-pair query was bypassed for shared membership.");

        using var world = new FalloutReferenceWorld(records);
        world.LoadCell(FalloutCellSceneReader.Read(records, Key(0x80)));
        Require(world.ActorRelation(Key(0x94), Key(0x95)) == 3 && world.ActorRelation(Key(0x95), Key(0x94)) == 1,
            "Winning FACT pairs did not preserve directional friend-over-enemy arbitration independently of disposition scores.");
        world.ChangeFaction(Key(0x94), Key(0x14), 0, false);
        Require(world.ActorRelation(Key(0x94), Key(0x95)) == 2, "New active ally membership did not outrank friend and enemy.");
        world.ChangeFaction(Key(0x94), Key(0x14), -1, false);
        world.SetFactionRelationship(Key(0x13), Key(0x16), false, 0, 1);
        Require(world.ActorRelation(Key(0x94), Key(0x95)) == 1, "Live enemy overlay did not replace its source friend pair.");
        world.SetFactionRelationship(Key(0x12), Key(0x16), true, 0, 1);
        Require(world.ActorRelation(Key(0x94), Key(0x95)) == 2 && world.ActorRelation(Key(0x95), Key(0x94)) == 3,
            "Directional live ally/friend overlays did not outrank the remaining enemy inputs.");
        for (uint reaction = 0; reaction < 4; reaction++)
        {
            var friendly = reaction is 2 or 3;
            var flag = reaction is 0 or 3 ? 1 : 0;
            world.SetFactionRelationship(Key(0x19), Key(0x19), friendly, flag, flag);
            Require(FalloutActorThreat.Relation(records, shared, shared, world.FactionCombatReaction) == reaction,
                "Actual dynamic self overlay was replaced by shared-membership inference.");
        }
        var references = JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!;
        var overrides = JsonSerializer.Deserialize<FalloutActorOverrides[]>(JsonSerializer.Serialize(world.CaptureActorOverrides()))!;
        var relations = JsonSerializer.Deserialize<FalloutFactionRelationSnapshot[]>(JsonSerializer.Serialize(world.CaptureFactionRelations()))!;
        using var cold = new FalloutReferenceWorld(records);
        cold.Restore(references); cold.RestoreActorOverrides(overrides); cold.RestoreFactionRelations(relations);
        Require(cold.ActorRelation(Key(0x94), Key(0x95)) == 2 && cold.ActorRelation(Key(0x95), Key(0x94)) == 3 &&
            FalloutActorThreat.Relation(records, shared, shared, cold.FactionCombatReaction) == 3 &&
            cold.ActorFactions(Key(0x94))[Key(0x14)] == -1 &&
            JsonSerializer.Serialize(cold.CaptureActorOverrides()) == JsonSerializer.Serialize(world.CaptureActorOverrides()) &&
            JsonSerializer.Serialize(cold.CaptureFactionRelations()) == JsonSerializer.Serialize(world.CaptureFactionRelations()),
            "Cold arbitration changed removed membership, directional overlays or their source identities.");
        Console.WriteLine("OPENNV_FACTION_ARBITRATION_PASS allReactionPairs=true orderIndependent=true threeWay=true allyOverFriendOverEnemy=true dispositionScoreIndependent=true removedRank=true directional=true liveOverlays=true cold=true malformedAfterAllyRefused=true sharedEmptyNeutral=true authoredAndDynamicSelfPairs=true assistanceAndRetailParity=unverified");
    }

    private static byte[] ReactionFaction(uint id, string name, uint target, uint reaction, int disposition) =>
        Record("FACT", id, Field("EDID", Text(name)), Field("XNAM", Join(U32(target), BitConverter.GetBytes(disposition), U32(reaction))));
    private static byte[] FactionActor(uint id, params (uint Faction, sbyte Rank)[] factions) =>
        Record("NPC_", id, [Field("ACBS", new byte[24]), Field("AIDT", new byte[20]),
            .. factions.Select(value => Field("SNAM", Join(U32(value.Faction), [unchecked((byte)value.Rank), 0, 0, 0])))]);
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
    private static byte[] SameCellCondition(uint target)
    {
        var bytes = new byte[28]; BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(4), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(8), 32); U32(target).CopyTo(bytes, 12); return bytes;
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
