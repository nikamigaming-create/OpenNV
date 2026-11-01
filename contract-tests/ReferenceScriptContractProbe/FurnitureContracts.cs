using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Cells;

internal static partial class FurnitureContracts
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
                Record("DOOR", 0x717), Record("STAT", 0x3b), Record("IDLE", 0x712), Record("FURN", 0x710, Field("MNAM", BitConverter.GetBytes(0x40000001u))),
                Record("FURN", 0x711, Field("MNAM", BitConverter.GetBytes(0x80000001u))),
                Record("FURN", 0x714, Field("MNAM", BitConverter.GetBytes(0x40000003u))),
                Record("GMST", 0x720, Field("EDID", Text("fFurnitureMarker14DeltaX")), Field("DATA", BitConverter.GetBytes(.25f))),
                Record("GMST", 0x721, Field("EDID", Text("fFurnitureMarker14DeltaY")), Field("DATA", BitConverter.GetBytes(-1.5f))),
                Record("GMST", 0x722, Field("EDID", Text("fFurnitureMarker14DeltaZ")), Field("DATA", BitConverter.GetBytes(3f))),
                Record("GMST", 0x723, Field("EDID", Text("fFurnitureMarker14HeadingDelta")), Field("DATA", BitConverter.GetBytes(.125f))),
                Record("ALCH", 0x713, Field("ENIT", new byte[3])),
                Record("ALCH", 0x715, Field("ENIT", FoodHeader())), Record("ALCH", 0x716, Field("ENIT", FoodHeader())),
                Record("CELL", 0x800, Field("DATA", [1])), References()));
            var sittingCondition = new byte[28]; BinaryPrimitives.WriteUInt16LittleEndian(sittingCondition.AsSpan(8), 159);
            File.WriteAllBytes(Path.Combine(directory, "Find.esp"), Join(Header("Seats.esm"),
                Package(0x01000400, 2, 11), Package(0x01000401, 0, 0x901), Package(0x01000402, 1, 0x710),
                Package(0x01000403, 2, 12), Package(0x01000404, 2, 11, count: 1), Package(0x01000405, 2, 11, flags: 0x9006),
                SleepFixture(0x01000406, 1, 0x800), SleepFixture(0x01000407, 0, 0x905, 5),
                SleepFixture(0x01000408, 2, 0, 5), SleepFixture(0x01000409, 3, 0, 5),
                SleepFixture(0x0100040a, 4, 0), SleepFixture(0x0100040b, 1, 0x800, flags: 0x40000000),
                EatFixture(0x0100040c), EatFixture(0x0100040d, foodType: 1, food: 0x715),
                EatFixture(0x0100040e, foodType: 0, food: 0x901), EatFixture(0x0100040f, count: 1),
                EatFixture(0x01000410, location: 4), SleepFixture(0x01000411, 1, 0x800, flags: 0x88),
                SleepFixture(0x01000412, 1, 0x800, flags: 0x20),
                Record("SCPT", 0x01000500, Field("SCRO", BitConverter.GetBytes(0x900u)),
                    Field("SCRO", BitConverter.GetBytes(0x90au)),
                    Field("SCRO", BitConverter.GetBytes(0x901u)), Field("SCRO", BitConverter.GetBytes(0x14u))),
                Record("INFO", 0x01000501, Field("CTDA", sittingCondition))));
            using var records = FalloutPluginStack.Load(directory, ["Seats.esm", "Find.esp"]);
            using var world = new FalloutReferenceWorld(records);
            FalloutFormKey Key(uint id) => new("Seats.esm", id);
            var permitted = records.GetEffective(Key(0x714));
            var physicalSeats = FalloutFurnitureSource.ReadSeats(records, permitted, FurnitureMarkers(1));
            Require(physicalSeats.Count == 1 && physicalSeats[0].Index == 0 && physicalSeats[0].MarkerId == 14 &&
                physicalSeats[0].PlacementOffset.SequenceEqual([.25f, -1.5f, 3f]) && physicalSeats[0].HeadingDelta == .125f,
                "Unused furniture permission bits invented seats or confused physical ordinals with marker type/settings.");
            Require(FalloutFurnitureSource.ReadSeats(records, permitted, FurnitureMarkers(33)).Select(seat => seat.Index).SequenceEqual([0, 1]),
                "Furniture positions outside the lower thirty mask bits wrapped into an enabled ordinal.");
            Reject(() => FalloutFurnitureSource.ReadSeats(records, permitted, FurnitureMarkers(0)));
            var cell = FalloutCellSceneReader.Read(records, Key(0x800)); world.LoadCell(cell);
            SleepContracts(records, cell);
            EatContracts(records, cell);
            LinkedSandboxContracts();
            var source = FalloutFindFurniturePackage.Read(records.GetEffective(new("Find.esp", 0x400)));
            var actor = Key(0x900); var chair = Key(0x901); var otherActor = Key(0x90a); var player = Key(0x14);
            var sandbox = new FalloutSandboxPackage(new("Find.esp", 0x400), 0, 0, Key(0x905), 5, new(31));
            var wanderOnly = world.SandboxCandidateSources(actor, sandbox, 1, null, .0142875f);
            Require(wanderOnly.Count == 1 && wanderOnly[0].Activity == FalloutSandboxActivity.Wander,
                "Sandbox inspected excluded food or admitted an excluded activity.");
            Reject(() => world.SandboxCandidateSources(actor, sandbox with { Activities = new(30) }, 1, null, .0142875f));
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
            ColdFurniture(records, cell, actor, chair, otherActor);
            StoppedProcedureContracts();
            PackageSittingContracts();
            Console.WriteLine("OPENNV_FIND_FURNITURE_CONTRACT_PASS sourceMasters=true radius=true actualPlacement=true enabled=true actorFactionOwnership=true targetKinds=true reservations=true playerRecordAbsent=true unsupportedRefused=true");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static void ColdFurniture(FalloutPluginStack records, FalloutCellScene cell,
        FalloutFormKey actor, FalloutFormKey chair, FalloutFormKey otherActor)
    {
        var package = records.GetEffective(new("Find.esp", 0x400));
        var assignment = new FalloutActorPackageAssignment(package.FormKey, FalloutActorFurnitureContinuation.RecordHash(package), false);
        const string resource = "meshes/fixture-chair-loop.kf";
        var hash = new string('A', 64);
        float[] pose = [1, 0, 0, 0, 1, 0, 0, 0, 1, 1, 2, 3];
        var search = new FalloutActorFurnitureContinuation(assignment, 1, "POBA", package.FormKey,
            assignment.Sha256, 0, .125, pose, package.FormKey, null, 123456, 3.5, null, null);
        using var warm = new FalloutReferenceWorld(records);
        warm.LoadCell(cell);
        var owner = warm.Get(actor);
        owner.QuerySitting = null;
        owner.PackageAssignment = assignment;
        owner.Animation.Restore(new(resource, hash, .3125, false));
        owner.FurnitureContinuation = search;
        owner.ProcedureCaptureBlocker = FalloutActorFurnitureContinuation.CaptureBlocker;
        var saved = warm.Capture();
        using (var coldSearch = new FalloutReferenceWorld(records))
        {
            coldSearch.Restore(saved);
            Require(coldSearch.GetSitting(actor) == 0 && coldSearch.PendingProcedureCaptureCount == 0 &&
                JsonSerializer.Serialize(coldSearch.Capture()) == JsonSerializer.Serialize(saved),
                "Cold furniture search changed its source assignment, clock, polling or random state.");
        }
        var furniture = records.GetEffective(warm.Get(chair).Base);
        var idle = records.GetEffective(new("Seats.esm", 0x712));
        var seat = new FalloutFurnitureSeat(furniture.FormKey, 0, 1, new(new(0, 0, 0), 0, 1, 1), [0, 0, 0], 0);
        var occupied = search with
        {
            Assignment = assignment with { Done = true },
            Phase = 3,
            Furniture = chair,
            FurnitureSha256 = FalloutActorFurnitureContinuation.RecordHash(furniture),
            Model = "meshes/fixture-chair.nif",
            ModelSha256 = hash,
            Seat = seat,
            Occupied = (float[])pose.Clone(),
            Clip = new(idle.FormKey, FalloutActorFurnitureContinuation.RecordHash(idle), resource, hash)
        };
        owner.PackageAssignment = occupied.Assignment;
        owner.FurnitureContinuation = occupied;
        saved = warm.Capture();
        var approach = occupied with
        {
            Assignment = assignment,
            Phase = 1,
            Approach = (float[])pose.Clone(),
            ApproachFloor = [1, 2, 3]
        };
        owner.PackageAssignment = assignment;
        owner.FurnitureContinuation = approach;
        using (var approaching = new FalloutReferenceWorld(records))
        {
            approaching.Restore(JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(warm.Capture()))!);
            Require(approaching.GetSitting(actor) == 0 && approaching.OwnsFurnitureSeat(chair, 0, actor),
                "Retained approach changed public sitting state or released its selected seat.");
        }
        var independentApproach = approach.Copy();
        independentApproach.Approach![9] += 1;
        Require(independentApproach.Approach[9] != approach.Approach![9], "Furniture approach snapshots share mutable pose buffers.");
        owner.PackageAssignment = occupied.Assignment;
        owner.FurnitureContinuation = occupied;
        using (var cold = new FalloutReferenceWorld(records))
        {
            cold.Restore(JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(saved))!);
            Require(cold.GetSitting(actor) == 3 && cold.OwnsFurnitureSeat(chair, 0, actor) &&
                !cold.ReserveFurnitureSeat(chair, 0, otherActor), "Cold occupied chair lost its exclusive source reservation.");
            cold.Get(actor).Enabled = false;
            Require(cold.OwnsFurnitureSeat(chair, 0, actor) && !cold.ReserveFurnitureSeat(chair, 0, otherActor),
                "Disabled saved occupant lost its retained physical reservation.");
            cold.ReleaseFurnitureSeat(chair, 0, otherActor);
            Require(cold.OwnsFurnitureSeat(chair, 0, actor), "Foreign actor released the cold reservation.");
            cold.ReleaseFurnitureSeat(chair, 0, actor);
            Require(cold.ReserveFurnitureSeat(chair, 0, otherActor), "Retirement did not release the cold reservation.");
        }
        void AtomicReject(FalloutReferenceSnapshot[] candidate)
        {
            using var refused = new FalloutReferenceWorld(records);
            Reject(() => refused.Restore(candidate));
            Require(refused.InstanceCount == 0 && !refused.OwnsFurnitureSeat(chair, 0, actor),
                "Rejected furniture save partially published references or reservations.");
        }
        FalloutReferenceSnapshot[] Replace(FalloutActorFurnitureContinuation changed) => saved.Select(value =>
            value.Reference == actor ? value with { FurnitureContinuation = changed } : value).ToArray();
        AtomicReject(Replace(occupied with { ModelSha256 = "bad" }));
        AtomicReject(Replace(occupied with { FurnitureSha256 = new string('B', 64) }));
        AtomicReject(Replace(occupied with { Clip = occupied.Clip! with { Sha256 = new string('B', 64) } }));
        AtomicReject(Replace(occupied with { Seat = seat with { Index = 1 } }));
        AtomicReject(Replace(occupied with { Pose = [float.NaN, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0] }));
        AtomicReject(Replace(occupied with { Phase = 2 }));
        var competing = saved.Select(value => value.Reference == otherActor ? value with
        { PackageAssignment = occupied.Assignment, Animation = owner.Animation.Capture(), FurnitureContinuation = occupied.Copy() } : value).ToArray();
        AtomicReject(competing);
        Console.WriteLine("OPENNV_FURNITURE_COLD_CONTRACT_PASS search=true sourceAssignment=true phase=true animationClock=true reservation=true disabledLease=true atomicReject=true nativeMotion=unverified");
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
        byte[] Door(uint id, uint target)
        {
            var teleport = new byte[32]; BitConverter.GetBytes(target).CopyTo(teleport, 0);
            return Record("REFR", id, Field("NAME", BitConverter.GetBytes(0x717u)),
                Field("DATA", new byte[24]), Field("XTEL", teleport));
        }
        var body = Join(Door(0x90c, 0x90d), Door(0x90d, 0x90c), Ref("ACHR", 0x900, 0x700, 0, name: "SeatActor", linked: 0x90a), Ref("ACHR", 0x90a, 0x701, 0, name: "OtherActor"),
            Ref("REFR", 0x901, 0x710, 1), Ref("REFR", 0x902, 0x710, 5), Ref("REFR", 0x903, 0x710, 3, 0x700),
            Ref("REFR", 0x904, 0x710, 4, 0x701), Ref("REFR", 0x905, 0x3b, 0), Ref("REFR", 0x907, 0x710, 6),
            Ref("REFR", 0x908, 0x711, 1), Ref("REFR", 0x909, 0x710, 2, 0x730), Ref("REFR", 0x90b, 0x713, 1));
        var group = new byte[24 + body.Length]; Encoding.ASCII.GetBytes("GRUP").CopyTo(group, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(4), (uint)group.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(8), 0x800); BinaryPrimitives.WriteInt32LittleEndian(group.AsSpan(12), 6);
        body.CopyTo(group, 24); return group;
    }

    private static FalloutNifFile FurnitureMarkers(int count)
    {
        using var bytes = new MemoryStream();
        using var writer = new BinaryWriter(bytes, Encoding.UTF8, leaveOpen: true);
        void Sized(string value) { var text = Encoding.UTF8.GetBytes(value); writer.Write(text.Length); writer.Write(text); }
        writer.Write(Encoding.ASCII.GetBytes("Gamebryo File Format, Version 20.2.0.7\n"));
        writer.Write(FalloutNifFile.Version); writer.Write((byte)1); writer.Write(FalloutNifFile.UserVersion);
        writer.Write(1); writer.Write(34u); writer.Write(new byte[] { 1, 0, 1, 0, 1, 0 });
        writer.Write((ushort)1); Sized("BSFurnitureMarker"); writer.Write((ushort)0); writer.Write(8 + count * 16);
        writer.Write(1); writer.Write(3); Sized("FRN"); writer.Write(0);
        writer.Write(0); writer.Write(count);
        for (var index = 0; index < count; ++index)
        {
            writer.Write((float)index); writer.Write(2f); writer.Write(3f);
            writer.Write((ushort)0); writer.Write((byte)14); writer.Write((byte)14);
        }
        writer.Write(1); writer.Write(0); writer.Flush();
        return FalloutNifFile.Read(bytes.ToArray());
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
