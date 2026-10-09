using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static class ActorScriptPackageContracts
{
    internal static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-actor-script-package-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllBytes(Path.Combine(directory, "Actors.esm"), Join(Header(),
                Record("NPC_", 0x700, Field("ACBS", new byte[24]), Field("PKID", BitConverter.GetBytes(0x300u))),
                Record("CREA", 0x701, Field("ACBS", new byte[24]), Field("PKID", BitConverter.GetBytes(0x300u))),
                Record("STAT", 0x710), Record("CELL", 0x800, Field("DATA", [1])),
                Group(Ref("ACHR", 0x900, 0x700), Ref("ACRE", 0x901, 0x701),
                    Ref("ACHR", 0x902, 0x700), Ref("REFR", 0x903, 0x710)), Package(0x300, 0)));
            File.WriteAllBytes(Path.Combine(directory, "Decoy.esm"), Join(Header(),
                Record("NPC_", 0x700, Field("ACBS", new byte[24])), Record("CELL", 0x800, Field("DATA", [1])),
                Group(Ref("ACHR", 0x900, 0x700)), Package(0x300, 0)));
            // Source master ordinal 1 is Actors; runtime ordinal 1 is Decoy.
            File.WriteAllBytes(Path.Combine(directory, "Packages.esp"), Join(Header("Decoy.esm", "Actors.esm"),
                Package(0x01000300, 4), Package(0x02000301, 2), Package(0x02000302, 0, Condition(65535)),
                Record("PACK", 0x02000303, Field("PKDT", [1])), Deleted(Package(0x02000304, 0)),
                Record("STAT", 0x02000305)));
            using var records = FalloutPluginStack.Load(directory, ["Actors.esm", "Decoy.esm", "Packages.esp"]);
            Require(FalloutScriptPackage.Read(records.GetEffective(ActorKey(0x300))).Flags == 4 &&
                FalloutScriptPackage.Read(records.GetEffective(new("Decoy.esm", 0x300))).Flags == 0,
                "Winning master identity or independent source flag was replaced by runtime ordinal.");
            foreach (var actor in new[] { ActorKey(0x900), ActorKey(0x901) }) CheckActor(records, actor);
            CheckSourceRefusals(records);
            CheckSourceDrift(directory, records);
            Console.WriteLine("OPENNV_ACTOR_SCRIPT_PACKAGE_CONTRACT_PASS placedNpc=true placedCreature=true winningMaster=true priority=true samePackEpoch=true staleCompletionRefused=true coldPending=true coldChoice=true siblingUntouched=true mustReach=true mustComplete=true sourceDriftRefused=true invalidAtomic=true electionFaultRetained=true nativeTransition=unexecuted retailCadence=unmatched");
        }
        finally
        {
            foreach (var file in Directory.EnumerateFiles(directory)) File.Delete(file);
            Directory.Delete(directory);
        }
    }

    private static void CheckActor(FalloutPluginStack records, FalloutFormKey actor)
    {
        using var world = new FalloutReferenceWorld(records);
        var quests = new FalloutQuestState(records);
        world.UnloadedPackages = new(records, world, quests, null, null,
            (_, _) => throw new InvalidDataException("Fixture has no package result program."), () => 1);
        var packages = world.UnloadedPackages;
        var state = world.Get(actor);
        var first = PackageKey(0x301);
        Require(world.ApplyActorScriptPackage(actor, first), "Script override was ignored.");
        Require(world.SelectActorPackage(actor, _ => throw new InvalidDataException("Command evaluated default predicates."),
            null, null, null, false)?.FormKey == first, "Override did not precede the base PKID list.");
        packages.EvaluatePackages(actor, false);
        var revision = state.ScriptPackage!.Revision;
        Require(state.PackageAssignment is { Done: false } assignment && assignment.Package == first &&
            assignment.ScriptPackageRevision == revision && !state.ScriptPackage.Pending,
            "Source Start lost its actual assignment epoch or invented completion.");
        Consume(world, actor);
        var firstHistory = state.DeferredPackageContinuation!.Revision;
        world.ApplyActorScriptPackage(actor, first);
        packages.EvaluatePackages(actor, false);
        Require(state.ScriptPackage!.Revision > revision && state.PackageAssignment!.ScriptPackageRevision == state.ScriptPackage.Revision &&
            state.DeferredPackageContinuation!.Revision == firstHistory + 2 &&
            world.PackageEvents.Capture(actor).Select(mark => mark.Kind).SequenceEqual(
                [FalloutReferencePackageEventKind.Change, FalloutReferencePackageEventKind.Start]),
            "A fresh same-PACK command reused the old Start/Done rather than Change then Start.");
        Require(!world.RetireActorScriptPackage(actor, first, revision), "An old completion retired a new same-PACK epoch.");
        Consume(world, actor);
        Require(world.Get(ActorKey(0x902)).ScriptPackage is null &&
            FalloutAiPackages.OnPriorityList(records, state.Base, ActorKey(0x300), state.Templates),
            "Per-reference override altered a sibling or removed the source priority list.");

        var choice = world.QueueActorPackageChoice(actor, records.GetEffective(first));
        Reject(() => (choice with { Package = null, Sha256 = null }).Bind(records, state));
        Reject(() => (choice with { ScriptPackageRevision = choice.ScriptPackageRevision + 1 }).Bind(records, state));
        var saved = RoundTrip(world.Capture());
        using (var coldChoice = new FalloutReferenceWorld(records))
        {
            coldChoice.Restore(saved);
            Require(coldChoice.Get(actor).PendingPackageChoice == choice && coldChoice.CurrentPackage(actor) == first,
                "Cold pending election lost its consumed source choice.");
            coldChoice.UnloadedPackages = new(records, coldChoice, quests, null, null,
                (_, _) => throw new InvalidDataException("Cold choice replayed a source result."), () => 1);
            coldChoice.UnloadedPackages.Advance(0);
            Require(coldChoice.Get(actor).PendingPackageChoice is null && coldChoice.PendingPackageEventCount == 0,
                "Unloaded polling retained a consumed election or replayed its Start.");
        }
        packages.Advance(0);
        Require(state.PendingPackageChoice is null, "An unloaded actor never applied its queued election.");
        var reached = world.SelectActorPackage(actor, _ => 0, state.Templates, null, first, false, locationReached: false);
        Require(reached?.FormKey == first, "Must-reach override ended before an actual location receipt.");
        Require(world.SelectActorPackage(actor, _ => 0, state.Templates, null, first, false, locationReached: true)?.FormKey == ActorKey(0x300) &&
            state.ScriptPackage!.Package is null, "Reached override did not release to the source list.");
        world.ApplyActorScriptPackage(actor, ActorKey(0x300));
        packages.EvaluatePackages(actor, false);
        Require(world.SelectActorPackage(actor, _ => 0, state.Templates, null, ActorKey(0x300), false, locationReached: true)?.FormKey == ActorKey(0x300) &&
            state.ScriptPackage!.Package == ActorKey(0x300), "Must-complete override ended at location arrival.");
        // This checks the shared election predicate. It is no native arrival proof.
        _ = world.SelectActorPackage(actor, _ => 0, state.Templates, null, ActorKey(0x300), true, locationReached: true);
        Require(state.ScriptPackage!.Package is null, "Completed override was retained indefinitely.");

        world.ApplyActorScriptPackage(actor, PackageKey(0x302));
        Require(world.SelectActorPackage(actor, _ => throw new InvalidDataException("Override tested its CTDA."), state.Templates,
            null, ActorKey(0x300), false)?.FormKey == PackageKey(0x302), "Script command redrew override eligibility.");
        Consume(world, actor);
        saved = RoundTrip(world.Capture());
        using (var coldPending = new FalloutReferenceWorld(records))
        {
            coldPending.Restore(saved);
            Require(coldPending.Get(actor).ScriptPackage is { Pending: true } pending && pending.Package == PackageKey(0x302) &&
                coldPending.Get(actor).PackageAssignment!.ScriptPackageRevision < pending.Revision,
                "Cold pending override became an applied native procedure.");
        }
        var retained = state.ScriptPackage!;
        foreach (var invalid in new[]
        {
            retained with { Actor = new("Decoy.esm", actor.ObjectId) }, retained with { Revision = 0 },
            retained with { Pending = false }, retained with { Sha256 = new string('0', 64) }
        })
        {
            using var rejected = new FalloutReferenceWorld(records);
            Reject(() => rejected.Restore(saved.Select(row => row.Reference == actor ? row with { ScriptPackage = invalid } : row).ToArray()));
            Require(rejected.InstanceCount == 0, "Rejected script package partially restored references.");
        }
        world.ApplyActorScriptPackage(actor, null);
        Require(!world.ApplyActorScriptPackage(actor, null), "Repeated removal allocated another assignment.");
        Reject(() => world.BeginActorScriptPackage(actor, first, revision));
        state.ScriptError = "Package selection fixture: unsupported source condition";
        using var failedCold = new FalloutReferenceWorld(records);
        failedCold.Restore(RoundTrip(world.Capture()));
        Require(failedCold.Get(actor).ScriptError == state.ScriptError,
            "Cold restoration cleared an actual source election failure on an actor without a script.");
    }

    private static void CheckSourceRefusals(FalloutPluginStack records)
    {
        using var world = new FalloutReferenceWorld(records);
        var actor = ActorKey(0x900);
        world.ApplyActorScriptPackage(actor, PackageKey(0x301));
        var retained = world.Get(actor).ScriptPackage;
        foreach (var invalid in new[] { PackageKey(0x303), PackageKey(0x304), PackageKey(0x305), new FalloutFormKey("Absent.esm", 0x301) })
        {
            Reject(() => world.ApplyActorScriptPackage(actor, invalid));
            Require(world.Get(actor).ScriptPackage == retained, "Rejected source replaced an owned override.");
        }
        Reject(() => world.ApplyActorScriptPackage(ActorKey(0x903), PackageKey(0x301)));
        world.Get(actor).DeletePending = true;
        Reject(() => world.ApplyActorScriptPackage(actor, PackageKey(0x302)));
        Require(world.Get(actor).ScriptPackage == retained, "Retiring actor accepted another override.");
        using var nativePending = new FalloutReferenceWorld(records);
        var quests = new FalloutQuestState(records);
        nativePending.UnloadedPackages = new(records, nativePending, quests, null, null,
            (_, _) => throw new InvalidDataException("Fixture has no result program."), () => 1);
        nativePending.UnloadedPackages.EvaluatePackages(actor, false);
        Consume(nativePending, actor);
        var actual = nativePending.Get(actor).PackageAssignment;
        // An authored animation clock represents an existing presentation owner;
        // this source-only fixture neither creates a native actor nor reports movement.
        nativePending.Get(actor).Animation.Change("meshes/authored-fixture/idle.kf", new string('a', 64));
        nativePending.ApplyActorScriptPackage(actor, PackageKey(0x301));
        nativePending.UnloadedPackages.EvaluatePackages(actor, false);
        Require(nativePending.Get(actor).PendingPackageChoice?.Package == PackageKey(0x301) &&
            nativePending.Get(actor).PackageAssignment == actual && nativePending.PendingPackageEventCount == 0 &&
            nativePending.Get(actor).ScriptPackage?.Pending == true,
            "Unloaded election overwrote a retained physical owner or published an unobserved transition.");
    }

    private static void CheckSourceDrift(string directory, FalloutPluginStack records)
    {
        using var original = new FalloutReferenceWorld(records);
        original.ApplyActorScriptPackage(ActorKey(0x900), PackageKey(0x301));
        var saved = RoundTrip(original.Capture());
        File.WriteAllBytes(Path.Combine(directory, "Drift.esp"), Join(Header("Decoy.esm", "Actors.esm", "Packages.esp"),
            Package(0x02000301, 4)));
        using var changed = FalloutPluginStack.Load(directory, ["Actors.esm", "Decoy.esm", "Packages.esp", "Drift.esp"]);
        using var cold = new FalloutReferenceWorld(changed);
        Reject(() => cold.Restore(saved));
        Require(cold.InstanceCount == 0, "Winning source drift partially bound the cold actor.");
    }

    private static FalloutReferenceSnapshot[] RoundTrip(IReadOnlyList<FalloutReferenceSnapshot> source) =>
        JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(source))!;
    private static void Consume(FalloutReferenceWorld world, FalloutFormKey actor) => world.PackageEvents.Consume(world.PackageEvents.SnapshotPending(actor));
    private static FalloutFormKey ActorKey(uint id) => new("Actors.esm", id);
    private static FalloutFormKey PackageKey(uint id) => new("Packages.esp", id);
    private static byte[] Ref(string signature, uint id, uint source) => Record(signature, id,
        Field("NAME", BitConverter.GetBytes(source)), Field("DATA", new byte[24]));
    private static byte[] Package(uint id, uint flags, byte[]? condition = null)
    {
        var data = new byte[12]; BinaryPrimitives.WriteUInt32LittleEndian(data, flags); data[4] = 6;
        return Record("PACK", id, Field("EDID", Text("AuthoredPackage" + id)), Field("PKDT", data),
            Field("PSDT", [255, 255, 0, 255, 0, 0, 0, 0]), condition ?? []);
    }
    private static byte[] Condition(ushort function)
    {
        var data = new byte[28]; BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(8), function);
        return Field("CTDA", data);
    }
    private static byte[] Header(params string[] masters) => Record("TES4", 0, Field("HEDR", new byte[12]),
        Join(masters.Select(master => Join(Field("MAST", Text(master)), Field("DATA", new byte[8]))).ToArray()));
    private static byte[] Group(params byte[][] references)
    {
        var body = Join(references); var result = new byte[24 + body.Length]; Encoding.ASCII.GetBytes("GRUP").CopyTo(result, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), (uint)result.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(8), 0x800); BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(12), 6);
        body.CopyTo(result, 24); return result;
    }
    private static byte[] Deleted(byte[] bytes) { BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), 0x20); return bytes; }
    private static byte[] Record(string signature, uint id, params byte[][] fields)
    {
        var data = Join(fields); var result = new byte[24 + data.Length]; Encoding.ASCII.GetBytes(signature).CopyTo(result, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), (uint)data.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(12), id); data.CopyTo(result, 24); return result;
    }
    private static byte[] Field(string signature, byte[] data)
    {
        var result = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(signature).CopyTo(result, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(4), checked((ushort)data.Length)); data.CopyTo(result, 6); return result;
    }
    private static byte[] Text(string value) => Encoding.ASCII.GetBytes(value + '\0');
    private static byte[] Join(params byte[][] values) => values.SelectMany(value => value).ToArray();
    private static void Require(bool value, string error) { if (!value) throw new InvalidDataException(error); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException or InvalidOperationException or KeyNotFoundException) { return; }
        throw new InvalidDataException("Foreign, stale, deleted or malformed actor package was accepted.");
    }
}
