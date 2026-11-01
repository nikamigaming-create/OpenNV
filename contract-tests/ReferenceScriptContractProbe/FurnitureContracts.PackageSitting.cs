using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

internal static partial class FurnitureContracts
{
    private static void PackageSittingContracts()
    {
        SandboxEmptyRescanContracts();
        foreach (var activity in Enum.GetValues<FalloutSandboxActivity>())
        {
            Require(FalloutSandboxProcedure.Read(0, activity) == 45 && FalloutSandboxProcedure.Read(1, activity) == 21 &&
                FalloutSandboxProcedure.Read(2, activity) == 0,
                "Sandbox selection/readiness phases were inferred from their activity or movement.");
            var expected = activity switch
            {
                FalloutSandboxActivity.Furniture => 48, FalloutSandboxActivity.Sleep => 8,
                FalloutSandboxActivity.Eat => 10, FalloutSandboxActivity.Wander => 7,
                FalloutSandboxActivity.IdleMarker => 46, FalloutSandboxActivity.Dialogue => 4,
                _ => throw new InvalidDataException("Unexpected source activity.")
            };
            Require(FalloutSandboxProcedure.Read(3, activity) == expected, "Sandbox activity phase lost its public procedure.");
            var target = activity is FalloutSandboxActivity.Furniture or FalloutSandboxActivity.Sleep or
                FalloutSandboxActivity.IdleMarker or FalloutSandboxActivity.Dialogue ? new FalloutFormKey("Fixture.esm", 1) : (FalloutFormKey?)null;
            var progress = new OpenNV.Runtime.Gameplay.State.FalloutSandboxActivityProgress(activity, 1, 1,
                new FalloutSandboxPreferences().Capture(), target, NativePhase: 3);
            progress.Validate();
            var cold = JsonSerializer.Deserialize<OpenNV.Runtime.Gameplay.State.FalloutSandboxActivityProgress>(JsonSerializer.Serialize(progress))!;
            cold.Validate();
            Require(cold.NativePhase == 3 && FalloutSandboxProcedure.Read(cold.NativePhase.Value, cold.Activity) == expected,
                "Cold Sandbox restoration lost its independently retained phase.");
        }
        Reject(() => FalloutSandboxProcedure.Read(3, null));
        Reject(() => FalloutSandboxProcedure.Read(-1, FalloutSandboxActivity.Wander));
        Reject(() => FalloutSandboxProcedure.Read(4, FalloutSandboxActivity.Wander));
        Require(RuntimeNativeNpc.SandboxFurnitureProcedure(1, sleeping: false, activityReselectionExit: false) == 48 &&
            RuntimeNativeNpc.SandboxFurnitureProcedure(1, sleeping: true, activityReselectionExit: false) == 8,
            "Occupied Sandbox furniture confused its activity procedure with the package or sitting code.");
        Require(RuntimeNativeNpc.SandboxFurnitureProcedure(4, sleeping: false, activityReselectionExit: true) == 21 &&
            RuntimeNativeNpc.SandboxFurnitureProcedure(4, sleeping: true, activityReselectionExit: true) == 21,
            "Sandbox activity reselection retained its occupied procedure after requesting Get Up.");
        foreach (var transition in new[] { 0, 2, 4 })
        {
            Reject(() => RuntimeNativeNpc.SandboxFurnitureProcedure(transition, sleeping: false, activityReselectionExit: false));
            Reject(() => RuntimeNativeNpc.SandboxFurnitureProcedure(transition, sleeping: true, activityReselectionExit: false));
        }
        foreach (var invalidExit in new[] { 0, 1, 2 })
            Reject(() => RuntimeNativeNpc.SandboxFurnitureProcedure(invalidExit, sleeping: false, activityReselectionExit: true));
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
                Record("FURN", 0x710, Field("MNAM", BitConverter.GetBytes(0x40000001u))), Record("IDLE", 0x712), Record("IDLE", 0x713),
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
            LastIdleRequestContracts(records, explicitCondition, caller, subject, creature);
            Console.WriteLine("OPENNV_PACKAGE_SITTING_CONTRACT_PASS self=true explicit=true target=true foreignMaster=true crossedPhases=true " +
                "npcAndCreatureUnloadedSelection=true actualPlayerCallback=true invalidSourceAndPhysicalOwnerRefused=true cold=true nativeMotion=unverified");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static void LastIdleRequestContracts(FalloutPluginStack records, FalloutCondition declaration,
        FalloutFormKey caller, FalloutFormKey subject, FalloutFormKey creature)
    {
        var first = new FalloutFormKey("SittingActors.esm", 0x712);
        var second = new FalloutFormKey("SittingActors.esm", 0x713);
        var condition = declaration with { Function = 451, Argument1 = 0x01000712, Argument2 = 0, RunOn = 0 };
        using var world = new FalloutReferenceWorld(records);
        var actor = world.Get(caller);
        Reject(() => world.IsLastIdlePlayed(condition, caller));
        actor.InitializeIdleRequestHistory(newProcess: false);
        Require(actor.LastIdleRequest == FalloutActorLastIdleRequest.NeverCreated,
            "An unqualified process manufactured initialized last-idle history.");
        actor.InitializeIdleRequestHistory(newProcess: true);
        Reject(() => world.IsLastIdlePlayed(condition, caller));
        void Bind(FalloutReferenceWorld owner, FalloutFormKey reference)
        {
            var state = owner.Get(reference);
            state.QueryLastIdleRequest = () => state.LastIdleRequest;
        }
        Bind(world, caller);
        Require(world.IsLastIdlePlayed(condition, caller) == 0 && actor.LastIdleRequest == FalloutActorLastIdleRequest.Empty,
            "A genuinely new process did not expose its empty last-request field.");
        actor.RecordIdleRequest(records, first);
        actor.SandboxIdleSelection = new(0, first);
        Require(world.IsLastIdlePlayed(condition, caller) == 1, "Published request was absent from last-idle history.");
        // Request history is independent of the older selected marker IDLE
        // and the absence of an active sequence.
        actor.RecordIdleRequest(records, second);
        var request = actor.LastIdleRequest;
        Reject(() => actor.RecordIdleRequest(records, new("SittingActors.esm", 0x710)));
        Require(actor.LastIdleRequest == request && world.IsLastIdlePlayed(condition, caller) == 0 &&
            world.IsLastIdlePlayed(condition with { Argument1 = 0x01000713 }, caller) == 1 &&
            actor.SandboxIdleSelection?.SelectedIdle == first,
            "Last-idle history followed marker selection or a rejected non-IDLE request.");
        actor.InitializeIdleRequestHistory(newProcess: true);
        Require(actor.LastIdleRequest == request, "Process publication reset an already accepted request.");
        world.Get(subject).InitializeIdleRequestHistory(newProcess: true);
        world.Get(creature).RecordIdleRequest(records, first);
        Bind(world, subject); Bind(world, creature);
        Require(world.IsLastIdlePlayed(condition with { RunOn = 2, Reference = 0x0100090b }, caller) == 1 &&
            world.IsLastIdlePlayed(condition with { RunOn = 2, Reference = 0x0100090a }, caller) == 0 &&
            world.IsLastIdlePlayed(condition with { RunOn = 1 }, caller, creature) == 1,
            "Last-idle query borrowed caller history or the runtime master ordinal for its explicit subject.");
        var before = JsonSerializer.Serialize(world.Capture());
        using var cold = new FalloutReferenceWorld(records);
        cold.Restore(JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(before)!);
        Reject(() => cold.IsLastIdlePlayed(condition, caller));
        Bind(cold, caller); Bind(cold, subject); Bind(cold, creature);
        Require(cold.Get(caller).LastIdleRequest == request &&
            cold.IsLastIdlePlayed(condition with { Argument1 = 0x01000713 }, caller) == 1 &&
            cold.IsLastIdlePlayed(condition, creature) == 1 &&
            cold.IsLastIdlePlayed(condition with { RunOn = 2, Reference = 0x0100090a }, caller) == 0 &&
            JsonSerializer.Serialize(cold.Capture()) == before,
            "Cold restoration replayed or replaced the independently persisted last request.");
        cold.Get(caller).QueryLastIdleRequest = null;
        Reject(() => cold.IsLastIdlePlayed(condition, caller));
        Require(cold.Get(caller).LastIdleRequest == request, "Native binding retirement erased persistent request history.");
        Bind(cold, caller);
        foreach (var invalid in new[] { condition with { Argument2 = 1 }, condition with { Argument1 = 0x01000710 },
            condition with { Function = 159 }, condition with { RunOn = 3 },
            condition with { RunOn = 2, Reference = 0 }, condition with { RunOn = 2, Reference = 0x01000901 } })
            Reject(() => cold.IsLastIdlePlayed(invalid, caller));
        Reject(() => cold.IsLastIdlePlayed(condition, records.RuntimeFormKey(0x14)));
        var snapshots = world.Capture();
        using var invalidHistory = new FalloutReferenceWorld(records);
        try
        {
            invalidHistory.Restore(snapshots.Select(value => value.Reference == caller
                ? value with { LastIdleRequest = request! with { Sha256 = new string('0', 64) } } : value).ToArray());
            throw new InvalidDataException("Changed last-idle source hash was admitted.");
        }
        catch (InvalidDataException error) when (error.Message == "Idle request history differs from its winning IDLE source.") { }
        Require(invalidHistory.Capture().Count == 0, "Invalid history source partially changed the cold world.");
        using var unknown = new FalloutReferenceWorld(records);
        unknown.Restore(snapshots.Select(value => value with { LastIdleRequest = null }).ToArray());
        unknown.Get(caller).InitializeIdleRequestHistory(newProcess: true);
        Bind(unknown, caller);
        Require(unknown.Get(caller).LastIdleRequest is null, "An absent saved process field became known-empty history.");
        Reject(() => unknown.IsLastIdlePlayed(condition, caller));
        unknown.Get(caller).RecordIdleRequest(records, first);
        Require(unknown.IsLastIdlePlayed(condition, caller) == 1, "A real new request failed to establish previously unknown history.");
        using var fresh = new FalloutReferenceWorld(records);
        _ = fresh.Get(caller);
        using var freshCold = new FalloutReferenceWorld(records);
        freshCold.Restore(JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(fresh.Capture()))!);
        Require(freshCold.Get(caller).MayInitializeIdleRequestHistory(hasNativeOwner: false),
            "A current saved actor without a prior native process could not create its first real process.");
        freshCold.Get(caller).InitializeIdleRequestHistory(newProcess: true);
        Bind(freshCold, caller);
        Require(freshCold.IsLastIdlePlayed(condition, caller) == 0,
            "A genuine first native process inherited an invented request.");
        Require(RuntimeNativeNpc.ProcedureIdleAlreadyOwned(first, (first, 2), null, []) &&
            RuntimeNativeNpc.ProcedureIdleAlreadyOwned(first, null, (first, 3), []) &&
            RuntimeNativeNpc.ProcedureIdleAlreadyOwned(first, null, null, [first]) &&
            !RuntimeNativeNpc.ProcedureIdleAlreadyOwned(first, (first, 1), (first, 1), []) &&
            !RuntimeNativeNpc.ProcedureIdleAlreadyOwned(first, (second, 2), null, []),
            "Procedure admission confused pending requests with owned current or outgoing requests.");
        foreach (var invalid in new FalloutActorLastIdleRequest[]
        {
            new(null, new string('0', 64)), new(first, null), new(first, "bad"), new(new FalloutFormKey("", 1), new string('0', 64))
        }) Reject(() => invalid.Validate());
        Console.WriteLine("OPENNV_LAST_IDLE_REQUEST_CONTRACT_PASS newEmpty=true unknownRefused=true distinctSelection=true " +
            "latestRequestRetained=true sourceHash=true declaringNamespace=true actorSubjects=true coldExact=true " +
            "processVisibilityRequired=true retirementRetains=true noHistoryBackfill=true");
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
