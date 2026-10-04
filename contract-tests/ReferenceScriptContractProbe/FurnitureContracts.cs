using System.Buffers.Binary;
using System.Text;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static class FurnitureContracts
{
    internal static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-furniture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllBytes(Path.Combine(directory, "Seats.esm"), Join(Header(),
                Record("NPC_", 0x700, Field("EDID", Text("SeatActorBase")), Field("ACBS", new byte[24]),
                    Field("SNAM", Join(BitConverter.GetBytes(0x730u), new byte[] { 3, 0, 0, 0 }))),
                Record("NPC_", 0x701, Field("ACBS", new byte[24])), Record("FACT", 0x730),
                Record("STAT", 0x3b), Record("FURN", 0x710, Field("MNAM", BitConverter.GetBytes(0x40000001u))),
                Record("FURN", 0x711, Field("MNAM", BitConverter.GetBytes(0x80000001u))),
                Record("CELL", 0x800, Field("DATA", [1])), References()));
            var sittingCondition = new byte[28]; BinaryPrimitives.WriteUInt16LittleEndian(sittingCondition.AsSpan(8), 159);
            File.WriteAllBytes(Path.Combine(directory, "Find.esp"), Join(Header("Seats.esm"),
                Package(0x01000400, 2, 11), Package(0x01000401, 0, 0x901), Package(0x01000402, 1, 0x710),
                Package(0x01000403, 2, 12), Package(0x01000404, 2, 11, count: 1), Package(0x01000405, 2, 11, flags: 0x9006),
                Record("SCPT", 0x01000500, Field("SCRO", BitConverter.GetBytes(0x900u)),
                    Field("SCRO", BitConverter.GetBytes(0x90au)),
                    Field("SCRO", BitConverter.GetBytes(0x901u)), Field("SCRO", BitConverter.GetBytes(0x14u))),
                Record("INFO", 0x01000501, Field("CTDA", sittingCondition))));
            using var records = FalloutPluginStack.Load(directory, ["Seats.esm", "Find.esp"]);
            using var world = new FalloutReferenceWorld(records);
            FalloutFormKey Key(uint id) => new("Seats.esm", id);
            var cell = FalloutCellSceneReader.Read(records, Key(0x800)); world.LoadCell(cell);
            var source = FalloutFindFurniturePackage.Read(records.GetEffective(new("Find.esp", 0x400)));
            var actor = Key(0x900); var chair = Key(0x901); var otherActor = Key(0x90a); var player = Key(0x14);
            Require(source.Location == Key(0x905) && source.Radius == 5 && !source.Running, "Find lost its declaring master or source search radius.");
            uint[] Candidates(FalloutFindFurniturePackage package) => package.Candidates(records, world, actor, cell).Select(reference => reference.FormKey.ObjectId).Order().ToArray();
            Require(Candidates(source).SequenceEqual(new uint[] { 0x901, 0x902, 0x903, 0x909 }), "Find did not apply radius, sitting type and actor/faction ownership.");
            world.Get(Key(0x902)).Enabled = false;
            Require(Candidates(source).SequenceEqual(new uint[] { 0x901, 0x903, 0x909 }), "Find selected disabled furniture.");
            world.SetPlacement(chair, new(Key(0x800), [20, 0, 0], [0, 0, 0]));
            Require(!Candidates(source).Contains(0x901u), "Find ignored an authoritative moved chair.");
            world.SetPlacement(chair, new(Key(0x800), [1, 0, 0], [0, 0, 0]));
            Require(Candidates(FalloutFindFurniturePackage.Read(records.GetEffective(new("Find.esp", 0x401)))).SequenceEqual(new uint[] { 0x901 }), "Find explicit target lost its adjusted reference.");
            Require(Candidates(FalloutFindFurniturePackage.Read(records.GetEffective(new("Find.esp", 0x402)))).SequenceEqual(Candidates(source)), "Find base target lost its source identity.");
            foreach (var id in new uint[] { 0x403, 0x404, 0x405 }) Reject(() => FalloutFindFurniturePackage.Read(records.GetEffective(new("Find.esp", id))));
            Require(world.ReserveFurnitureSeat(chair, 0, actor) && world.ReserveFurnitureSeat(chair, 0, actor) &&
                !world.ReserveFurnitureSeat(chair, 0, otherActor) && !world.ReserveFurnitureSeat(chair, 0, player), "Source seat reservation allowed competing actors.");
            world.ReleaseFurnitureSeat(chair, 0, otherActor);
            Require(!world.ReserveFurnitureSeat(chair, 0, player), "Another actor released the source reservation.");
            world.ReleaseFurnitureSeat(chair, 0, actor);
            var instances = world.InstanceCount;
            Require(world.ReserveFurnitureSeat(chair, 0, player) && !world.ReserveFurnitureSeat(chair, 0, actor) && world.InstanceCount == instances,
                "Player reservation invented a reference record or lost exclusive ownership.");
            world.ReleaseFurnitureSeat(chair, 0, player);
            SittingContracts(records, world, actor, chair, player);
            ReferenceQueries(records, world, actor, chair, otherActor);
            Console.WriteLine("OPENNV_FIND_FURNITURE_CONTRACT_PASS sourceMasters=true radius=true actualPlacement=true enabled=true actorFactionOwnership=true targetKinds=true reservations=true playerRecordAbsent=true unsupportedRefused=true");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static void ReferenceQueries(FalloutPluginStack records, FalloutReferenceWorld world,
        FalloutFormKey actor, FalloutFormKey chair, FalloutFormKey otherActor)
    {
        Require(world.GetLinkedRef(actor) == otherActor && world.GetLinkedRef(chair) is null,
            "Linked reference lost its source master or invented an absent link.");
        Reject(() => world.GetLinkedRef(new("Find.esp", 0x400)));
        var effects = 0;
        var scripts = new FalloutReferenceScripts(records, world, new(records), new((_, _) => false, _ => effects++));
        var script = records.GetEffective(new("Find.esp", 0x500));
        void Query(FalloutFormKey caller, string expression)
        {
            effects = 0;
            scripts.ExecuteProgram(records.GetEffective(caller), script,
                FalloutGameModeProgram.Read($"begin GameMode\nif {expression}\nActivate\nendif\nend"), 0);
            Require(effects == 1, "Reference history/link query lost its caller or typed linked result.");
        }
        world.Get(actor).TalkedToPlayer = false; world.Get(otherActor).TalkedToPlayer = true;
        Query(actor, "GetTalkedToPC == 0 && SeatActor.GetTalkedToPC == 0 && ( GetSelf ).GetTalkedToPC == 0");
        Query(actor, "GetLinkedRef == OtherActor && ( GetLinkedRef ).GetTalkedToPC == 1");
        Query(chair, "GetLinkedRef == 0");
        world.Get(actor).TalkedToPlayer = true;
        Query(actor, "GetTalkedToPC == 1");
        using var cold = new FalloutReferenceWorld(records); cold.Restore(world.Capture());
        Require(cold.GetTalkedToPlayer(actor) && cold.GetLinkedRef(actor) == otherActor,
            "Cold reference queries lost authoritative history or source links.");
        Reject(() => world.GetTalkedToPlayer(chair));
        Console.WriteLine("OPENNV_REFERENCE_HISTORY_LINK_PASS self=true explicit=true postfix=true typedLink=true sourceMasters=true absentNull=true live=true cold=true invalidOwnerRefused=true");
    }

    private static void SittingContracts(FalloutPluginStack records, FalloutReferenceWorld world,
        FalloutFormKey actor, FalloutFormKey chair, FalloutFormKey player)
    {
        var state = 0; var playerState = 0;
        world.Get(actor).QuerySitting = () => state;
        var script = records.GetEffective(new("Find.esp", 0x500));
        var effects = 0;
        var scripts = new FalloutReferenceScripts(records, world, new(records), new((_, _) => false, _ => effects++,
            Sitting: reference => reference == player ? playerState : world.GetSitting(reference)));
        void Query(string expression, bool expected)
        {
            effects = 0;
            scripts.ExecuteProgram(records.GetEffective(actor), script,
                FalloutGameModeProgram.Read($"begin GameMode\nif {expression}\nActivate\nendif\nend"), 0);
            Require(effects == (expected ? 1 : 0), "GetSitting borrowed another actor or lost its live procedure phase.");
        }
        for (state = 0; state <= 4; state++)
        {
            Require(world.GetSitting(actor) == state, "GetSitting changed a physical procedure code.");
            Query($"GetSitting == {state}", true);
            Query($"SeatActor.GetSitting == {state}", true);
            Query($"( GetSelf ).GetSitting == {state}", true);
            Query("player.GetSitting == 3", false);
        }
        state = 3; playerState = 3;
        Query("player.GetSitting == 3 && GetSitting == 3", true);
        state = 4; Query("GetSitting == 3", false);
        var condition = FalloutCondition.Read(records.GetEffective(new("Find.esp", 0x501))).Single();
        int Sitting(FalloutFormKey reference) => reference == player ? playerState : world.GetSitting(reference);
        var identity = new FalloutDialogueSpeaker(new("Seats.esm", 0x700), new("Seats.esm", 0x700),
            new("Seats.esm", 0x740), "FixtureVoice", null, false);
        var conditions = new FalloutDialogueConditions(records, new(records), actor, identity, sitting: Sitting);
        Require(conditions.Evaluate(condition) == 4 && conditions.Evaluate(condition with { RunOn = 1 }) == 3 &&
            conditions.Evaluate(condition with { RunOn = 2, Reference = 0x900 }) == 4, "Dialogue GetSitting lost self, player or declaring-master scope.");
        Reject(() => conditions.Evaluate(condition with { RunOn = 3 }));
        Reject(() => conditions.Evaluate(condition with { RunOn = 2, Reference = 0x901 }));
        Reject(() => world.GetSitting(chair, _ => 3));
        state = 5; Reject(() => world.GetSitting(actor));
        world.Get(actor).QuerySitting = null; Reject(() => world.GetSitting(actor)); Reject(() => world.GetSitting(player));
        Console.WriteLine("OPENNV_GET_SITTING_CONTRACT_PASS self=true explicit=true postfix=true player=true sourceMasters=true phases=true live=true missingOwnerRefused=true invalidActorRefused=true");
    }

    private static byte[] References()
    {
        byte[] Ref(string signature, uint id, uint baseId, float x, uint? owner = null, string? name = null, uint? linked = null) => Record(signature, id,
            Field("NAME", BitConverter.GetBytes(baseId)), Field("DATA", new float[] { x, 0, 0, 0, 0, 0 }.SelectMany(BitConverter.GetBytes).ToArray()),
            owner is { } key ? Field("XOWN", BitConverter.GetBytes(key)) : [], name is null ? [] : Field("EDID", Text(name)),
            linked is { } link ? Field("XLKR", BitConverter.GetBytes(link)) : []);
        var body = Join(Ref("ACHR", 0x900, 0x700, 0, name: "SeatActor", linked: 0x90a), Ref("ACHR", 0x90a, 0x701, 0, name: "OtherActor"),
            Ref("REFR", 0x901, 0x710, 1), Ref("REFR", 0x902, 0x710, 5), Ref("REFR", 0x903, 0x710, 3, 0x700),
            Ref("REFR", 0x904, 0x710, 4, 0x701), Ref("REFR", 0x905, 0x3b, 0), Ref("REFR", 0x907, 0x710, 6),
            Ref("REFR", 0x908, 0x711, 1), Ref("REFR", 0x909, 0x710, 2, 0x730));
        var group = new byte[24 + body.Length]; Encoding.ASCII.GetBytes("GRUP").CopyTo(group, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(4), (uint)group.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(8), 0x800); BinaryPrimitives.WriteInt32LittleEndian(group.AsSpan(12), 6);
        body.CopyTo(group, 24); return group;
    }

    private static byte[] Package(uint id, int targetType, uint target, int count = 0, uint flags = 0x1006)
    {
        var data = new byte[12]; BinaryPrimitives.WriteUInt32LittleEndian(data, flags);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(6), 0x31);
        return Record("PACK", id, Field("EDID", Text("Find" + id)), Field("PKDT", data),
            Field("PLDT", Join(BitConverter.GetBytes(0), BitConverter.GetBytes(0x905u), BitConverter.GetBytes(5))),
            Field("PTDT", Join(BitConverter.GetBytes(targetType), BitConverter.GetBytes(target), BitConverter.GetBytes(count), new byte[4])));
    }
    private static byte[] Header(string? master = null) => Record("TES4", 0, Field("HEDR", new byte[12]),
        master is null ? [] : Join(Field("MAST", Text(master)), Field("DATA", new byte[8])));
    private static byte[] Text(string value) => Encoding.ASCII.GetBytes(value + '\0');
    private static byte[] Field(string name, byte[] data)
    {
        var result = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(result, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(4), checked((ushort)data.Length)); data.CopyTo(result, 6); return result;
    }
    private static byte[] Record(string name, uint id, params byte[][] fields)
    {
        var body = Join(fields); var result = new byte[24 + body.Length]; Encoding.ASCII.GetBytes(name).CopyTo(result, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), (uint)body.Length); BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(12), id);
        body.CopyTo(result, 24); return result;
    }
    private static byte[] Join(params byte[][] values) => values.SelectMany(value => value).ToArray();
    private static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidDataException("Unowned furniture input was admitted.");
    }
}
