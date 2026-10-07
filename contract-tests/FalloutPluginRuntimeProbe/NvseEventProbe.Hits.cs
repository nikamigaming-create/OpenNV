using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class NvseEventProbe
{
    internal static void HitHandlers()
    {
        var directory = Path.Combine("local", $"hit-handler-contract-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllBytes(Path.Combine(directory, "HitEvents.esm"), HitFixture());
            using var records = FalloutPluginStack.Load(directory, ["HitEvents.esm"]);
            using var world = new FalloutReferenceWorld(records);
            var quests = new FalloutQuestState(records);
            var events = new FalloutScriptEvents();
            var executor = HitExecutor(records, world, quests, events);
            var quest = records.GetEffective(HitKey(0x100));
            var definition = records.GetEffective(HitKey(0x200));
            var cell = FalloutCellSceneReader.Read(records, HitKey(0x500));
            world.LoadCell(cell);
            using var binding = world.BindHitDispatcher(actor => _ = executor.DispatchHitCallbacks(actor, .125));
            void Run(string body) => executor.ExecuteProgram(quest, definition,
                FalloutGameModeProgram.Read("begin Result\n" + body + "\nend", "Result"), 0);
            double Value(uint index) => quests.Variable(quest.FormKey, index);
            var first = HitKey(0x510);
            var second = HitKey(0x511);
            var player = records.RuntimeFormKey(0x14);
            double Count(FalloutFormKey actor) => world.Auxiliary.GetFloat(actor, "HitEvents.esm", "_hitCount");

            Run("eval 0 && SetOnHitEventHandler BrokenHit 1\n" +
                "SetOnHitEventHandler GlobalHit 1\nSetOnHitEventHandler GlobalHit -1 0");
            Require(events.Hits.Count == 1, "Duplicate hit registration was not idempotent.");
            world.BeforeActorHit(first);
            world.BeforeActorHit(second);
            world.BeforeActorHit(player);
            Require(Value(2) == 3 && Value(3) == 0x14 && Value(10) == .125 &&
                Count(first) == 1 && Count(second) == 1 && Count(player) == 1,
                "Hit callback lost its actual typed caller or failed to publish source-owned state.");
            HitReject(() => executor.DispatchHitCallbacks(first, double.NaN));
            HitReject(() => executor.DispatchHitCallbacks(first, -1));
            Require(Value(2) == 3, "Invalid hit frame time ran source callbacks.");
            events.Advance(.1, true, executor.InvokeFunction);
            events.Advance(.1, false, executor.InvokeFunction);
            Require(Value(2) == 3, "A hit callback ran from a frame or paused quest poll.");

            Run("SetOnHitEventHandler GlobalHit 1 ActorFilters");
            Require(events.Hits.Count == 3, "FLST filters did not expand direct actors once or admitted bases/nested lists.");
            world.BeforeActorHit(first);
            world.BeforeActorHit(second);
            Require(Value(2) == 6 && Count(first) == 3 && Count(second) == 2,
                "Global and actor scopes failed their independent delivery or leaked list membership.");
            Run("SetOnHitEventHandler GlobalHit 0");
            world.BeforeActorHit(second);
            world.BeforeActorHit(first);
            Require(Value(2) == 7 && events.Hits.Count == 2, "Global removal erased actor scopes or actor filters leaked.");
            Run("SetOnHitEventHandler GlobalHit 0 ActorFilters");
            Require(events.Hits.Count == 0, "List removal retained an admitted actor scope.");

            Run("filters = Ar_List ActorTwo\nSetOnHitEventHandler (call PickHandler) 1 (call PickActor)");
            Require(Value(9) == 2, "Typed hit-handler arguments were evaluated more than once.");
            world.BeforeActorHit(first);
            Require(!world.Get(first).Restrained && !world.Get(second).Restrained,
                "Dynamic typed actor filter selected a foreign actor.");
            world.BeforeActorHit(second);
            Require(world.Get(second).Restrained && Value(4) == 0 && Value(5) == 0 &&
                events.Hits.State.Single().Executions == 1,
                "The source callback lost real actor effects or omitted typed UDF parameter defaults.");
            HitReject(() => executor.InvokeFunction(HitKey(0x211), second, [], 0));
            Require(world.Get(second).Restrained, "Event defaults changed ordinary UDF argument admission.");

            var before = JsonSerializer.Serialize(events.Hits.State);
            foreach (var command in new[]
            {
                "SetOnHitEventHandler GlobalHit",
                "SetOnHitEventHandler GlobalHit 1 ActorOne ActorTwo",
                "SetOnHitEventHandler ActorOne 1",
                "SetOnHitEventHandler \"GlobalHit\" 1",
                "SetOnHitEventHandler GlobalHit ActorOne",
                "SetOnHitEventHandler GlobalHit 0.5",
                "SetOnHitEventHandler GlobalHit 1 \"ActorOne\"",
                "SetOnHitEventHandler GlobalHit 1 1296",
                "SetOnHitEventHandler GlobalHit 1 WrongFilter",
                "SetOnHitEventHandler GlobalHit 1 MalformedFilters",
                "SetOnHitEventHandler BrokenHit 1",
                "SetOnHitEventHandler UnboundHit 1",
            }) HitReject(() => Run(command));
            Require(JsonSerializer.Serialize(events.Hits.State) == before,
                "An invalid source argument, filter or function partially changed existing registrations.");
            Run("SetOnHitEventHandler BrokenHit 0");
            HitReject(() => Run("HitQuest.afterFault += 1\nSetOnHitEventHandler GlobalHit 0.5\nHitQuest.afterFault += 100"));
            Require(Value(6) == 1 && JsonSerializer.Serialize(events.Hits.State) == before,
                "Failed registration discarded its executed prefix or ran its suffix.");

            Run("SetOnHitEventHandler FaultHit 1\nSetOnHitEventHandler GlobalHit 1");
            world.BeforeActorHit(first);
            var fault = events.Hits.State.Single(state => state.Script == HitKey(0x212));
            Require(Value(6) == 2 && fault.Invocations == 1 && fault.Executions == 0 &&
                fault.LastCaller == first && fault.Error?.Contains("UnsupportedHitCommand", StringComparison.Ordinal) == true &&
                Count(first) == 5,
                "Callback failure lost its original source/caller/prefix or suppressed another handler.");
            world.BeforeActorHit(first);
            Run("SetOnHitEventHandler FaultHit 1");
            world.BeforeActorHit(first);
            Require(Value(6) == 2 && events.Hits.State.Single(state => state.Script == HitKey(0x212)).Invocations == 1,
                "A faulted callback or duplicate registration replayed its consumed prefix.");
            Run("SetOnHitEventHandler FaultHit 0\nSetOnHitEventHandler FaultHit 1");
            world.BeforeActorHit(first);
            Require(Value(6) == 3, "Explicit callback removal and fresh registration did not replace its failed scope.");

            Run("SetOnHitEventHandler FaultHit 0\nSetOnHitEventHandler Restrainer 0 ActorTwo\n" +
                "SetOnHitEventHandler GlobalHit 0\nSetOnHitEventHandler MutateHit 1\nSetOnHitEventHandler GlobalHit 1");
            var previous = Value(2);
            world.BeforeActorHit(first);
            Require(Value(2) == previous && Value(7) == 0,
                "Removed pending callbacks or newly registered filtered callbacks ran in the same hit.");
            world.BeforeActorHit(first);
            Require(Value(7) == 1, "A newly registered filtered callback did not begin on a later contact.");
            Run("SetOnHitEventHandler MutateHit 0\nSetOnHitEventHandler LateHit 0 ActorOne\nSetOnHitEventHandler GlobalHit 1");

            Run("SetOnHitEventHandler FaultHit 1");
            world.BeforeActorHit(first);
            HitCold(records, world, quests, events, executor, cell, first);
            HitFallback(records, cell, first);
            HitMasterFilters(directory);
            HitDispatcherLifetime(world, executor, first, HitKey(0x512));
            Console.WriteLine("OPENNV_SCRIPT_HIT_HANDLERS_PASS sourceCommands=true actorAndListFilters=true " +
                "actualCaller=true actorEffect=true defaultTypedParameters=true argumentsOnce=true inactiveLazy=true " +
                "contactsOnly=true frameContext=true independentScopes=true sourceMasterAdjustment=true " +
                "validationAtomic=true faultPrefixRetained=true noReplay=true mutationAdmission=true " +
                "fallbackQuest=true coldEffects=true processRebound=true coldRegistrationsAbsent=true dispatcherRetirement=true " +
                "nativeContactAndRetailTiming=unverified hitDataQueries=unbound");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static FalloutReferenceScripts HitExecutor(FalloutPluginStack records, FalloutReferenceWorld world,
        FalloutQuestState quests, FalloutScriptEvents events) => new(records, world, quests,
        new((_, _) => throw new InvalidDataException("Unexpected fixture furniture call."),
            _ => throw new InvalidDataException("Unexpected fixture presentation effect."), Events: events));

    private static void HitCold(FalloutPluginStack records, FalloutReferenceWorld world, FalloutQuestState quests,
        FalloutScriptEvents events, FalloutReferenceScripts executor, FalloutCellScene cell, FalloutFormKey actor)
    {
        var owner = new FalloutQuestScripts(records, quests, new HashSet<FalloutFormKey>(), new FalloutPlayerInventory(),
            defaultProcessingDelay: .01f, references: world, events: events);
        var scriptState = JsonSerializer.Deserialize<FalloutQuestScriptsSnapshot>(JsonSerializer.Serialize(owner.Capture()))!;
        var questState = JsonSerializer.Deserialize<FalloutQuestSnapshot[]>(JsonSerializer.Serialize(quests.Capture()))!;
        var referenceState = JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!;
        var oldCalls = quests.Variable(HitKey(0x100), 2);
        var faultPrefix = quests.Variable(HitKey(0x100), 6);
        var oldEffects = world.Auxiliary.GetFloat(actor, "HitEvents.esm", "_hitCount");
        using var restored = new FalloutReferenceWorld(records);
        restored.Restore(referenceState); restored.LoadCell(cell);
        var restoredQuests = new FalloutQuestState(records); restoredQuests.Restore(questState);
        var restoredOwner = new FalloutQuestScripts(records, restoredQuests, new HashSet<FalloutFormKey>(),
            new FalloutPlayerInventory(), defaultProcessingDelay: .01f, references: restored, events: events);
        restoredOwner.Restore(scriptState);
        var freshExecutor = HitExecutor(records, restored, restoredQuests, events);
        using var binding = restored.BindHitDispatcher(target => _ = freshExecutor.DispatchHitCallbacks(target, .25));
        events.LoadGame();
        restored.BeforeActorHit(actor);
        Require(restoredQuests.Variable(HitKey(0x100), 2) == oldCalls + 1 &&
            quests.Variable(HitKey(0x100), 2) == oldCalls &&
            restored.Auxiliary.GetFloat(actor, "HitEvents.esm", "_hitCount") == oldEffects + 1 &&
            world.Auxiliary.GetFloat(actor, "HitEvents.esm", "_hitCount") == oldEffects &&
            restored.Get(HitKey(0x511)).Restrained && restoredQuests.Variable(HitKey(0x100), 6) == faultPrefix &&
            restoredQuests.Variable(HitKey(0x100), 10) == .25 &&
            events.Hits.State.Single(state => state.Script == HitKey(0x212)).Error is not null,
            "Serialized cold effects or process registrations retained an old world/executor.");
        var coldEvents = new FalloutScriptEvents();
        Require(coldEvents.Hits.Count == 0 && coldEvents.Hits.Dispatch(records, actor,
            (_, _) => throw new InvalidDataException("A cold process invented a hit callback.")).Count == 0,
            "Process-owned registrations leaked into a cold process.");
        HitReject(() => coldEvents.Hits.Dispatch(records, HitKey(0x512), (_, _) => { }));
        Require(executor.DispatchHitCallbacks(actor, .125).All(result => result.Error is null),
            "Restoring another world poisoned the original executor.");
    }

    private static void HitMasterFilters(string directory)
    {
        File.WriteAllBytes(Path.Combine(directory, "Prelude.esm"), Record("TES4", 0, Field("HEDR", new byte[12])));
        File.WriteAllBytes(Path.Combine(directory, "HitFilterPatch.esp"),
            Record("TES4", 0, Field("HEDR", new byte[12]).Concat(Field("MAST", Text("Prelude.esm")))
                .Concat(Field("DATA", new byte[8])).Concat(Field("MAST", Text("HitEvents.esm")))
                .Concat(Field("DATA", new byte[8])).ToArray())
            .Concat(Record("FLST", 0x01000402, Field("EDID", Text("ActorFilters"))
                .Concat(Field("LNAM", BitConverter.GetBytes(0x01000510u)))
                .Concat(Field("LNAM", BitConverter.GetBytes(0x01000511u))).ToArray())).ToArray());
        using var records = FalloutPluginStack.Load(directory, ["HitEvents.esm", "Prelude.esm", "HitFilterPatch.esp"]);
        using var world = new FalloutReferenceWorld(records);
        world.LoadCell(FalloutCellSceneReader.Read(records, HitKey(0x500)));
        var quests = new FalloutQuestState(records);
        var events = new FalloutScriptEvents();
        var executor = HitExecutor(records, world, quests, events);
        executor.ExecuteProgram(records.GetEffective(HitKey(0x100)), records.GetEffective(HitKey(0x200)),
            FalloutGameModeProgram.Read("begin GameMode\nSetOnHitEventHandler GlobalHit 1 ActorFilters\nend"), 0);
        Require(events.Hits.State.Select(state => state.Filter!.Value).ToHashSet()
            .SetEquals([HitKey(0x510), HitKey(0x511)]),
            "Winning FLST members used runtime load indices instead of their declaring master table.");
        using var binding = world.BindHitDispatcher(actor => _ = executor.DispatchHitCallbacks(actor, .125));
        world.BeforeActorHit(HitKey(0x510)); world.BeforeActorHit(HitKey(0x511));
        Require(quests.Variable(HitKey(0x100), 2) == 2, "Master-adjusted hit filters did not deliver to their actual actors.");
    }

    private static void HitFallback(FalloutPluginStack records, FalloutCellScene cell, FalloutFormKey actor)
    {
        using var world = new FalloutReferenceWorld(records);
        world.LoadCell(cell);
        var quests = new FalloutQuestState(records);
        var events = new FalloutScriptEvents();
        var owner = new FalloutQuestScripts(records, quests, new HashSet<FalloutFormKey>(), new FalloutPlayerInventory(),
            defaultProcessingDelay: .01f, references: world, events: events);
        owner.Advance(.02);
        Require(events.Hits.Count == 1 && quests.Variable(HitKey(0x100), 1) == 1,
            "Fallback quest execution did not register through the same hit owner.");
        var executor = HitExecutor(records, world, quests, events);
        using var binding = world.BindHitDispatcher(target => _ = executor.DispatchHitCallbacks(target, .125));
        world.BeforeActorHit(actor);
        Require(quests.Variable(HitKey(0x100), 2) == 1,
            "Fallback registration did not reach the real shared callback executor.");
    }

    private static void HitDispatcherLifetime(FalloutReferenceWorld world, FalloutReferenceScripts executor,
        FalloutFormKey actor, FalloutFormKey nonActor)
    {
        var retiredCalls = 0;
        var activeCalls = 0;
        using var retired = world.BindHitDispatcher(_ => ++retiredCalls);
        using var active = world.BindHitDispatcher(target =>
        {
            ++activeCalls;
            _ = executor.DispatchHitCallbacks(target, .125);
        });
        retired.Dispose();
        world.BeforeActorHit(actor);
        Require(retiredCalls == 0 && activeCalls == 1, "A retired warm-cell dispatcher stole or detached its replacement.");
        active.Dispose();
        world.BeforeActorHit(actor);
        Require(activeCalls == 1, "A retired active dispatcher retained a callback into its old executor.");
        HitReject(() => world.BeforeActorHit(nonActor));
        world.UnloadCell(HitKey(0x500));
        HitReject(() => world.BeforeActorHit(actor));
        world.Dispose();
        HitReject(() => world.BeforeActorHit(actor));
    }

    private static FalloutFormKey HitKey(uint id) => new("HitEvents.esm", id);

    private static byte[] HitFixture()
    {
        var data = new byte[8]; data[0] = 1; BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(4), .01f);
        var actor = new byte[24]; actor[8] = 1;
        const string declarations = "short initialized\nint calls\nref target\nfloat defaultNumber\nref defaultActor\n" +
            "int afterFault\nint late\narray_var filters\nint evaluations\nfloat elapsed\n";
        var forms = new uint[] { 0x14, 0x100, 0x210, 0x211, 0x212, 0x213, 0x214, 0x215, 0x216, 0x217, 0x400,
            0x401, 0x402, 0x403, 0x404, 0x510, 0x511 };
        IEnumerable<byte> bytes = Record("TES4", 0, Field("HEDR", new byte[12]));
        bytes = bytes.Concat(Record("QUST", 0x100, Field("EDID", Text("HitQuest")).Concat(Field("DATA", data))
            .Concat(Field("SCRI", BitConverter.GetBytes(0x200u))).ToArray()));
        bytes = bytes.Concat(Script(0x200, "HitInit", declarations +
            "begin GameMode\nif initialized == 0\nSetOnHitEventHandler GlobalHit 1\ninitialized = 1\nendif\nend",
            ["initialized", "calls", "target", "defaultNumber", "defaultActor", "afterFault", "late", "filters", "evaluations", "elapsed"],
            quest: true, referenceForms: forms));
        bytes = bytes.Concat(Script(0x210, "GlobalHit", "begin Function {}\nHitQuest.calls += 1\nHitQuest.target = this\n" +
            "HitQuest.elapsed = GetSecondsPassed\n" +
            "this.AuxiliaryVariableSetFloat \"_hitCount\" (this.AuxiliaryVariableGetFloat \"_hitCount\" + 1)\n" +
            "SetFunctionValue \"discarded event return\"\nend", [], referenceForms: forms));
        bytes = bytes.Concat(Script(0x211, "Restrainer", "float damage\nref attacker\nbegin Function {damage attacker}\n" +
            "HitQuest.defaultNumber = damage\nHitQuest.defaultActor = attacker\nthis.SetRestrained 1\nend",
            ["damage", "attacker"], referenceForms: forms));
        bytes = bytes.Concat(Script(0x212, "FaultHit", "begin Function {}\nHitQuest.afterFault += 1\n" +
            "UnsupportedHitCommand\nHitQuest.afterFault += 100\nend", [], referenceForms: forms));
        bytes = bytes.Concat(Script(0x213, "BrokenHit", "begin Function {}\nUnsupported @\nend", [], referenceForms: forms));
        bytes = bytes.Concat(Script(0x214, "MutateHit", "begin Function {}\nSetOnHitEventHandler GlobalHit 0\n" +
            "SetOnHitEventHandler LateHit 1 this\nend", [], referenceForms: forms));
        bytes = bytes.Concat(Script(0x215, "LateHit", "begin Function {}\nHitQuest.late += 1\nend", [], referenceForms: forms));
        bytes = bytes.Concat(Script(0x216, "PickHandler", "begin Function {}\nHitQuest.evaluations += 1\n" +
            "SetFunctionValue Restrainer\nend", [], referenceForms: forms));
        bytes = bytes.Concat(Script(0x217, "PickActor", "begin Function {}\nHitQuest.evaluations += 1\n" +
            "SetFunctionValue HitQuest.filters[0]\nend", [], referenceForms: forms));
        bytes = bytes.Concat(Record("CREA", 0x400, Field("EDID", Text("ActorBase")).Concat(Field("ACBS", actor))
            .Concat(Field("DATA", new byte[17])).ToArray()));
        bytes = bytes.Concat(Record("STAT", 0x401, Field("EDID", Text("WrongFilter"))));
        bytes = bytes.Concat(Record("FLST", 0x402, Field("EDID", Text("ActorFilters"))
            .Concat(Field("LNAM", BitConverter.GetBytes(0x510u))).Concat(Field("LNAM", BitConverter.GetBytes(0x510u)))
            .Concat(Field("LNAM", BitConverter.GetBytes(0x14u))).Concat(Field("LNAM", BitConverter.GetBytes(0x400u)))
            .Concat(Field("LNAM", BitConverter.GetBytes(0x401u))).Concat(Field("LNAM", BitConverter.GetBytes(0x403u))).ToArray()));
        bytes = bytes.Concat(Record("FLST", 0x403, Field("EDID", Text("NestedActors"))
            .Concat(Field("LNAM", BitConverter.GetBytes(0x511u))).ToArray()));
        bytes = bytes.Concat(Record("FLST", 0x404, Field("EDID", Text("MalformedFilters"))
            .Concat(Field("LNAM", BitConverter.GetBytes(0x510u))).Concat(Field("LNAM", [1])).ToArray()));
        bytes = bytes.Concat(Record("CELL", 0x500, Field("DATA", [1])));
        var references = new[] { (0x510u, "ActorOne", 0x400u, "ACRE"), (0x511u, "ActorTwo", 0x400u, "ACRE"),
            (0x512u, "Object", 0x401u, "REFR") }.SelectMany(reference =>
                Record(reference.Item4, reference.Item1, Field("EDID", Text(reference.Item2))
                    .Concat(Field("NAME", BitConverter.GetBytes(reference.Item3))).Concat(Field("DATA", new byte[24])).ToArray())).ToArray();
        var group = new byte[24 + references.Length]; Encoding.ASCII.GetBytes("GRUP").CopyTo(group, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(4), (uint)group.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(8), 0x500);
        BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(12), 6);
        references.CopyTo(group, 24);
        return bytes.Concat(group).ToArray();
    }

    private static void HitReject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException or InvalidOperationException or
            KeyNotFoundException) { return; }
        throw new InvalidOperationException("Invalid hit registration, caller or retired owner was admitted.");
    }
}
