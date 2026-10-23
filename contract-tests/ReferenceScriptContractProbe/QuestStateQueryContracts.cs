using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class QuestStateQueryContracts
{
    internal static void Run(string? selected = null)
    {
        foreach (var query in selected is null ? new[] { "GetQuestRunning", "GetQuestCompleted" } : new[] { selected })
        {
            foreach (var shared in new[] { false, true }) Exercise(query, shared);
        }
        if (selected is null)
        {
            RunObjective();
            RunNotes();
            RunNoteRemoval();
            RunNoteReceivers();
            RunRemoveNoteReceivers();
            foreach (var shared in new[] { false, true })
            {
                ShortCircuit(shared);
                foreach (var query in new[] { "GetQuestRunning", "GetQuestCompleted" })
                {
                    foreach (var target in new[] { "BadGlobal", "MissingQuest" }) Refusal(query, target, shared);
                    Refusal(query, "TargetQuest", shared, bindTarget: false);
                }
            }
        }
        Console.WriteLine("OPENNV_QUEST_STATE_QUERY_CONTRACT_PASS fallbackAndShared=true liveState=true " +
            $"runningIndependentFromCompleted=true coldExact=true fullSuite={(selected is null ? "true" : "false")} " +
            $"invalidTargetAndShortCircuitChecks={(selected is null ? "passed" : "not-run")} ordinaryGameplay=unverified");
    }

    internal static void RunObjective(string? selected = null)
    {
        bool[] executors = selected == "fallback" ? [false] : selected == "shared" ? [true] : [false, true];
        foreach (var shared in executors)
        {
            using var fixture = new Fixture("set prefix to prefix + 1\nset result to GetObjectiveCompleted TargetQuest 10\nset suffix to suffix + 1", shared);
            fixture.Tick(); fixture.Expect(0, 1);
            fixture.Quests.ApplyObjective(fixture.Target, 10, display: true, value: true);
            fixture.Tick(); fixture.Expect(0, 2);
            fixture.Quests.ApplyObjective(fixture.Target, 10, display: false, value: true);
            fixture.Quests.ApplyObjective(fixture.Target, 10, display: true, value: false);
            fixture.Tick(); fixture.Expect(1, 3);
            Require(!fixture.Quests.ObjectiveDisplayed(fixture.Target, 10), "Completion query displayed a hidden objective.");
            fixture.Quests.Complete(fixture.Target);
            fixture.Quests.ApplyObjective(fixture.Target, 10, display: false, value: false);
            fixture.Tick(); fixture.Expect(0, 4);
            fixture.Quests.ApplyObjective(fixture.Target, 10, display: false, value: true);
            fixture.Quests.SetRunning(fixture.Target, false);
            var before = JsonSerializer.Serialize(fixture.Quests.Capture().Single(value => value.Quest == fixture.Target));
            fixture.Tick(); fixture.Expect(1, 5);
            Require(JsonSerializer.Serialize(fixture.Quests.Capture().Single(value => value.Quest == fixture.Target)) == before,
                "Objective query changed target state, flags or objective declarations.");
            var quests = JsonSerializer.Serialize(fixture.Quests.Capture());
            var scripts = JsonSerializer.Serialize(fixture.Scripts.Capture());
            using var cold = new Fixture(fixture.Body, shared, initialize: false);
            cold.Quests.Restore(RoundTrip(fixture.Quests.Capture().ToArray()));
            cold.Scripts.Restore(RoundTrip(fixture.Scripts.Capture()));
            Require(JsonSerializer.Serialize(cold.Quests.Capture()) == quests && JsonSerializer.Serialize(cold.Scripts.Capture()) == scripts,
                "Cold restore changed objective flags, values, counters or clocks.");
            cold.Tick(); cold.Expect(1, 6);
            foreach (var index in new[] { "0", "4294967295", "11" })
            {
                using var boundary = new Fixture($"set prefix to prefix + 1\nset result to GetObjectiveCompleted TargetQuest {index}\nset suffix to suffix + 1", shared);
                boundary.Tick(); boundary.Expect(0, 1);
                if (index == "11") continue;
                boundary.Quests.ApplyObjective(boundary.Target, uint.Parse(index, System.Globalization.CultureInfo.InvariantCulture), display: false, value: true);
                boundary.Tick(); boundary.Expect(1, 2);
            }
            using var unselected = new Fixture("if 0 && GetObjectiveCompleted MissingQuest 10\nset result to 99\nendif\n" +
                "if 1 || GetObjectiveCompleted BadGlobal -1\nset result to 7\nendif\nset suffix to 1", shared);
            unselected.Tick();
            Require(unselected.Error is null && unselected.Value(2) == 7 && unselected.Value(3) == 1,
                "Unselected objective query resolved a target or validated an unselected argument.");
            foreach (var target in new[] { "BadGlobal 10", "MissingQuest 10", "TargetQuest -1", "TargetQuest .5", "TargetQuest 4294967296", "TargetQuest 1e309", "TargetQuest", "TargetQuest 10 11" })
                Refusal("GetObjectiveCompleted", target, shared);
            Refusal("GetObjectiveCompleted", "TargetQuest 10", shared, bindTarget: false);
        }
        Console.WriteLine($"OPENNV_OBJECTIVE_QUERY_CONTRACT_PASS executors={selected ?? "fallback-and-shared"} " +
            "independentFlags=true uintBoundaries=true invalidArgumentsRefused=true shortCircuit=true coldExact=true prefixRetained=true gameplay=unverified");
    }

    private static void Exercise(string query, bool shared)
    {
        using var fixture = new Fixture($"set prefix to prefix + 1\nset result to {query} TargetQuest\nset suffix to suffix + 1", shared);
        fixture.Tick(); fixture.Expect(0, 1);
        fixture.Quests.SetRunning(fixture.Target, true);
        fixture.Tick(); fixture.Expect(query == "GetQuestRunning" ? 1 : 0, 2);
        fixture.Quests.Complete(fixture.Target);
        // Completion and running are separate authoritative flags.
        fixture.Quests.SetRunning(fixture.Target, true);
        fixture.Tick(); fixture.Expect(1, 3);
        fixture.Quests.SetRunning(fixture.Target, false);
        fixture.Tick(); fixture.Expect(query == "GetQuestCompleted" ? 1 : 0, 4);
        var expectedQuests = JsonSerializer.Serialize(fixture.Quests.Capture());
        var expectedScripts = JsonSerializer.Serialize(fixture.Scripts.Capture());
        using var cold = new Fixture(fixture.Body, shared, initialize: false);
        cold.Quests.Restore(RoundTrip(fixture.Quests.Capture().ToArray()));
        cold.Scripts.Restore(RoundTrip(fixture.Scripts.Capture()));
        Require(JsonSerializer.Serialize(cold.Quests.Capture()) == expectedQuests &&
            JsonSerializer.Serialize(cold.Scripts.Capture()) == expectedScripts,
            "Cold reconstruction changed quest query state, counters or execution clocks.");
        cold.Tick(); cold.Expect(query == "GetQuestCompleted" ? 1 : 0, 5);
        var targetBefore = JsonSerializer.Serialize(cold.Quests.Capture().Single(value => value.Quest == cold.Target));
        cold.Tick(); cold.Expect(query == "GetQuestCompleted" ? 1 : 0, 6);
        Require(JsonSerializer.Serialize(cold.Quests.Capture().Single(value => value.Quest == cold.Target)) == targetBefore,
            "Read-only quest queries changed the target quest.");
    }

    private static void ShortCircuit(bool shared)
    {
        using var fixture = new Fixture("if 0 && GetQuestRunning MissingQuest\nset result to 99\nendif\n" +
            "if 1 || GetQuestCompleted BadGlobal\nset result to 7\nendif\nset suffix to 1", shared);
        fixture.Tick();
        Require(fixture.Error is null && fixture.Value(2) == 7 && fixture.Value(3) == 1,
            "An unselected quest-query operand was resolved or executed.");
    }

    private static void Refusal(string query, string target, bool shared, bool bindTarget = true, bool bindNote = true)
    {
        using var fixture = new Fixture($"set prefix to prefix + 1\nset result to {query} {target}\nset suffix to 99", shared, bindTarget: bindTarget, bindNote: bindNote);
        fixture.Tick();
        Require(fixture.Error is not null && fixture.Value(1) == 1 && fixture.Value(2) == 0 && fixture.Value(3) == 0,
            "Invalid quest query ran its suffix or discarded its consumed prefix.");
        var error = fixture.Error;
        fixture.Tick();
        Require(fixture.Error == error && fixture.Value(1) == 1, "A failed quest query replayed its prefix.");
        using var cold = new Fixture(fixture.Body, shared, initialize: false, bindTarget: bindTarget, bindNote: bindNote);
        cold.Quests.Restore(RoundTrip(fixture.Quests.Capture().ToArray()));
        cold.Scripts.Restore(RoundTrip(fixture.Scripts.Capture()));
        cold.Tick();
        Require(cold.Error == error && cold.Value(1) == 1 && cold.Value(3) == 0,
            "Cold retry cleared a quest-query failure or replayed effects.");
    }

    internal static void RunOwned(string game, bool expectPrefixFailure = false)
    {
        RuntimeLiveContentSource.Configure(game, RuntimeLiveContentSource.FalloutNewVegasGame,
            activePlugins: RuntimeLiveContentSource.ResolveVanillaPlugins(game), includeImplicitPlugins: false);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var quest = FalloutDialogueTopic.Find(records, "QUST", "vDialogueEDE");
        var script = FalloutScriptLocals.AttachedScript(records, quest) ?? throw new InvalidDataException("Owned ED-E quest lacks its script.");
        var source = script;
        var original = source.ReadData();
        var hash = SHA256.HashData(original);
        var text = FalloutDialogueTopic.ScriptText(source.ReadSubrecords().Single(field => field.Signature == "SCTX").Data.Span);
        Require(text.Contains("GetQuestCompleted", StringComparison.OrdinalIgnoreCase), "Owned quest no longer exercises completion queries.");
        var locals = FalloutScriptLocals.Read(source);
        using var world = new FalloutReferenceWorld(records);
        var quests = new FalloutQuestState(records);
        var globals = new FalloutGlobalState(records.EffectiveRecords("GLOB").Select(FalloutGlobal.Read));
        var claims = records.EffectiveRecords("QUST").Select(value => value.FormKey).ToHashSet();
        var scripts = new FalloutQuestScripts(records, quests, claims, new FalloutPlayerInventory(), globals,
            defaultProcessingDelay: 0, references: world);
        scripts.RequireQuestExecution(quest.FormKey);
        // Explicit isolated inputs, not an imported campaign or recruitment state.
        quests.SetRunning(quest.FormKey, true);
        quests.SetVariable(quest.FormKey, locals["iAprilDead"], 1);
        // Deliberately completed source-state fixture: both completion queries
        // execute, while unrelated dead-actor and reward owners are unselected.
        // This is not a campaign completion or an assertion that those owners work.
        quests.EnterStage(quest.FormKey, 100);
        quests.Complete(quest.FormKey);
        var counter = quests.Variable(quest.FormKey, locals["iCounter"]);
        try
        {
            scripts.AdvanceClaimed(quest.FormKey, 0, new((_, _) => throw new InvalidDataException("Unexpected owned stage effect."), _ => 0));
        }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException)
        {
            // Failure-location observation only: retain the original failure and
            // do not retry, replace the source program or skip an operation.
            var instances = (System.Collections.IEnumerable)typeof(FalloutQuestScripts)
                .GetField("_instances", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .GetValue(scripts)!;
            var owner = instances.Cast<object>().Single(value =>
                ((FalloutPluginRecord)value.GetType().GetField("Quest", System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic)!.GetValue(value)!).FormKey == quest.FormKey);
            var program = (FalloutGameModeProgram)owner.GetType().GetField("Program", System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic)!.GetValue(owner)!;
            var statements = (IReadOnlyList<string[]>)typeof(FalloutGameModeProgram)
                .GetField("_lines", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(program)!;
            Console.Error.WriteLine(JsonSerializer.Serialize(new
            {
                diagnostic = "owned-full-script-refusal",
                quest = quest.FormKey.ToString(),
                script = source.FormKey.ToString(),
                sourceSha256 = Convert.ToHexString(hash),
                statement = program.LastStatement,
                tokens = statements[program.LastStatement],
                counterBefore = counter,
                counterAfter = quests.Variable(quest.FormKey, locals["iCounter"]),
                error = error.Message,
                fullScriptSuccess = false
            }));
            if (expectPrefixFailure)
            {
                var tokens = statements[program.LastStatement];
                Require(program.LastStatement == 83 && error.Message == "NVSE expression has an unbound operation." &&
                    tokens[0] == "if" && tokens.Contains("GetObjectiveCompleted") &&
                    tokens.Contains("gethasnote") && quests.Variable(quest.FormKey, locals["iCounter"]) == counter + 1,
                    "Owned query prefix did not reach the diagnosed independent condition refusal.");
                var questState = JsonSerializer.Serialize(quests.Capture());
                var scriptState = JsonSerializer.Serialize(scripts.Capture());
                RequireRetainedRefusal(() => scripts.AdvanceClaimed(quest.FormKey, 10,
                    new((_, _) => throw new InvalidDataException("Unexpected stage replay."), _ => 0)), error.Message);
                Require(JsonSerializer.Serialize(quests.Capture()) == questState && JsonSerializer.Serialize(scripts.Capture()) == scriptState,
                    "Retry changed the consumed owned prefix or cleared its refusal.");
                using var coldWorld = new FalloutReferenceWorld(records);
                var coldQuests = new FalloutQuestState(records);
                var coldScripts = new FalloutQuestScripts(records, coldQuests, claims, new FalloutPlayerInventory(), globals,
                    defaultProcessingDelay: 0, references: coldWorld);
                coldQuests.Restore(RoundTrip(quests.Capture().ToArray()));
                coldScripts.Restore(RoundTrip(scripts.Capture()));
                Require(JsonSerializer.Serialize(coldQuests.Capture()) == questState && JsonSerializer.Serialize(coldScripts.Capture()) == scriptState,
                    "Cold restore changed the owned query prefix or retained error.");
                RequireRetainedRefusal(() => coldScripts.AdvanceClaimed(quest.FormKey, 10,
                    new((_, _) => throw new InvalidDataException("Unexpected cold stage replay."), _ => 0)), error.Message);
                Require(JsonSerializer.Serialize(coldQuests.Capture()) == questState && JsonSerializer.Serialize(coldScripts.Capture()) == scriptState,
                    "Cold retry changed the consumed owned prefix or cleared its refusal.");
                Require(SHA256.HashData(source.ReadData()).SequenceEqual(hash), "Owned script bytes changed.");
                Console.WriteLine("OPENNV_OWNED_QUEST_QUERY_PREFIX_PASS completionGuardsReached=true counterIncrement=1 " +
                    "independentUnsupportedConditionRetained=true coldExact=true noPrefixReplay=true fullScriptSuccess=false " +
                    "runningQueryOwnedCoverage=false ordinaryRecruitment=false campaign=false retailParity=unverified");
                return;
            }
            throw;
        }
        Require(!expectPrefixFailure, "The diagnosed independent owned refusal disappeared; review this audit boundary.");
        var instance = scripts.Capture().Instances.Single(value => value.Quest == quest.FormKey);
        Require(instance.Error is null && quests.Variable(quest.FormKey, locals["iCounter"]) == counter + 1 &&
            quests.IsCompleted(quest.FormKey) && quests.Stage(quest.FormKey) == 100,
            "Actual unmodified ED-E GameMode did not pass its completion queries or changed the controlled completed state.");
        Require(SHA256.HashData(source.ReadData()).SequenceEqual(hash), "Owned script bytes changed.");
        Console.WriteLine($"OPENNV_OWNED_QUEST_STATE_QUERY_PASS quest={quest.FormKey} script={source.FormKey} " +
            $"sourceSha256={Convert.ToHexString(hash)} fullOriginalGameMode=true fallbackExecutor=true " +
            "controlledCompletedFixture=true unselectedDeadAndRewardOwners=unverified sourceUnchanged=true " +
            "ordinaryRecruitment=false campaign=false retailParity=unverified");
    }

    private sealed class Fixture : IDisposable
    {
        private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("opennv-quest-state-query-");
        private readonly FalloutPluginStack _records;
        private readonly FalloutReferenceWorld _world;
        internal readonly string Body;
        internal FalloutQuestState Quests { get; }
        internal FalloutQuestScripts Scripts { get; }
        internal FalloutReferenceWorld World => _world;
        internal FalloutPluginStack Records => _records;
        internal FalloutFormKey Note => new("QuestQueries.esm", 0x400);
        internal FalloutFormKey Target => new("QuestQueries.esm", 0x100);
        private FalloutFormKey Caller => new("QuestQueries.esm", 0x101);
        internal string? Error => Scripts.Capture().Instances.Single().Error;

        internal Fixture(string body, bool shared, bool initialize = true, bool bindTarget = true, bool bindNote = true, bool allowNoteEffects = false, bool bindPlayer = true)
        {
            Body = body;
            var header = new byte[20]; U32(3).CopyTo(header, 12); header[16] = 1;
            var declarations = new[] { "prefix", "result", "suffix" }.SelectMany((name, index) =>
            {
                var local = new byte[24]; U32((uint)index + 1).CopyTo(local, 0);
                return new[] { Field("SLSD", local), Field("SCVR", Text(name)) };
            }).ToArray();
            var bindings = new[] { Field("SCRO", U32(0x300)), Field("SCRO", U32(0x500)) }.Concat(bindPlayer ? new[] { Field("SCRO", U32(0x14)) } : []).Concat(bindTarget ? new[] { Field("SCRO", U32(0x100)) } : [])
                .Concat(bindNote ? new[] { Field("SCRO", U32(0x400)) } : []);
            File.WriteAllBytes(Path.Combine(_directory.FullName, "QuestQueries.esm"), Join(
                Record("TES4", 0, Field("HEDR", new byte[12])),
                Record("GMST", 0x401, Field("EDID", Text("sComputersAddedNote")), Field("DATA", Text("Note Added: %s"))),
                Record("NOTE", 0x400, Field("EDID", Text("CollectedNote"))),
                Record("REFR", 0x500, Field("EDID", Text("PlacedReceiver"))),
                Record("QUST", 0x100, Field("EDID", Text("TargetQuest")), Field("DATA", [0, 0]),
                    Field("QOBJ", U32(0)), Field("QOBJ", U32(10)), Field("QOBJ", U32(uint.MaxValue))),
                Record("QUST", 0x101, Field("EDID", Text("CallerQuest")), Field("SCRI", U32(0x200)), Field("DATA", [0, 0])),
                Record("GLOB", 0x300, Field("EDID", Text("BadGlobal")), Field("FNAM", [(byte)'f']), Field("FLTV", new byte[4])),
                Record("SCPT", 0x200, new[] { Field("SCHR", header) }.Concat(bindings)
                    .Concat(declarations).Append(Field("SCTX", Text("begin GameMode\n" + body + "\nend"))).ToArray())));
            _records = FalloutPluginStack.Load(_directory.FullName, ["QuestQueries.esm"]);
            _world = new(_records); Quests = new(_records);
            Scripts = new(_records, Quests, new HashSet<FalloutFormKey>(), Inventory,
                defaultProcessingDelay: 0, references: _world);
            if (shared)
            {
                var executor = new FalloutReferenceScripts(_records, _world, Quests,
                    new((_, _) => false, effect =>
                    {
                        if (!allowNoteEffects || effect.Kind is not (FalloutReferenceEffectKind.AddNote or FalloutReferenceEffectKind.AddItem or FalloutReferenceEffectKind.RemoveNote))
                            throw new InvalidDataException("Unexpected query effect.");
                        // Controlled command composition; the separate Godot
                        // audit exercises the actual native effect adapter.
                        if (effect.Kind == FalloutReferenceEffectKind.AddNote)
                            _world.AcquirePlayerNote(effect.Target!.Value, Inventory.Notifications);
                        else if (effect.Kind == FalloutReferenceEffectKind.RemoveNote)
                            _world.RemovePlayerNote(effect.Target!.Value);
                        else new FalloutInventoryCommands(_records, _world, Inventory, () => 1).Execute(
                            new(FalloutInventoryCommandKind.Add, effect.Target!.Value, effect.Argument, Count: effect.Value, Silent: effect.Enable));
                    }));
                Scripts.Host = new((_, _) => throw new InvalidDataException("Unexpected stage effect."), _ => 0,
                    ExecuteProgram: executor.ExecuteProgram);
            }
            if (initialize) { Quests.SetRunning(Caller, true); Quests.SetRunning(Target, false); }
        }

        internal FalloutPlayerInventory Inventory { get; } = new();
        internal double Value(uint index) => Quests.Variable(Caller, index);
        internal void Tick() => Scripts.Advance(0);
        internal void Expect(int expected, int ticks) => Require(Error is null && Value(1) == ticks && Value(2) == expected && Value(3) == ticks,
            $"Quest query did not observe authoritative state: {Error ?? "wrong value"}.");
        public void Dispose() { _world.Dispose(); _records.Dispose(); _directory.Delete(true); }
    }

    private static T RoundTrip<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
    private static void RequireRetainedRefusal(Action invoke, string expected)
    {
        var refused = false;
        try { invoke(); }
        catch (NotSupportedException error) when (error.Message == expected) { refused = true; }
        Require(refused, "Retry did not explicitly throw its retained source error.");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    private static byte[] U32(uint value) => BitConverter.GetBytes(value);
    private static byte[] Text(string value) => Encoding.ASCII.GetBytes(value + '\0');
    private static byte[] Join(params byte[][] values) => values.SelectMany(value => value).ToArray();
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
}
