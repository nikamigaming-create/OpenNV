using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static partial class CompiledScriptContracts
{
    internal static void QuestRecurrence()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-quest-recurrence-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            foreach (var diagnostic in new string?[] { null, "begin GameMode\nset value to 999\nend", "This is deliberately malformed SCTX", "duplicate" })
            {
                var selected = Path.Combine(directory, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(selected);
                var input = Path.Combine(selected, "Bytecode.esm"); File.WriteAllBytes(input, RecurrenceFixture(diagnostic));
                var hash = SHA256.HashData(File.ReadAllBytes(input));
                using var records = FalloutPluginStack.Load(selected, ["Bytecode.esm"]);
                RecurrenceColdAndPause(records);
                RecurrenceFailures(records);
                RecurrenceAdmission(records);
                RecurrenceManualSaveLease(records, selected);
                RecurrenceMenuAndSource(records);
                RecurrenceClockTiming(records);
                Require(hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(input))), "Canonical fixture source changed.");
            }
            RecurrenceUnsupported(directory);
        }
        finally { Directory.Delete(directory, recursive: true); }
        Console.WriteLine("OPENNV_COMPILED_QUEST_RECURRENCE_CONTRACT_PASS originalRanges=true realLeases=true retiredPrefixChecked=true branchCold=true pause=true callbackPrefix=true unknownPrefix=true ordinalIdentity=true legacyRefused=true");
    }

    private static FalloutQuestScripts RecurrenceScheduler(FalloutPluginStack records, FalloutQuestState quests,
        FalloutReferenceWorld world, float delay = 0) => new(records, quests, new HashSet<FalloutFormKey> { Key(0x20), Key(0x22), Key(0x23), Key(0x24), Key(0x25) },
            new(), defaultProcessingDelay: delay, references: world);
    private static FalloutQuestScriptHost RecurrenceHost(FalloutReferenceScripts executor, Func<bool, bool>? gate = null) => new(
        (_, _) => throw new InvalidDataException("Compiled dispatch called diagnostic PrepareSetStage."),
        _ => throw new InvalidDataException("Unexpected source actor-value fallback."),
        (_, _, _, _) => throw new InvalidDataException("Compiled dispatch used a void/source host."),
        ExecuteCompiledProgram: executor.ExecuteProgram, CanContinueCompiled: gate);
    private static FalloutReferenceScripts RecurrenceExecutor(FalloutPluginStack records, FalloutReferenceWorld world,
        FalloutQuestState quests, Action<FalloutReferenceScriptEffect> apply) => new(records, world, quests,
            new((_, _) => false, apply, Command: (_, _, _, _) => throw new InvalidDataException("SCDA called a diagnostic command host.")));
    private static FalloutQuestScriptSnapshot MainState(FalloutQuestScripts scripts) => scripts.Capture().Instances.Single(value => value.Quest == Key(0x20));

    private static void RecurrenceColdAndPause(FalloutPluginStack records)
    {
        var quests = new FalloutQuestState(records); quests.SetRunning(Key(0x20), true);
        using var world = new FalloutReferenceWorld(records);
        var scheduler = RecurrenceScheduler(records, quests, world);
        FalloutQuestStages? stages = null; var stageCalls = 0;
        var executor = RecurrenceExecutor(records, world, quests, effect =>
        {
            Require(effect.Kind == FalloutReferenceEffectKind.SetStage, "Unexpected recurring fixture effect.");
            ++stageCalls; stages!.Enter(effect.Target!.Value, effect.Stage);
        });
        stages = new(records, quests, executor.StageSteps, _ => throw new InvalidDataException("Unexpected stage condition."));
        var gateCalls = 0; var host = RecurrenceHost(executor, _ => gateCalls++ < 3);
        scheduler.AdvanceClaimed(Key(0x20), .125, host);
        var pending = MainState(scheduler);
        Require(pending is { Executions: 0, Clock.Invocations: 0, Compiled.Pending.EventIndex: 0 } &&
            pending.Compiled!.Pending!.Cursor is { CommittedInstructions: 2, Branches.Count: 1 } &&
            pending.Compiled.Pending.LastSlice is { Disposition: "suspended", Invocation: > 0 } &&
            quests.Variable(Key(0x20), 1) == 1 && quests.Variable(Key(0x20), 2) == 0 && stageCalls == 0 &&
            world.ScriptManualSaves.EnteredInvocations == 0, "Interrupted branch lost its prefix/actual lease or completed a clock.");
        var warm = JsonSerializer.Serialize(scheduler.Capture());
        scheduler.Host = RecurrenceHost(executor);
        scheduler.ExecuteClaimedMenu(Key(0x20), 1001, RecurrenceHost(executor));
        scheduler.Advance(100, gameMode: false, menus: [1001, 2]);
        scheduler.Advance(100, execute: false);
        Require(warm == JsonSerializer.Serialize(scheduler.Capture()) && stageCalls == 0,
            "Wrong mode/observational frame consumed a compiled pending cursor or cadence.");

        var saved = Copy(scheduler.Capture()); var savedQuests = Copy(quests.Capture());
        using var coldWorld = new FalloutReferenceWorld(records);
        var coldQuests = new FalloutQuestState(records); coldQuests.Restore(savedQuests);
        var cold = RecurrenceScheduler(records, coldQuests, coldWorld); cold.Restore(saved);
        Require(JsonSerializer.Serialize(saved) == JsonSerializer.Serialize(cold.Capture()) && coldQuests.Variable(Key(0x20), 1) == 1,
            "Cold admission replayed a consumed instruction or invented a lease.");
        FalloutQuestStages? coldStages = null; var coldStageCalls = 0;
        var coldExecutor = RecurrenceExecutor(records, coldWorld, coldQuests, effect =>
        { ++coldStageCalls; coldStages!.Enter(effect.Target!.Value, effect.Stage); });
        coldStages = new(records, coldQuests, coldExecutor.StageSteps, _ => throw new InvalidDataException("Unexpected cold condition."));
        var coldHost = RecurrenceHost(coldExecutor);
        cold.AdvanceClaimed(Key(0x20), 0, coldHost);
        var completed = MainState(cold);
        Require(completed is { Executions: 1, Clock.Invocations: 1, Compiled.Pending: null, Compiled.CompletedDispatches: 1 } &&
            coldQuests.Variable(Key(0x20), 1) == 1 && coldQuests.Variable(Key(0x20), 2) == 10 &&
            coldQuests.Variable(Key(0x20), 3) == 7 && coldQuests.Variable(Key(0x20), 4) == 1 && coldStageCalls == 1 &&
            coldStages.CaptureResults().Single() is { Completed: true, Steps: 1 },
            "Cold suffix re-evaluated the IF, replayed the prefix, lost canonical stage results or finished an extra clock.");
        Require(completed.Compiled!.LastCompletedEvents.Count == 2 && completed.Compiled.LastCompletedEvents.All(receipt =>
            receipt is { Disposition: "completed", Invocation: > 0 } && receipt.Session == coldWorld.ScriptManualSaves.ExecutionSession),
            "Scheduling success has no genuine event-scoped shared leases.");
        cold.ExecuteClaimedMenu(Key(0x20), 1001, coldHost);
        Require(MainState(cold) is { Executions: 1, Clock.Invocations: 1, Compiled.CompletedDispatches: 2 } &&
            coldQuests.Variable(Key(0x20), 2) == 110, "Claimed MenuMode duplicated a GameMode cadence or ignored its original byte block.");
        coldQuests.SetRunning(Key(0x20), false);
        var stopped = JsonSerializer.Serialize(cold.Capture()); cold.AdvanceClaimed(Key(0x20), 1, coldHost);
        Require(stopped == JsonSerializer.Serialize(cold.Capture()), "Stopped quest advanced or replayed its compiled frame.");

        // Semantic drift/refusal gates execute before values, clocks or messages
        // are restored. Each attempt starts with an unused real owner.
        var main = saved.Instances.Single(value => value.Quest == Key(0x20));
        var authority = main.Compiled!; var frame = authority.Pending!; var cursor = frame.Cursor!;
        foreach (var invalid in new FalloutQuestScriptSnapshot[]
        {
            main with { Compiled = null },
            main with { Compiled = authority with { InitializationOrdinal = authority.InitializationOrdinal + 1 } },
            main with { Compiled = authority with { ProgramSha256 = new string('0', 64) } },
            main with { Compiled = authority with { Pending = frame with { Events = frame.Events.Reverse().ToArray() } } },
            main with { Compiled = authority with { Pending = frame with { Cursor = cursor with { NextOffset = cursor.NextOffset + 1 },
                LastSlice = frame.LastSlice! with { Cursor = cursor with { NextOffset = cursor.NextOffset + 1 } } } } },
            main with { Compiled = authority with { Pending = frame with { Cursor = cursor with { Branches = [] } } } },
            main with { Compiled = authority with { Pending = frame with {
                Cursor = cursor with { Branches = cursor.Branches.Select(branch => branch with { Taken = false }).ToArray() },
                LastSlice = frame.LastSlice! with { Cursor = cursor with {
                    Branches = cursor.Branches.Select(branch => branch with { Taken = false }).ToArray() } } } } },
            main with { Compiled = authority with { Pending = frame with { ClockInvocation = 9 } } },
            main with { Compiled = authority with { Claimed = false } }
        })
        {
            using var refusedWorld = new FalloutReferenceWorld(records);
            var refusedQuests = new FalloutQuestState(records);
            var refused = RecurrenceScheduler(records, refusedQuests, refusedWorld);
            var before = JsonSerializer.Serialize(refused.Capture());
            Reject(() => refused.Restore(saved with { Instances = saved.Instances.Select(item => item.Quest == main.Quest ? invalid : item).ToArray() }));
            Require(before == JsonSerializer.Serialize(refused.Capture()) && refusedQuests.Capture().Count == 0,
                "Compiled semantic/cursor refusal changed the cold graph/stores before preflight.");
        }
        using var legacyWorld = new FalloutReferenceWorld(records);
        var legacyQuests = new FalloutQuestState(records); var legacy = RecurrenceScheduler(records, legacyQuests, legacyWorld);
        Reject(() => legacy.Restore(saved with { CompiledSchedulingVersion = 0 }));
        Reject(() => legacy.Restore(saved with { Instances = saved.Instances.Where(item => item.Quest != Key(0x20)).ToArray() }));
    }

    private static void RecurrenceFailures(FalloutPluginStack records)
    {
        foreach (var fault in new[] { "unknown", "callback", "uncommon-callback", "discard", "tampered-prefix", "reentrant" })
        {
            var quests = new FalloutQuestState(records); quests.SetRunning(Key(0x20), true); quests.SetRunning(Key(0x23), true);
            using var world = new FalloutReferenceWorld(records); var scheduler = RecurrenceScheduler(records, quests, world);
            FalloutQuestScriptHost? host = null; var calls = 0; FalloutQuestStages? actualStages = null;
            var executor = RecurrenceExecutor(records, world, quests, effect =>
            {
                ++calls;
                if (fault is "discard" or "tampered-prefix") { actualStages!.Enter(effect.Target!.Value, effect.Stage); return; }
                if (fault == "uncommon-callback") throw new ApplicationException("actual uncommon callback failure");
                if (fault == "reentrant") scheduler.AdvanceClaimed(Key(0x20), 0, host!);
                throw new InvalidOperationException("actual callback owner failed after its externally committed effect");
            });
            actualStages = new(records, quests, executor.StageSteps, _ => throw new InvalidDataException("Unexpected failure fixture condition."));
            host = RecurrenceHost(executor);
            if (fault == "discard") host = host with { ExecuteCompiledProgram = (owner, program, ordinal, cursor, seconds, gate) =>
            { _ = executor.ExecuteProgram(owner, program, ordinal, cursor, seconds, gate); throw new InvalidOperationException("host discarded actual receipt"); } };
            if (fault == "tampered-prefix") host = host with { ExecuteCompiledProgram = (owner, program, ordinal, cursor, seconds, gate) =>
            {
                var actual = executor.ExecuteProgram(owner, program, ordinal, cursor, seconds, gate);
                // Return genuinely completed this block before its tail. An
                // internal host cannot stamp another unexecuted instruction.
                cursor.Commit(cursor.State.NextOffset, true, [], cursor.State.BudgetSpent + 1);
                return actual with { Cursor = cursor.State };
            } };
            var target = fault == "unknown" ? Key(0x23) : Key(0x20);
            Reject(() => scheduler.AdvanceClaimed(target, 0, host));
            Require(world.ScriptManualSaves.EnteredInvocations == 0, "Compiled callback failure leaked its active invocation.");
            if (fault is "discard" or "tampered-prefix") { Reject(() => scheduler.Capture()); continue; }
            var captured = Copy(scheduler.Capture()); var state = captured.Instances.Single(item => item.Quest == target);
            Require(state is { Executions: 0, Clock.Invocations: 0, Error: not null, Compiled.Pending.LastSlice.Disposition: "closed-failure" } &&
                state.Compiled!.Pending!.Cursor!.CommittedInstructions > 0 && quests.Variable(target, 1) == 1 &&
                (fault != "unknown" || state.Error!.Contains("2f03", StringComparison.Ordinal)),
                "Instruction/callback failure lost its genuine prefix or produced successful completion.");
            var prefix = JsonSerializer.Serialize(captured); var effects = calls;
            Reject(() => scheduler.AdvanceClaimed(target, 0, RecurrenceHost(executor)));
            Require(prefix == JsonSerializer.Serialize(scheduler.Capture()) && calls == effects,
                "Retry replayed a failed callback/instruction prefix.");
            using var coldWorld = new FalloutReferenceWorld(records);
            var coldQuests = new FalloutQuestState(records); coldQuests.Restore(Copy(quests.Capture()));
            var cold = RecurrenceScheduler(records, coldQuests, coldWorld); cold.Restore(captured);
            Reject(() => cold.AdvanceClaimed(target, 0, RecurrenceHost(RecurrenceExecutor(records, coldWorld, coldQuests,
                _ => throw new InvalidDataException("Cold failed prefix replayed a callback.")))));
            Require(prefix == JsonSerializer.Serialize(cold.Capture()), "Cold failure resumed or rewrote its receipt/prefix.");
        }
    }

    private static void RecurrenceAdmission(FalloutPluginStack records)
    {
        foreach (var claimed in new[] { false, true })
        {
            var quests = new FalloutQuestState(records); quests.SetRunning(Key(0x20), true);
            using var world = new FalloutReferenceWorld(records);
            var scheduler = new FalloutQuestScripts(records, quests, claimed ? new HashSet<FalloutFormKey> { Key(0x20) } : [],
                new(), defaultProcessingDelay: 0, references: world);
            var sourceCalls = 0; var host = new FalloutQuestScriptHost((_, _) => () => ++sourceCalls, _ => 0,
                (_, _, _, _) => ++sourceCalls);
            if (claimed) Reject(() => scheduler.AdvanceClaimed(Key(0x20), 0, host));
            else { scheduler.Host = host; scheduler.Advance(0); }
            Require(sourceCalls == 0 && quests.Variable(Key(0x20), 1) == 0 && MainState(scheduler) is
                { Executions: 0, Clock.Invocations: 0, Error: not null, Compiled.Pending.AdmissionError: not null },
                "Missing compiled host invoked SCTX/void fallback or completed a due clock.");
        }
        var sharedQuests = new FalloutQuestState(records); sharedQuests.SetRunning(Key(0x20), true); sharedQuests.SetRunning(Key(0x22), true);
        using var sharedWorld = new FalloutReferenceWorld(records); var shared = RecurrenceScheduler(records, sharedQuests, sharedWorld, 2);
        FalloutQuestStages? sharedStages = null;
        var sharedExecutor = RecurrenceExecutor(records, sharedWorld, sharedQuests,
            effect => sharedStages!.Enter(effect.Target!.Value, effect.Stage));
        sharedStages = new(records, sharedQuests, sharedExecutor.StageSteps, _ => throw new InvalidDataException("Unexpected shared-clock condition."));
        var hostShared = RecurrenceHost(sharedExecutor);
        shared.AdvanceClaimed(Key(0x20), 0, hostShared); shared.AdvanceClaimed(Key(0x22), 0, hostShared);
        var states = shared.Capture().Instances.Where(item => item.Script == Key(0x30)).ToArray();
        Require(states.Length == 2 && states[0].Clock!.HasSameBits(states[1].Clock!) && states.Sum(item => item.Executions) == 1,
            "Compiled quest aliases cloned their original shared SCPT cadence or completed two due invocations.");
    }

    private static void RecurrenceClockTiming(FalloutPluginStack records)
    {
        var quests = new FalloutQuestState(records); quests.SetRunning(Key(0x20), true);
        using var world = new FalloutReferenceWorld(records); var scheduler = RecurrenceScheduler(records, quests, world, 2);
        FalloutQuestStages? stages = null;
        var executor = RecurrenceExecutor(records, world, quests, effect => stages!.Enter(effect.Target!.Value, effect.Stage));
        stages = new(records, quests, executor.StageSteps, _ => throw new InvalidDataException("Unexpected cadence condition."));
        var elapsed = new List<double>();
        var host = RecurrenceHost(executor) with { ExecuteCompiledProgram = (owner, program, ordinal, cursor, seconds, gate) =>
        { elapsed.Add(seconds); return executor.ExecuteProgram(owner, program, ordinal, cursor, seconds, gate); } };
        scheduler.AdvanceClaimed(Key(0x20), 0, host);
        var initial = MainState(scheduler);
        scheduler.AdvanceClaimed(Key(0x20), .25, host);
        Require(MainState(scheduler) is { Executions: 1, Clock.Remaining: .75f, Clock.Elapsed: .25f } && elapsed.Count == 2,
            "Compiled scheduler ran before its authored recurrence or lost Float32 elapsed time.");
        scheduler.AdvanceClaimed(Key(0x20), 2, host);
        Require(MainState(scheduler) is { Executions: 2, Clock.Invocations: 2, Clock.Remaining: -.25f, Clock.Elapsed: 0 } &&
            elapsed.SequenceEqual(new double[] { 0, 0, 2.25, 2.25 }),
            "Compiled recurrence reset countdown debt, fabricated catch-up or changed shared GetSecondsPassed arguments.");
        scheduler.AdvanceClaimed(Key(0x20), .125, host);
        Require(MainState(scheduler) is { Executions: 3, Clock.Invocations: 3, Clock.Remaining: .75f } &&
            elapsed.TakeLast(2).SequenceEqual(new double[] { .125, .125 }),
            "Overdue compiled invocation skipped the next real frame elapsed time or finished multiple cadences.");
        var initialization = new FalloutQuestScriptInitialization(records, 2);
        Require(initial.Compiled!.InitializationOrdinal == initialization.Definitions[Key(0x30)].InitializationOrdinal &&
            initial.Compiled.InitialPhaseBits == unchecked((uint)BitConverter.SingleToInt32Bits(initialization.Definitions[Key(0x30)].InitialPhase)),
            "Compiled support selected a different registration denominator/phase.");
    }

    private static void RecurrenceMenuAndSource(FalloutPluginStack records)
    {
        var quests = new FalloutQuestState(records); quests.SetRunning(Key(0x20), true);
        using var world = new FalloutReferenceWorld(records); var scheduler = RecurrenceScheduler(records, quests, world);
        var executor = RecurrenceExecutor(records, world, quests, _ => throw new InvalidDataException("Menu block reached an unrelated effect."));
        var calls = 0; scheduler.ExecuteClaimedMenu(Key(0x20), 1001, RecurrenceHost(executor, _ => calls++ < 2));
        var saved = Copy(scheduler.Capture());
        Require(MainState(scheduler) is { Clock.Invocations: 0, Compiled.Pending.GameMode: false, Compiled.Pending.Cadenced: false } &&
            quests.Variable(Key(0x20), 2) == 100 && quests.Variable(Key(0x20), 3) == 0,
            "Actual menu interruption consumed its suffix or a GameMode cadence.");
        using var coldWorld = new FalloutReferenceWorld(records); var coldQuests = new FalloutQuestState(records); coldQuests.Restore(Copy(quests.Capture()));
        var cold = RecurrenceScheduler(records, coldQuests, coldWorld); cold.Restore(saved);
        var host = RecurrenceHost(RecurrenceExecutor(records, coldWorld, coldQuests, _ => throw new InvalidDataException("Cold menu called a foreign owner.")));
        cold.ExecuteClaimedMenu(Key(0x20), 1002, host); cold.AdvanceClaimed(Key(0x20), 100, host);
        Require(JsonSerializer.Serialize(saved) == JsonSerializer.Serialize(cold.Capture()), "Cold menu resumed under a different actual menu/game frame.");
        cold.ExecuteClaimedMenu(Key(0x20), 1001, host);
        Require(MainState(cold) is { Executions: 0, Clock.Invocations: 0, Compiled.CompletedDispatches: 1, Compiled.Pending: null } &&
            coldQuests.Variable(Key(0x20), 2) == 100 && coldQuests.Variable(Key(0x20), 3) == 1,
            "Cold same-menu suffix replayed its prefix or completed a gameplay clock.");
        var observationQuests = new FalloutQuestState(records); observationQuests.SetRunning(Key(0x20), true);
        using var observationWorld = new FalloutReferenceWorld(records);
        var observation = new FalloutQuestScripts(records, observationQuests, new HashSet<FalloutFormKey>(), new(), defaultProcessingDelay: 2, references: observationWorld);
        var untouched = JsonSerializer.Serialize(observation.Capture()); observation.Advance(100, execute: false);
        Require(untouched == JsonSerializer.Serialize(observation.Capture()), "A void observational frame created compiled completion/cursor authority.");
        foreach (var shared in new[] { false, true })
        {
            var sourceQuests = new FalloutQuestState(records); sourceQuests.SetRunning(Key(0x25), true);
            using var sourceWorld = new FalloutReferenceWorld(records); var source = RecurrenceScheduler(records, sourceQuests, sourceWorld);
            var sourceExecutor = RecurrenceExecutor(records, sourceWorld, sourceQuests, _ => throw new InvalidDataException("Unexpected diagnostic effect."));
            var sourceHost = new FalloutQuestScriptHost((_, _) => throw new InvalidDataException("Unexpected source stage."), _ => 0,
                ExecuteProgram: shared ? sourceExecutor.ExecuteProgram : null);
            source.AdvanceClaimed(Key(0x25), 0, sourceHost); source.ExecuteClaimedMenu(Key(0x25), 1001, sourceHost);
            Require(sourceQuests.Variable(Key(0x25), 1) == 3 && source.Capture().Instances.Single(state => state.Quest == Key(0x25)) is
                { Executions: 1, Clock.Invocations: 1, Compiled: null }, "Source-only fallback/shared execution drifted after binary recurrence integration.");
        }
    }

    private static void RecurrenceManualSaveLease(FalloutPluginStack records, string directory)
    {
        var quests = new FalloutQuestState(records); quests.SetRunning(Key(0x24), true);
        using var world = new FalloutReferenceWorld(records); var scheduler = RecurrenceScheduler(records, quests, world);
        var writerCalls = 0;
        world.ScriptManualSaves.Bind(_ => { ++writerCalls; throw new InvalidDataException("Unfinished request reached writer."); },
            _ => throw new InvalidDataException("Actual pending request failed unexpectedly."),
            binding: new("authored-compiled-recurrence", Path.Combine(directory, "continue.json"),
                id => Path.Combine(directory, "slots", id.ToString("N") + ".json")));
        var executor = RecurrenceExecutor(records, world, quests, _ => throw new InvalidDataException("Unexpected ForceSave fixture effect."));
        var calls = 0; scheduler.AdvanceClaimed(Key(0x24), 0, RecurrenceHost(executor, _ => calls++ < 3));
        Require(quests.Variable(Key(0x24), 1) == 1 && quests.Variable(Key(0x24), 2) == 0 &&
            world.ScriptManualSaves.Receipt is { Disposition: "pending", Sites.Count: 1, Invocations.Count: 1 } receipt &&
            receipt.Sites[0].Authority == FalloutScriptResultAuthority.CompiledVanilla &&
            !receipt.Invocations![0].Ended && world.ScriptManualSaves.EnteredInvocations == 0,
            "Suspending ForceSave guessed completion, lost its exact source site or retained a physical lease.");
        world.ScriptManualSaves.RequireCapture();
        Require(scheduler.Capture().Instances.Single(state => state.Quest == Key(0x24)).Compiled?.Pending is not null &&
            world.ScriptManualSaves.Order.Capture().CapturedOrder is null,
            "A suspended request lost its capturable cursor or invented a published writer.");
        world.ScriptManualSaves.AdvancePhase();
        Require(!world.ScriptManualSaves.Drain(() => null) && writerCalls == 0 && world.ScriptManualSaves.DeferredBy == "source-script-execution",
            "Suspended ForceSave wrote before its original suffix actually ended.");
        scheduler.AdvanceClaimed(Key(0x24), 0, RecurrenceHost(executor));
        Require(quests.Variable(Key(0x24), 1) == 1 && quests.Variable(Key(0x24), 2) == 1 &&
            world.ScriptManualSaves.Receipt is { Disposition: "pending", Sites.Count: 1 } ended &&
            ended.Invocations!.All(invocation => invocation.Ended && invocation.SourceError is null) && writerCalls == 0,
            "Resuming the genuine ForceSave suffix replayed its request/prefix or failed to retire its original invocation.");
        Require(scheduler.Capture().Instances.Single(state => state.Quest == Key(0x24)).Compiled?.Pending is null &&
            world.ScriptManualSaves.Order.Capture().CapturedOrder is null,
            "Retired scheduling retained a live suffix or invented a persistent writer.");
    }

    private static void RecurrenceUnsupported(string directory)
    {
        foreach (var code in new[] { Join(Instruction(0x1d), Block(37, Set('f', 1, " 1"))),
            Join(Instruction(0x1d), Block(0, Instruction(0x16, Join(U16(1), U16(2), new byte[] { 32, 49 })))),
            Join(Instruction(0x1d), Instruction(0x10, Join(U16(1), U32(4), U16(1), new byte[] { (byte)'n' }, U32(1001))), Instruction(0x11)) })
        {
            var selected = Path.Combine(directory, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(selected);
            File.WriteAllBytes(Path.Combine(selected, "Bytecode.esm"), RecurrenceFixture(null, code));
            using var records = FalloutPluginStack.Load(selected, ["Bytecode.esm"]);
            var quests = new FalloutQuestState(records); using var world = new FalloutReferenceWorld(records);
            var scheduler = RecurrenceScheduler(records, quests, world);
            Reject(() => scheduler.RequireQuestExecution(Key(0x20)));
            Require(quests.Capture().Count == 0 && scheduler.Capture().Instances.All(item => item.Quest != Key(0x20)),
                "Unsupported block/filter/structural frame was hidden or allocated an executable scheduler owner.");
        }
    }

    private static byte[] RecurrenceFixture(string? source, byte[]? selectedCode = null)
    {
        var variable = Join(new byte[] { (byte)'f' }, U16(1));
        var predicate = Join(new byte[] { 32 }, variable, new byte[] { 32, 48, 32, 61, 61 });
        var positive = Join(Instruction(0x1d), Block(0, Join(
            Instruction(0x16, Join(U16(2), U16((ushort)predicate.Length), predicate)),
            RecurrenceAdd(1, 1), RecurrenceAdd(2, 10), Instruction(0x17, U16(1)), Set('f', 2, " 999"), Instruction(0x19),
            Call(0x1039, Join(U16(2), Form(1), Integer(10))), Instruction(0x1e), Set('f', 1, " 999"))),
            Block(0, Set('s', 4, " 1")), Block(1, Join(RecurrenceAdd(2, 100), RecurrenceAdd(3, 1))));
        var code = selectedCode ?? positive;
        var fields = new List<byte[]> { Field("SCHR", Header(code, 1, 4, 1)), Field("SCDA", code),
            Local(1, "value"), Local(2, "suffix"), Local(3, "stage"), Local(4, "integral", true), Field("SCRO", U32(0x21)) };
        if (source is not null) fields.Add(Field("SCTX", Text(source)));
        if (source == "duplicate") fields.Add(Field("SCTX", Text(source)));
        var stage = Set('f', 3, " 7", 1);
        var data = new byte[8]; System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(4), 1);
        return Join(Tes4(),
            Record("QUST", 0x20, Field("DATA", data), Field("SCRI", U32(0x30))),
            Record("QUST", 0x21, Field("DATA", data), Field("SCRI", U32(0x31)), Field("INDX", U16(10)), Field("QSDT", new byte[] { 0 }),
                Field("SCHR", Header(stage, 1)), Field("SCDA", stage), Field("SCRO", U32(0x20))),
            Record("QUST", 0x22, Field("DATA", data), Field("SCRI", U32(0x30))),
            Record("QUST", 0x23, Field("DATA", data), Field("SCRI", U32(0x32))),
            Record("QUST", 0x24, Field("DATA", data), Field("SCRI", U32(0x33))),
            Record("QUST", 0x25, Field("DATA", data), Field("SCRI", U32(0x34))),
            Record("SCPT", 0x30, fields.ToArray()), Script(0x31, Instruction(0x1d), [], quest: true),
            Script(0x32, Join(Instruction(0x1d), Block(0, Join(RecurrenceAdd(1, 1), Instruction(0x2f03), Set('f', 2, " 999")))), [], quest: true),
            Script(0x33, Join(Instruction(0x1d), Block(0, Join(RecurrenceAdd(1, 1), Call(0x1217, new byte[] { 0, 0 }),
                RecurrenceAdd(2, 1)))), [], quest: true),
            Record("SCPT", 0x34, Field("SCHR", Header([], 0, 1, 1)), Local(1, "value"),
                Field("SCTX", Text("float value\nbegin GameMode\nset value to value + 1\nend\nbegin MenuMode\nset value to value + 2\nend"))));
    }
    private static byte[] RecurrenceAdd(ushort slot, int value)
    {
        var expression = Join(new byte[] { 32, (byte)'f' }, U16(slot), System.Text.Encoding.ASCII.GetBytes(" " + value + " +"));
        return Instruction(0x15, Join(new byte[] { (byte)'f' }, U16(slot), U16((ushort)expression.Length), expression));
    }
}
