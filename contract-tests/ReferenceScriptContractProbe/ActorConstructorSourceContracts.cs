using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class ActorConstructorSourceContracts
{
    private static readonly string[] Images =
    ["518c87f58a6c4d9826e9ef8fbb7f4213882fa70822675610d45aea2464502a57",
     "c3f97c2255fa041a851c17cf372d69aaadd8694e2dc4230ba556001bbfbd2f3e"];
    private const string Selection = "authored-current-constructor-selection";
    private static readonly FalloutFormKey Npc = Key(0x901), Creature = Key(0x902);

    internal static void Run()
    {
        ThreadDecisions();
        var directory = Path.Combine(Path.GetTempPath(), "opennv-actor-constructor-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllBytes(Path.Combine(directory, "Constructor.esm"), Plugin());
            foreach (var image in Images) SourceConstruction(directory, image);
        }
        finally { File.Delete(Path.Combine(directory, "Constructor.esm")); Directory.Delete(directory); }
        Console.WriteLine("OPENNV_ACTOR_CONSTRUCTOR_SOURCE_CONTRACT_PASS actualReader=true placedNpcCreatureFactories=true mainIniDirect=true threadShortCircuit=true selectedQueueThresholdsDistinct=true missingThreadOwnerHeld=true winnerMasterDriftRefused=true typedReceiptRequired=true coldNoThreadCallbackReplay=true repeatedColdPreservesOriginalConstruction=true nativeGameplay=unexecuted");
    }

    private static void SourceConstruction(string directory, string image)
    {
        var declaration = FalloutActorProcessDeclaration.ForExecutable(image);
        var group = FalloutCombatGroupDeclaration.ForExecutable(image);
        using var records = Stack(directory, image, 1, prefs: 9);
        var process = Guid.NewGuid(); var calls = 0;
        var owner = new FalloutActorProcessConstructionSource(records, declaration, Selection, process,
            actor => FalloutCombatActorSource.Read(records, group, actor), () =>
            { calls++; throw new InvalidDataException("Short-circuit constructor queried an unrelated worker owner."); });
        var npc = owner.Read(Npc); var creature = owner.Read(Creature);
        Require(npc is { RegistrationRequested.Value: true, AlternateRegistrationMode.Value: false, Source: not null } &&
            creature is { RegistrationRequested.Value: true, AlternateRegistrationMode.Value: false, Source: not null } && calls == 0 &&
            npc.Source.ReferenceSignature == "ACHR" && creature.Source.ReferenceSignature == "ACRE" &&
            npc.Source.HardwareThreads.CurrentValue == 1 && npc.Source.ConstructedProcess == process,
            "Winning source constructor changed the literal request, used Prefs, or inferred a thread state.");
        Reject(() => owner.Read(records.RuntimeFormKey(0x14)));
        Reject(() => owner.Read(Key(0x999)));
        var forged = new FalloutActorProcessConstructionSource(records, declaration, Selection, process,
            actor => FalloutCombatActorSource.Read(records, group, actor) with { BaseSha256 = new string('b', 64) });
        Reject(() => forged.Read(Npc));
        var foreign = new FalloutActorProcessConstructionSource(records, declaration, Selection, process,
            actor => FalloutCombatActorSource.Read(records, group, actor) with { Reference = new("Foreign.esm", actor.ObjectId) });
        Reject(() => foreign.Read(Npc));

        using var threaded = Stack(directory, image, 2);
        var missing = new FalloutActorProcessConstructionSource(threaded, declaration, Selection, process,
            actor => FalloutCombatActorSource.Read(threaded, group, actor));
        var held = missing.Read(Npc);
        Require(held.RegistrationRequested.Value == true && held.AlternateRegistrationMode.Value is null &&
            held.AlternateRegistrationMode.Failure is not null && held.Source is { AlternateMode: null, ThreadObservation: null },
            "A missing real worker owner was turned into normal registration.");
        var wrongEpoch = new FalloutActorProcessConstructionSource(threaded, declaration, Selection, process,
            actor => FalloutCombatActorSource.Read(threaded, group, actor), () => Observation(Guid.NewGuid()));
        Reject(() => wrongEpoch.Read(Npc));
        ConfigurationFailure(threaded, image);

        using var world = World(records, image);
        world.Get(Npc); world.Get(Creature);
        var saved = RoundTrip(world.CaptureActorProcesses());
        Require(saved.Cohort.Ends[3] == 2 && saved.Actors.Where(actor => !actor.Source.EnginePlayer).All(actor =>
            actor.Registered && actor.Level == FalloutDetectionProcessLevel.Low && actor.Construction?.Source is not null) &&
            saved.Actors.Single(actor => actor.Source.EnginePlayer) is { Registered: false, Level: FalloutDetectionProcessLevel.High },
            "The actual world constructor dropped its cohort or put Player into it.");
        owner.ValidateRetained(saved);
        Reject(() => owner.ValidateRetained(saved with { Actors = saved.Actors.Select(actor => actor.Source.Reference == Npc ?
            actor with { Construction = actor.Construction! with { Source = null } } : actor).ToArray() }));
        Reject(() => owner.ValidateRetained(saved with { Actors = saved.Actors.Select(actor => actor.Source.Reference == Npc ?
            actor with { Construction = actor.Construction! with { Source = actor.Construction!.Source! with { ReferenceSha256 = new string('a', 64) } } } : actor).ToArray() }));
        using var changed = Stack(directory, image, 0);
        var changedSetting = new FalloutActorProcessConstructionSource(changed, declaration, Selection, process,
            actor => FalloutCombatActorSource.Read(changed, group, actor));
        Reject(() => changedSetting.ValidateRetained(saved));

        using var cold = new FalloutReferenceWorld(records);
        cold.Restore(world.Capture()); cold.ConfigureCombatGroups(group, Selection, world.CaptureCombatGroups());
        cold.ConfigureActorPerception(FalloutActorPerceptionDeclaration.ForExecutable(image), Selection, world.CaptureActorPerception());
        cold.ConfigureActualProcessRuntime(FalloutActorProcessRuntimeDeclaration.ForExecutable(image), Selection,
            world.CaptureActualProcessRuntime(), world.CaptureActualProcessCommon());
        cold.ConfigureActorProcesses(declaration, Selection, saved);
        var once = cold.CaptureActorProcesses();
        Require(once.CapturedProcess != saved.CapturedProcess && once.Actors.Single(actor => actor.Source.Reference == Npc).Construction ==
            saved.Actors.Single(actor => actor.Source.Reference == Npc).Construction,
            "Cold actor registration replayed or rewrote historical construction.");
        using var twice = new FalloutReferenceWorld(records);
        twice.Restore(cold.Capture()); twice.ConfigureCombatGroups(group, Selection, cold.CaptureCombatGroups());
        twice.ConfigureActorPerception(FalloutActorPerceptionDeclaration.ForExecutable(image), Selection, cold.CaptureActorPerception());
        twice.ConfigureActualProcessRuntime(FalloutActorProcessRuntimeDeclaration.ForExecutable(image), Selection,
            cold.CaptureActualProcessRuntime(), cold.CaptureActualProcessCommon());
        twice.ConfigureActorProcesses(declaration, Selection, once);
        Require(twice.CaptureActorProcesses().CapturedProcess != once.CapturedProcess &&
            twice.CaptureActorProcesses().Actors.Single(actor => actor.Source.Reference == Npc).Construction!.Source!.ConstructedProcess ==
            saved.Actors.Single(actor => actor.Source.Reference == Npc).Construction!.Source!.ConstructedProcess,
            "Second cold epoch discarded the real original constructor receipt.");
    }

    private static void ConfigurationFailure(FalloutPluginStack records, string image)
    {
        using var world = new FalloutReferenceWorld(records);
        world.ConfigureCombatGroups(FalloutCombatGroupDeclaration.ForExecutable(image), Selection);
        world.ConfigureActorPerception(FalloutActorPerceptionDeclaration.ForExecutable(image), Selection);
        world.Get(Npc); world.Get(Creature);
        world.ConfigureActualProcessRuntime(FalloutActorProcessRuntimeDeclaration.ForExecutable(image), Selection);
        var rejected = true;
        using var lease = world.BindActualActorRegistrationThreads(process =>
            rejected ? throw new InvalidDataException("Authored source producer failed before publication.") : Observation(process));
        var declaration = FalloutActorProcessDeclaration.ForExecutable(image);
        Reject(() => world.ConfigureActorProcesses(declaration, Selection));
        Require(!world.ActorProcessesConfigured, "A retired constructor candidate was published after setup failure.");
        rejected = false;
        world.ConfigureActorProcesses(declaration, Selection);
        Require(world.CaptureActorProcesses().Actors.Where(actor => !actor.Source.EnginePlayer).All(actor =>
            actor.Registered && actor.Construction?.Source?.ThreadObservation is not null),
            "Failed setup kept its old source provider or discarded the actual independently bound thread lease.");
    }

    private static void ThreadDecisions()
    {
        var fnv = FalloutActorProcessDeclaration.ForExecutable(Images[0]);
        var fo3 = FalloutActorProcessDeclaration.ForExecutable(Images[1]);
        var state = Observation(Guid.NewGuid());
        Require(!FalloutActorProcessConstructionSource.EvaluateThreadMode(fnv, state) &&
            !FalloutActorProcessConstructionSource.EvaluateThreadMode(fo3, state), "Complete source main-worker state changed normal mode.");
        var equality = state with { PrimaryWorkTotal = new(10, "authored-actual-primary-work-total") };
        Require(FalloutActorProcessConstructionSource.EvaluateThreadMode(fnv, equality) &&
            !FalloutActorProcessConstructionSource.EvaluateThreadMode(fo3, equality), "Distinct original main-queue bounds were collapsed.");
        var other = state with { CurrentThreadIsMain = new(false, "authored-actual-other-thread"), PrimaryWorkTotal = new(null, "unqueried-other-primary") };
        Require(!FalloutActorProcessConstructionSource.EvaluateThreadMode(fnv, other), "Nonmain branch queried an unused primary counter.");
        Require(FalloutActorProcessConstructionSource.EvaluateThreadMode(fnv, other with { SecondaryWorkTotal = new(5, "authored-actual-secondary-work") }),
            "Actual other-worker low queue arm was discarded.");
        Require(FalloutActorProcessConstructionSource.EvaluateThreadMode(fnv, state with { MainOwnerGate = new(true, "authored-main-owner-gate") }),
            "Original final owner gate was discarded.");
        Require(FalloutActorProcessConstructionSource.EvaluateThreadMode(fnv, state with
        { SpecialWorkerPresent = new(true, "authored-special-worker"), CurrentThreadIsSpecialWorker = new(false, "authored-other-special-worker") }),
            "A foreign special-worker relationship was admitted as normal registration.");
        Require(!FalloutActorProcessConstructionSource.EvaluateThreadMode(fnv, state with
        { ThreadedProcessingEnabled = new(false, "authored-disabled-real-worker-owner"), CurrentThreadIsMain = new(null, "unqueried-role") }),
            "Disabled source threading queried an unrelated thread relationship.");
        Reject(() => FalloutActorProcessConstructionSource.EvaluateThreadMode(fnv, state with { SecondaryWorkTotal = new(null, "authored-missing-current-work-count") }));
        Reject(() => FalloutActorProcessConstructionSource.EvaluateThreadMode(fnv, state with { Process = Guid.Empty }));
    }
    private static FalloutActorRegistrationThreadObservation Observation(Guid process) => new(process,
        new(true, "authored-original-threaded-processing-enabled"), new(false, "authored-actual-main-owner-gate"),
        new(true, "authored-actual-current-main-thread"), new(false, "authored-actual-worker-phase"),
        new(11, "authored-actual-primary-work-total"), new(7, "authored-actual-secondary-work-total"),
        new(false, "authored-no-special-worker"), new(null, "unqueried-special-worker-role"), "authored-source-thread-observation");
    private static FalloutReferenceWorld World(FalloutPluginStack records, string image)
    {
        var world = new FalloutReferenceWorld(records);
        world.ConfigureCombatGroups(FalloutCombatGroupDeclaration.ForExecutable(image), Selection);
        world.ConfigureActorPerception(FalloutActorPerceptionDeclaration.ForExecutable(image), Selection);
        world.ConfigureActualProcessRuntime(FalloutActorProcessRuntimeDeclaration.ForExecutable(image), Selection);
        world.ConfigureActorProcesses(FalloutActorProcessDeclaration.ForExecutable(image), Selection);
        return world;
    }
    private static FalloutPluginStack Stack(string directory, string image, int threads, int? prefs = null)
    {
        var declarations = new List<FalloutIniDeclaration> { new("iNumHWThreads:General", FalloutIniCollection.Main, 1) };
        if (prefs is { } number) declarations.Add(new("iNumHWThreads:General", FalloutIniCollection.Prefs, unchecked((uint)number)));
        var settings = FalloutInstallationSettings.ReadIniLayers(() => declarations,
            [new("authored-main-layer", FalloutIniCollection.Main, [new("General", "iNumHWThreads", threads.ToString(System.Globalization.CultureInfo.InvariantCulture))])],
            Selection + ":authored-source:" + image);
        return FalloutPluginStack.Load([new("Constructor.esm", Path.Combine(directory, "Constructor.esm"))], false, out _, settings);
    }
    private static byte[] Plugin() => Join(Record("TES4", 0, Field("HEDR", new byte[12])),
        Actor("NPC_", 7), Actor("NPC_", 0x701), Actor("CREA", 0x702), Record("CELL", 0x800, Field("DATA", [1, 0])),
        Group(Record("ACHR", 0x901, Field("NAME", BitConverter.GetBytes(0x701u)), Field("DATA", new byte[24])),
            Record("ACRE", 0x902, Field("NAME", BitConverter.GetBytes(0x702u)), Field("DATA", new byte[24]))));
    private static byte[] Actor(string signature, uint id) =>
        Record(signature, id, Field("ACBS", new byte[24]), Field("AIDT", new byte[20]));
    private static byte[] Group(params byte[][] children)
    {
        var data = Join(children); var result = new byte[data.Length + 24]; Encoding.ASCII.GetBytes("GRUP").CopyTo(result, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), (uint)result.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(8), 0x800); BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(12), 6);
        data.CopyTo(result, 24); return result;
    }
    private static byte[] Record(string signature, uint id, params byte[][] fields)
    {
        var data = Join(fields); var result = new byte[data.Length + 24]; Encoding.ASCII.GetBytes(signature).CopyTo(result, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), (uint)data.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(12), id); data.CopyTo(result, 24); return result;
    }
    private static byte[] Field(string name, byte[] data)
    {
        var result = new byte[data.Length + 6]; Encoding.ASCII.GetBytes(name).CopyTo(result, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(4), checked((ushort)data.Length)); data.CopyTo(result, 6); return result;
    }
    private static byte[] Join(params byte[][] rows) => rows.SelectMany(row => row).ToArray();
    private static FalloutFormKey Key(uint id) => new("Constructor.esm", id);
    private static T RoundTrip<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
    private static void Require(bool value, string error) { if (!value) throw new InvalidDataException(error); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is InvalidDataException or InvalidOperationException or NotSupportedException or KeyNotFoundException) { return; }
        throw new InvalidDataException("Foreign/missing/stale constructor source input was admitted.");
    }
}
