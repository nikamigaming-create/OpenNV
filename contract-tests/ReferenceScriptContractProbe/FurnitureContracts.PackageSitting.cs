using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static partial class FurnitureContracts
{
    private static void PackageSittingContracts()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-package-sitting-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            byte[] Ref(string signature, uint id, uint source) => Record(signature, id,
                Field("NAME", BitConverter.GetBytes(source)), Field("DATA", new byte[24]));
            byte[] Group(params byte[][] references)
            {
                var body = Join(references); var group = new byte[24 + body.Length];
                Encoding.ASCII.GetBytes("GRUP").CopyTo(group, 0);
                BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(4), (uint)group.Length);
                BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(8), 0x800);
                BinaryPrimitives.WriteInt32LittleEndian(group.AsSpan(12), 6); body.CopyTo(group, 24); return group;
            }
            byte[] HeaderWithMasters(params string[] masters) => Record("TES4", 0, Field("HEDR", new byte[12]),
                Join(masters.Select(master => Join(Field("MAST", Text(master)), Field("DATA", new byte[8]))).ToArray()));
            File.WriteAllBytes(Path.Combine(directory, "SittingActors.esm"), Join(HeaderWithMasters(),
                Record("NPC_", 0x700, Field("ACBS", new byte[24])), Record("NPC_", 0x701, Field("ACBS", new byte[24])),
                Record("CREA", 0x702, Field("ACBS", new byte[24])), Record("NPC_", 0x703, Field("ACBS", new byte[24])),
                Record("FURN", 0x710, Field("MNAM", BitConverter.GetBytes(0x40000001u))), Record("IDLE", 0x712),
                Record("CELL", 0x800, Field("DATA", [1])), Group(Ref("ACHR", 0x900, 0x700), Ref("ACHR", 0x90a, 0x701),
                    Ref("ACRE", 0x90b, 0x702), Ref("ACHR", 0x90c, 0x703), Ref("REFR", 0x901, 0x710))));
            File.WriteAllBytes(Path.Combine(directory, "SittingDecoy.esm"), Join(HeaderWithMasters(),
                Record("NPC_", 0x701, Field("ACBS", new byte[24])), Record("CELL", 0x800, Field("DATA", [1])),
                Group(Ref("ACHR", 0x90a, 0x701))));
            byte[] Condition(uint runOn, uint reference)
            {
                var data = new byte[28]; BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(4), 3);
                BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(8), 159);
                BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(20), runOn);
                BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(24), reference); return Field("CTDA", data);
            }
            byte[] Package(uint id, byte[]? condition = null, uint? target = null)
            {
                var data = new byte[12]; BinaryPrimitives.WriteUInt32LittleEndian(data, 0x1000); data[4] = 6;
                return Record("PACK", id, Field("EDID", Text("SyntheticSitting" + id)), Field("PKDT", data),
                    Field("PSDT", [255, 255, 0, 255, 0, 0, 0, 0]),
                    Field("PLDT", Join(BitConverter.GetBytes(0), BitConverter.GetBytes(0x01000901u), new byte[4])),
                    target is { } key ? Field("PTDT", Join(BitConverter.GetBytes(0), BitConverter.GetBytes(key), new byte[8])) : [],
                    condition ?? []);
            }
            byte[] PackagesForActor(string signature, uint actor, params uint[] packages) => Record(signature, actor,
                Field("ACBS", new byte[24]), Join(packages.Select(package => Field("PKID", BitConverter.GetBytes(package))).ToArray()));
            // Declaring master ordinal 1 identifies SittingActors; runtime
            // ordinal 1 identifies the deliberately conflicting decoy instead.
            File.WriteAllBytes(Path.Combine(directory, "SittingQueries.esp"), Join(HeaderWithMasters("SittingDecoy.esm", "SittingActors.esm"),
                PackagesForActor("NPC_", 0x01000700, 0x02000400, 0x02000403),
                PackagesForActor("CREA", 0x01000702, 0x02000400, 0x02000403),
                PackagesForActor("NPC_", 0x01000703, 0x02000412, 0x02000403),
                Package(0x02000400, Condition(2, 0x0100090a)), Package(0x02000401, Condition(1, 0), 0x0100090a),
                Package(0x02000402, Condition(1, 0)), Package(0x02000403), Package(0x02000410),
                Package(0x02000412, Condition(2, 0x01000014))));
            using var records = FalloutPluginStack.Load(directory, ["SittingActors.esm", "SittingDecoy.esm", "SittingQueries.esp"]);
            FalloutFormKey Actor(uint id) => new("SittingActors.esm", id);
            FalloutFormKey PackageKey(uint id) => new("SittingQueries.esp", id);
            var caller = Actor(0x900); var subject = Actor(0x90a); var creature = Actor(0x90b);
            var playerCaller = Actor(0x90c); var player = records.RuntimeFormKey(0x14); var chair = Actor(0x901);
            var explicitCondition = FalloutCondition.Read(records.GetEffective(PackageKey(0x400))).Single();
            var targetCondition = FalloutCondition.Read(records.GetEffective(PackageKey(0x401))).Single();
            using var world = new FalloutReferenceWorld(records);
            var callerPhase = 0; var subjectPhase = 3; var playerPhase = 2; var queries = 0;
            world.Get(caller).QuerySitting = () => callerPhase;
            world.Get(subject).QuerySitting = () => subjectPhase;
            world.Get(creature).QuerySitting = () => 0;
            world.Get(playerCaller).QuerySitting = () => 0;
            world.Get(new("SittingDecoy.esm", 0x90a)).QuerySitting = () => 4;
            int Query(FalloutFormKey reference) { queries++; return reference == player ? playerPhase : world.GetSitting(reference); }
            float Evaluate(FalloutCondition condition, FalloutFormKey actor) => FalloutAiPackages.Sitting(records, condition, actor, Query);
            Require(Evaluate(explicitCondition, caller) == 3 && Evaluate(explicitCondition with { RunOn = 0 }, caller) == 0 &&
                Evaluate(targetCondition, caller) == 3 && Evaluate(explicitCondition, creature) == 3,
                "Package GetSitting borrowed the caller, decoy master or another subject's physical phase.");
            for (subjectPhase = 0; subjectPhase <= 4; subjectPhase++)
                Require(Evaluate(explicitCondition, caller) == subjectPhase, "Package GetSitting changed a selected subject's physical phase.");
            subjectPhase = 3;
            var playerCondition = FalloutCondition.Read(records.GetEffective(PackageKey(0x412))).Single();
            Require(Evaluate(playerCondition, caller) == 2 && Evaluate(explicitCondition with { RunOn = 0 }, player) == 2,
                "Package GetSitting lost the reserved player's supplied physical owner.");
            foreach (var invalid in new[] { explicitCondition with { Function = 47 }, explicitCondition with { Argument1 = 1 },
                explicitCondition with { Argument2 = 1 }, explicitCondition with { RunOn = 3 }, explicitCondition with { Reference = 0 },
                explicitCondition with { Reference = 0x01000901 }, explicitCondition with { Reference = 0x01000fff } })
            {
                queries = 0; Reject(() => Evaluate(invalid, caller));
                Require(queries == 0, "Malformed sitting declaration reached a physical owner before source validation.");
            }
            Reject(() => Evaluate(FalloutCondition.Read(records.GetEffective(PackageKey(0x402))).Single(), caller));
            Reject(() => FalloutAiPackages.Sitting(records, explicitCondition, caller, null));
            foreach (var invalidPhase in new[] { -1, 5 })
            { subjectPhase = invalidPhase; Reject(() => Evaluate(explicitCondition, caller)); }
            subjectPhase = 3;
            world.Get(subject).QuerySitting = null; Reject(() => Evaluate(explicitCondition, caller));
            world.Get(subject).QuerySitting = () => subjectPhase;
            var quests = new FalloutQuestState(records);
            world.UnloadedPackages = new(records, world, quests, null, null, (program, _) => program.RequireEmptyScript(), () => 1, Query);
            Require(world.CurrentPackage(caller) == PackageKey(0x400) && world.CurrentPackage(creature) == PackageKey(0x400),
                "Unloaded NPC/creature selection did not use the explicit physical subject.");
            callerPhase = 3; subjectPhase = 0;
            Require(world.CurrentPackage(caller) == PackageKey(0x403) && world.CurrentPackage(creature) == PackageKey(0x403),
                "Unloaded selection substituted the sitting caller for its unsitting explicit subject.");
            playerPhase = 3;
            Require(world.CurrentPackage(playerCaller) == PackageKey(0x412), "Unloaded selection ignored the actual player owner callback.");
            playerPhase = 0;
            Require(world.CurrentPackage(playerCaller) == PackageKey(0x403), "Unloaded selection retained a stale player phase.");
            using (var missing = new FalloutReferenceWorld(records))
            {
                missing.UnloadedPackages = new(records, missing, quests, null, null, (program, _) => program.RequireEmptyScript(), () => 1);
                Reject(() => missing.CurrentPackage(caller)); Reject(() => missing.CurrentPackage(creature));
                Reject(() => missing.CurrentPackage(playerCaller));
            }
            PackageSittingCold(records, explicitCondition, caller, subject, chair, PackageKey(0x410));
            Console.WriteLine("OPENNV_PACKAGE_SITTING_CONTRACT_PASS self=true explicit=true target=true foreignMaster=true crossedPhases=true " +
                "npcAndCreatureUnloadedSelection=true actualPlayerCallback=true invalidSourceAndPhysicalOwnerRefused=true cold=true nativeMotion=unverified");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static void PackageSittingCold(FalloutPluginStack records, FalloutCondition condition,
        FalloutFormKey caller, FalloutFormKey subject, FalloutFormKey chair, FalloutFormKey packageKey)
    {
        using var warm = new FalloutReferenceWorld(records);
        var package = records.GetEffective(packageKey); var furniture = records.GetEffective(warm.Get(chair).Base);
        var idle = records.GetEffective(new("SittingActors.esm", 0x712)); var resourceHash = new string('A', 64);
        const string resource = "meshes/first-party-sitting-fixture.kf";
        float[] pose = [1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0];
        var owner = warm.Get(subject); owner.Animation.Restore(new(resource, resourceHash, .375, false));
        var seat = new FalloutFurnitureSeat(furniture.FormKey, 0, 1, new(new(0, 0, 0), 0, 1, 1), [0, 0, 0], 0);
        foreach (var phase in new[] { 2, 3, 4 })
        {
            var assignment = new FalloutActorPackageAssignment(packageKey, FalloutActorFurnitureContinuation.RecordHash(package), phase != 2);
            owner.PackageAssignment = assignment;
            owner.FurnitureContinuation = new(assignment, 1, "POBA", packageKey, assignment.Sha256, phase, 0,
                pose, packageKey, null, 123, 0, null, null, chair, FalloutActorFurnitureContinuation.RecordHash(furniture),
                "meshes/first-party-sitting-fixture.nif", resourceHash, seat, pose,
                new(idle.FormKey, FalloutActorFurnitureContinuation.RecordHash(idle), resource, resourceHash));
            var snapshots = warm.Capture();
            using var cold = new FalloutReferenceWorld(records);
            cold.Restore(JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(snapshots))!);
            Require(FalloutAiPackages.Sitting(records, condition, caller, reference => cold.GetSitting(reference)) == phase &&
                cold.OwnsFurnitureSeat(chair, 0, subject) && JsonSerializer.Serialize(cold.Capture()) == JsonSerializer.Serialize(snapshots),
                "Cold sitting query borrowed the caller or changed the retained physical continuation.");
            var before = JsonSerializer.Serialize(cold.Capture()); cold.Get(subject).FurnitureContinuation = null;
            Reject(() => FalloutAiPackages.Sitting(records, condition, caller, reference => cold.GetSitting(reference)));
            cold.Get(subject).FurnitureContinuation = owner.FurnitureContinuation;
            Require(JsonSerializer.Serialize(cold.Capture()) == before, "Refused sitting query mutated retained world state.");
        }
    }
}
