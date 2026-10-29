using Function = OpenNV.Runtime.Content.Definitions.FalloutConditionFunction;
using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Content.Definitions;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class SharedConditionContracts
{
    internal static void Run()
    {
        foreach (var id in Enum.GetValues<FalloutActorValueId>())
            Require(FalloutActorValueNames.Parse(FalloutActorValueNames.Name(id).ToUpperInvariant()) == id,
                "Source AV identity/name roundtrip disagrees across dispatch paths.");
        Require(FalloutActorValueNames.Parse("Guns") == FalloutActorValueNames.Parse("SmallGuns") &&
            FalloutActorValueNames.Parse("RadLevel") == FalloutActorValueNames.Parse("RadiationRads") &&
            OpenNV.Runtime.World.Actors.FalloutActorValue.SourceIndex("Guns") == (int)FalloutActorValueId.SmallGuns &&
            OpenNV.Runtime.World.Actors.FalloutActorValue.UserSlot((int)FalloutActorValueId.Variable10) == "variable10" &&
            FalloutBodyPartData.LimbActorValue("BrainCondition") == (int)FalloutActorValueId.BrainCondition,
            "Source aliases, old pool adapters and script queries have competing AV definitions.");
        Require(OpenNV.Runtime.World.Actors.FalloutActorValue.SourceSlot("Mood") == "mood",
            "AI-data values did not join the shared source pool owner.");
        var directory = Directory.CreateTempSubdirectory("opennv-shared-conditions-");
        try
        {
            var female = new byte[24]; female[0] = 1;
            var playerStats = new byte[24]; playerStats[8] = 1; playerStats[14] = 100;
            var ai = new byte[20]; ai[0] = 2; ai[1] = 1; ai[2] = 73; ai[3] = 44; ai[4] = 6; ai[14] = 1;
            var inheritedAi = new byte[24]; inheritedAi[22] = (byte)FalloutActorTemplateGroup.AiData;
            File.WriteAllBytes(Path.Combine(directory.FullName, "Main.esm"), Join(Header(),
                Record("NPC_", 7, Field("ACBS", playerStats), Field("AIDT", ai),
                    Field("DATA", Join(U32(100), [5, 5, 5, 5, 5, 5, 5]))),
                Record("NPC_", 0x20, Field("EDID", Text("SharedActorBase")), Field("ACBS", female),
                    Field("AIDT", ai), Field("RNAM", U32(0x30)), Field("VTCK", U32(0x31)), Field("SNAM", Join(U32(0x32), [2, 0, 0, 0]))),
                Record("CREA", 0x21, Field("ACBS", new byte[24]), Field("AIDT", ai), Field("VTCK", U32(0x31))),
                Record("NPC_", 0x23, Field("ACBS", inheritedAi), Field("TPLT", U32(0x20))),
                Record("ACTI", 0x22), Record("RACE", 0x30),
                Record("VTYP", 0x31, Field("EDID", Text("TestVoice"))), Record("FACT", 0x32),
                Record("GLOB", 0x40, Field("EDID", Text("Threshold")), Field("FNAM", [(byte)'f']), Field("FLTV", BitConverter.GetBytes(1f))),
                Record("QUST", 0x50, Field("DATA", [1, 0])), Record("IDLE", 0x70),
                Record("WEAP", 0x71, Field("EDID", Text("SourceItem"))),
                Record("FLST", 0x72, Field("EDID", Text("SharedMutableList")), Field("LNAM", U32(0x71))),
                Record("AMMO", 0x74), Record("AMMO", 0x75),
                Record("FLST", 0x76, Field("LNAM", U32(0x74))),
                Record("FLST", 0x77, Field("LNAM", U32(0x71))),
                Record("FLST", 0x78, Field("LNAM", U32(0))), Record("FLST", 0x79),
                Record("CELL", 0x100, Field("EDID", Text("SharedRoom")), Field("DATA", [1])), Group(0x100,
                    Record("ACHR", 0x200, Field("EDID", Text("SharedActor")), Field("NAME", U32(0x20)), Field("DATA", new byte[24])),
                    Record("ACRE", 0x201, Field("NAME", U32(0x21)), Field("DATA", new byte[24])),
                    Record("REFR", 0x203, Field("NAME", U32(0x22)), Field("XLKR", U32(0x200)), Field("DATA", new byte[24])))));
            File.WriteAllBytes(Path.Combine(directory.FullName, "Other.esm"), Join(Header(),
                Record("VTYP", 0x31, Field("EDID", Text("WrongVoice"))), Record("WEAP", 0x71)));
            const string program = "float result\nbegin GameMode\nset result to 0\n" +
                "AddFormToFormList SharedMutableList SharedActorBase\n" +
                "SharedActor.RestoreAV Health 5.5\n" +
                "SharedActor.SetAV Confidence 3\nSharedActor.ForceAV Confidence 3\nSharedActor.ModAV Confidence -1\nSharedActor.DamageAV Confidence 1\nSharedActor.RestoreAV Confidence 1\n" +
                "player.RestoreAV Mood 100\nplayer.SetAV Mood 6.9\nplayer.ForceAV Mood 6.9\nplayer.ModAV Mood -1.75\nplayer.DamageAV Mood 0.75\nplayer.RestoreAV Mood 0.25\n" +
                "SharedActor.RestoreAV Mood 100\nSharedActor.SetAV Mood 6.9\nSharedActor.ForceAV Mood 6.9\nSharedActor.ModAV Mood -1.75\nSharedActor.DamageAV Mood 0.75\nSharedActor.RestoreAV Mood 0.25\n" +
                "if SharedActor.GetIsVoiceType TestVoice && SharedActor.IsEquipped SourceItem && player.GetIsSex Female && " +
                "SharedActor.IsInList SharedMutableList && ListGetCount SharedMutableList == 2 && " +
                "SharedActor.GetBaseAV Confidence == 3 && SharedActor.GetPermanentActorValue Confidence == 2 && SharedActor.GetAV Confidence == 2 && " +
                "player.GetAV LeftAttackCondition == 17 && player.GetAV Aggression == 2 && player.GetAV Mood == 4.5 && " +
                "SharedActor.GetAV Mood == 4.5 && player.GetBaseAV Mood == 6 && player.GetPermanentActorValue Mood == 5 && GetGlobalValue Threshold == 0\n" +
                "set result to 1\nendif\nend";
            var local = new byte[24]; U32(1).CopyTo(local, 0);
            var header = new byte[20]; U32(1).CopyTo(header, 12); header[16] = 1;
            File.WriteAllBytes(Path.Combine(directory.FullName, "Queries.esp"), Join(Header("Other.esm", "Main.esm"),
                Record("ACTI", 0x01000022, Field("SCRI", U32(0x02000090))),
                Record("PACK", 0x02000080),
                Record("SCPT", 0x02000090, Field("SCHR", header), Field("SLSD", local), Field("SCVR", Text("result")),
                    Field("SCRO", U32(0x01000200)), Field("SCRO", U32(0x01000031)),
                    Field("SCRO", U32(0x01000071)), Field("SCRO", U32(0x01000040)), Field("SCRO", U32(0x01000014)),
                    Field("SCRO", U32(0x01000072)), Field("SCRO", U32(0x01000020)),
                    Field("SCTX", Text(program))),
                Record("QUST", 0x02000091, Field("DATA", [1, 0]), Field("SCRI", U32(0x02000090)))));
            using var records = FalloutPluginStack.Load(directory.FullName, ["Main.esm", "Other.esm", "Queries.esp"]);
            using var world = new FalloutReferenceWorld(records);
            world.LoadCell(FalloutCellSceneReader.Read(records, Key(0x100)));
            var quests = new FalloutQuestState(records);
            var globals = FalloutGlobalState.Read(records);
            var owner = records.GetEffective(new("Queries.esp", 0x80));
            var scope = new FalloutConditionScope(Key(0x200), Key(0x201), world.GetLinkedRef);
            var draws = 0;
            var hour = 7f;
            var runtime = new FalloutConditionRuntime(records, quests, world)
            {
                Globals = globals, Hour = () => hour, RandomPercent = () => { draws++; return 10; },
            };
            FalloutCondition Query(Function function, uint argument = 0, uint runOn = 0, uint reference = 0) =>
                new(owner, 0, 0, (ushort)function, argument, 0, runOn, reference);
            Require(runtime.Evaluate(Query(Function.GetIsVoiceType, 0x01000031), scope) == 1 &&
                runtime.Evaluate(Query(Function.GetIsVoiceType, 0x01000031, 1), scope) == 1 &&
                runtime.Evaluate(Query(Function.GetIsID, 0x01000021, 1), scope) == 1 &&
                runtime.Evaluate(Query(Function.GetIsID, 0x01000020, 2, 0x01000200), scope) == 1 &&
                runtime.Evaluate(Query(Function.GetIsReference, 0x01000200, 4), scope with { Caller = Key(0x203) }) == 1,
                "Shared caller/target/explicit/linked identity lost its declaring namespace.");
            Require(runtime.Evaluate(Query(Function.GetIsSex, 1), scope) == 1 &&
                runtime.Evaluate(Query(Function.GetFactionRank, 0x01000032), scope) == 2,
                "Shared traits did not use actual winning NPC fields.");
            world.BindPlayerConditions(condition => condition.Kind switch
            {
                Function.GetIsSex or Function.GetPCIsSex when condition.Argument1 <= 1 => condition.Argument1 == 1 ? 1 : 0,
                Function.GetInCell => world.IsInCell(records.RuntimeFormKey(0x14), condition.FormArgument1, Key(0x100)) ? 1 : 0,
                _ => throw new NotSupportedException("Unowned synthetic player query."),
            });
            Require(runtime.Evaluate(Query(Function.GetIsSex, 1, 1), scope with { Target = records.RuntimeFormKey(0x14) }) == 1 &&
                runtime.Evaluate(Query(Function.GetPCIsSex, 1), scope) == 1, "Player sex borrowed actor traits instead of the bound player owner.");
            var dialogue = new FalloutDialogueConditions(records, quests, Key(0x200),
                FalloutDialogueSpeaker.Read(records, Key(0x20)), condition => runtime.Evaluate(condition, scope), world: world);
            Require(dialogue.Evaluate(Query(Function.GetCurrentTime)) == 7 && dialogue.Evaluate(Query(Function.GetGlobalValue, 0x01000040)) == 1,
                "Dialogue lost the shared clock/global fallback.");
            Require(runtime.Evaluate(Query(Function.GetGlobalValue, 0x01000040) with { Argument2 = 7 }, scope) == 1,
                "GetGlobalValue treated unused declaration storage as a second semantic argument.");
            Require(runtime.Evaluate(Query(Function.GetGlobalValue, 0x01000040), new()) == 1,
                "Global query demanded an unrelated physical caller.");
            Reject(() => runtime.Evaluate(Query(Function.GetIsVoiceType, 0x01000031), new()));
            Require(dialogue.Evaluate(Query(Function.GetInCell, 0x01000100, 1)) == 1 && dialogue.Evaluate(Query(Function.GetInCell, 0x01000100)) == 1,
                "Dialogue cell queries lost the actual player owner or speaker placement.");
            hour = 8;
            Require(dialogue.Evaluate(Query(Function.GetCurrentTime)) == 8, "Dialogue cached a mutable clock value.");
            var compareGlobal = Query(Function.GetIsVoiceType, 0x01000031) with
            { Flags = 4, Comparison = BitConverter.Int32BitsToSingle(0x01000040) };
            Require(FalloutCondition.AllPass([compareGlobal], condition => runtime.Evaluate(condition, scope), true),
                "Comparison-global union was interpreted as a floating literal.");
            globals.Set(Key(0x40), 0);
            Require(!FalloutCondition.AllPass([compareGlobal], condition => runtime.Evaluate(condition, scope), true),
                "Comparison-global predicate cached its earlier value.");
            var first = Query(Function.GetIsVoiceType, 0x01000031) with { Flags = 1, Comparison = 1 };
            var random = Query(Function.GetRandomPercent) with { Comparison = 10 };
            Require(FalloutCondition.AllPass([first, random], condition => runtime.Evaluate(condition, scope), true) && draws == 0 &&
                FalloutCondition.AllPass([first with { Comparison = 0 }, random], condition => runtime.Evaluate(condition, scope), true) && draws == 1,
                "Shared dispatch changed source-order OR random consumption.");
            Require(runtime.Evaluate(Query(Function.GetIsID, 0x01000021) with { Flags = 2 }, scope) == 1,
                "Legacy target flag borrowed the caller.");
            quests.EnterStage(Key(0x50), 10);
            Require(runtime.Evaluate(Query(Function.GetStage, 0x01000050), scope) == 10, "Quest state bypassed its authoritative owner.");
            Reject(() => runtime.Evaluate(Query(Function.GetIsVoiceType, 0x01000031, 3), scope));
            Reject(() => runtime.Evaluate(Query(Function.GetIsVoiceType, 0x01000031, 2), scope));
            Reject(() => runtime.Evaluate(Query(Function.GetIsVoiceType, 0x01000032), scope));
            Reject(() => new FalloutConditionRuntime(records, quests, world).Evaluate(Query(Function.GetCurrentTime), scope));
            Reject(() => runtime.Evaluate(Query(Function.IsLastIdlePlayed, 0x01000070) with { Argument2 = 1 }, scope));
            Reject(() => new FalloutConditionRuntime(records, quests, world) { Hour = () => float.NaN }.Evaluate(Query(Function.GetCurrentTime), scope));
            Reject(() => new FalloutConditionRuntime(records, quests, world) { Distance = (_, _) => -1 }
                .Evaluate(Query(Function.GetDistance, 0x01000201), scope));
            Reject(() => runtime.Evaluate(Query((Function)65000), scope));
            ScriptConsumers(records, world, quests, globals);
            MutableState(records, world, quests);
            History(records, world, runtime, Query(Function.IsLastIdlePlayed, 0x01000070), scope);
            var retired = new FalloutReferenceWorld(records);
            var staleCalls = 0;
            retired.BindPlayerConditions(_ => { staleCalls++; return 1; });
            retired.Dispose();
            var disposed = false;
            try { retired.QueryNativeCondition(records.RuntimeFormKey(0x14), Query(Function.GetIsSex, 1)); }
            catch (ObjectDisposedException) { disposed = true; }
            Require(disposed && staleCalls == 0, "Disposed world retained an executable player process callback.");
            Console.WriteLine("OPENNV_SHARED_CONDITION_CONTRACT_PASS callerTargetExplicitLinked=true sourceNamespaces=true " +
                "dialogueClockGlobals=true playerOwner=true comparisonGlobal=true lazyRandom=true parsedScriptConsumers=true " +
                "typedNativeOperands=true interruptedPrefix=true coldHistory=true missingOwnersRefused=true compiledVm=unimplemented");
        }
        finally { directory.Delete(true); }
    }

    private static void ScriptConsumers(FalloutPluginStack records, FalloutReferenceWorld world, FalloutQuestState quests,
        FalloutGlobalState globals)
    {
        var nativeCalls = 0;
        float Native(FalloutConditionQuery condition)
        {
            if (condition.Kind != Function.GetEquipped || condition.FormArgument1 != Key(0x71))
                throw new InvalidDataException("Typed script operand was reinterpreted in the declaring master's slot.");
            nativeCalls++; return 1;
        }
        world.Get(Key(0x200)).QueryNativeCondition = Native;
        var script = records.GetEffective(new("Queries.esp", 0x90));
        var source = FalloutDialogueTopic.ScriptText(script.ReadSubrecords().Single(field => field.Signature == "SCTX").Data.Span);
        var player = new FalloutPlayerActorValues(records);
        player.BindConstantModifiers((_, _) => []); // Fixture has no SPLO declarations.
        double ReadPlayer(string name, OpenNV.Runtime.World.Actors.FalloutActorValueRead kind) => name == "leftattackcondition"
            ? kind == OpenNV.Runtime.World.Actors.FalloutActorValueRead.Current ? 17 :
                throw new NotSupportedException("Fixture limb owner only supplies current state.")
            : player.Read(FalloutPlayerActorValues.PoolValue(name), kind);
        var executor = new FalloutReferenceScripts(records, world, quests,
            new((_, _) => false, _ => throw new InvalidDataException("Shared-state program emitted an unrelated effect."), Globals: globals,
                ReadActorValue: (actor, name, kind) => actor == records.RuntimeFormKey(0x14)
                    ? ReadPlayer(name, kind) : world.ActorValue(actor, name, kind),
                ChangeActorValue: (actor, name, operation, amount) =>
                {
                    if (actor == records.RuntimeFormKey(0x14)) player.Change(name, operation, amount);
                    else world.ChangeActorValue(actor, name, operation, amount);
                }));
        // Fixture begins with an existing damaged health pool; both script
        // adapters must reach the actual world health restore owner.
        world.Get(Key(0x200)).ActorValues["health"] = new(100, Damage: -10);
        executor.ExecuteProgram(records.GetEffective(Key(0x203)), script, FalloutGameModeProgram.Read(source), 0);
        Require(world.Get(Key(0x203)).Read(1) == 1 && nativeCalls == 1 && world.Health(Key(0x200)).Current == 95.5f,
            "Reference parsed-script queries did not reach shared handlers with typed forms.");
        var quest = new FalloutFormKey("Queries.esp", 0x91);
        var scripts = new FalloutQuestScripts(records, quests, new HashSet<FalloutFormKey>(), new(),
            globals: globals, references: world, defaultProcessingDelay: 0);
        scripts.Host = new((_, _) => throw new InvalidDataException("Query-only program changed a stage."), _ => 0,
            ReadPlayerActorValue: ReadPlayer, ChangePlayerActorValue: player.Change);
        world.Get(Key(0x200)).ActorValues["health"] = new(100, Damage: -10);
        scripts.Advance(0);
        Require(quests.Variable(quest, 1) == 1 && nativeCalls == 2 && scripts.Capture().Instances.Single().Error is null &&
            world.Health(Key(0x200)).Current == 95.5f,
            $"Quest fallback shared handlers failed: result={quests.Variable(quest, 1)} nativeCalls={nativeCalls} error={scripts.Capture().Instances.Single().Error}");
        world.Get(Key(0x200)).QueryNativeCondition = null;
        scripts.Advance(1);
        Require(quests.Variable(quest, 1) == 0 && scripts.Capture().Instances.Single().Error is not null,
            "Retired native owner fabricated a result or discarded the executed prefix.");
        world.Get(Key(0x200)).QueryNativeCondition = Native;
        scripts.Advance(1);
        Require(quests.Variable(quest, 1) == 0 && nativeCalls == 2,
            "Stopped script replayed its consumed query prefix after owner replacement.");
        // The separate behavioral cold test has no health/anatomy fixture.
        world.Get(Key(0x200)).ActorValues.Remove("health");

    }

    private static void History(FalloutPluginStack records, FalloutReferenceWorld world, FalloutConditionRuntime runtime,
        FalloutCondition condition, FalloutConditionScope scope)
    {
        var state = world.Get(scope.Caller!.Value);
        state.InitializeIdleRequestHistory(newProcess: true);
        state.QueryLastIdleRequest = () => state.LastIdleRequest;
        Require(runtime.Evaluate(condition, scope) == 0, "New process invented request history.");
        state.RecordIdleRequest(records, Key(0x70));
        Require(runtime.Evaluate(condition, scope) == 1, "Shared history query lost accepted request identity.");
        using var cold = new FalloutReferenceWorld(records);
        cold.Restore(JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!);
        cold.Get(scope.Caller!.Value).QueryLastIdleRequest = () => cold.Get(scope.Caller!.Value).LastIdleRequest;
        Require(new FalloutConditionRuntime(records, new(records), cold).Evaluate(condition, scope) == 1,
            "Shared history query changed after cold restoration.");
    }

    private static FalloutFormKey Key(uint id) => new("Main.esm", id);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is NotSupportedException or InvalidDataException) { return; }
        throw new InvalidDataException("Invalid shared condition context was accepted.");
    }
    private static byte[] U32(uint value) => BitConverter.GetBytes(value);
    private static byte[] Text(string value) => Encoding.ASCII.GetBytes(value + '\0');
    private static byte[] Join(params byte[][] values) => values.SelectMany(value => value).ToArray();
    private static byte[] Header(params string[] masters) => Record("TES4", 0,
        [Field("HEDR", new byte[12]), .. masters.SelectMany(master => new[] { Field("MAST", Text(master)), Field("DATA", new byte[8]) })]);
    private static byte[] Field(string name, byte[] data)
    {
        var bytes = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), checked((ushort)data.Length)); data.CopyTo(bytes, 6); return bytes;
    }
    private static byte[] Record(string name, uint id, params byte[][] fields)
    {
        var data = Join(fields); var bytes = new byte[24 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        U32((uint)data.Length).CopyTo(bytes, 4); U32(id).CopyTo(bytes, 12); data.CopyTo(bytes, 24); return bytes;
    }
    private static byte[] Group(uint cell, params byte[][] rows)
    {
        var data = Join(rows); var bytes = new byte[24 + data.Length]; Encoding.ASCII.GetBytes("GRUP").CopyTo(bytes, 0);
        U32((uint)bytes.Length).CopyTo(bytes, 4); U32(cell).CopyTo(bytes, 8); U32(6).CopyTo(bytes, 12); data.CopyTo(bytes, 24); return bytes;
    }
}
