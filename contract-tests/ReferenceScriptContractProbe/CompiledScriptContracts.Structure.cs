using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static partial class CompiledScriptContracts
{
    internal static void RunCompiledStructure()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-compiled-structure-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            StructuralBaseFrames(directory);
            StructuralColdFlags(directory);
            StructuralUnknownPrefix(directory);
            StructuralBoundaryPrefix(directory);
            StructuralMenuFilters(directory);
            StructuralMalformed(directory);
            StructuralMasterOverride(directory);
            QualifiedSet(directory);
            AdditionalResultScopes(directory);
            SceneComplexityCommand(directory);
        }
        finally { Directory.Delete(directory, recursive: true); }
        Console.WriteLine("OPENNV_COMPILED_STRUCTURE_PASS originalFrames=true sourceSkips=true baseFlags=true coldDecisions=true menuIdentity=true unknownPrefix=true");
    }

    private static void StructuralOwnedFixture(string directory, byte[] code, Action<FalloutPluginStack> run)
    {
        var selected = Path.Combine(directory, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(selected);
        var path = Path.Combine(selected, "Bytecode.esm");
        File.WriteAllBytes(path, RecurrenceFixture("Contradictory diagnostic text must not select instructions.", code));
        var hash = SHA256.HashData(File.ReadAllBytes(path));
        using (var records = FalloutPluginStack.Load(selected, ["Bytecode.esm"])) run(records);
        Require(hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(path))), "Structural full-reader original input changed.");
    }

    private static byte[] StructuralStandalone(params byte[][] blocks) => Join(Instruction(0x1d), Join(blocks));
    private static byte[] StructuralBranch(ushort opcode, short skip, string predicate) =>
        Branch(opcode, unchecked((ushort)skip), Ascii(predicate));
    private static byte[] StructuralMenu(int code, byte[] body) => Join(
        Instruction(0x10, Join(U16(1), U32(checked((uint)body.Length + 4)), U16(1), [(byte)'n'], U32(unchecked((uint)code)))),
        body, Instruction(0x11));
    private static byte[] StructuralAdd(char type, ushort slot, int value)
    {
        var expression = Join(new byte[] { 32, (byte)type }, U16(slot), Ascii(" " + value + " +"));
        return Instruction(0x15, Join(new byte[] { (byte)type }, U16(slot), U16((ushort)expression.Length), expression));
    }
    private static FalloutReferenceScripts StructuralExecutor(FalloutPluginStack records, FalloutReferenceWorld world,
        FalloutQuestState quests) => RecurrenceExecutor(records, world, quests,
            _ => throw new InvalidDataException("Structural fixture entered an unrelated external effect."));

    private static void StructuralBaseFrames(string directory)
    {
        var body = Join(Instruction(0x19), StructuralBranch(0x16, 1, " 1"), Set('f', 1, " 10"),
            Instruction(0x19), Instruction(0x19), StructuralBranch(0x18, 1, " 0"), Set('f', 2, " 999"),
            Instruction(0x19), StructuralBranch(0x18, 1, " 1"), Set('f', 2, " 20"), Instruction(0x19),
            Instruction(0x17, U16(1)), Set('f', 3, " 30"), Instruction(0x19),
            StructuralBranch(0x16, 1, " 0"), Set('s', 4, " 999"), StructuralBranch(0x18, 1, " 1"),
            Set('s', 4, " 40"), Instruction(0x17, U16(1)), Set('s', 4, " 999"), Instruction(0x19));
        StructuralOwnedFixture(directory, StructuralStandalone(Block(0, body)), records =>
        {
            var quests = new FalloutQuestState(records); quests.SetRunning(Key(0x20), true);
            using var world = new FalloutReferenceWorld(records);
            var scheduler = RecurrenceScheduler(records, quests, world);
            scheduler.AdvanceClaimed(Key(0x20), 0, RecurrenceHost(StructuralExecutor(records, world, quests)));
            Require(quests.Variable(Key(0x20), 1) == 10 && quests.Variable(Key(0x20), 2) == 20 &&
                quests.Variable(Key(0x20), 3) == 30 && quests.Variable(Key(0x20), 4) == 40 &&
                MainState(scheduler) is { Error: null, Executions: 1, Compiled.Pending: null },
                "Source base-slot clear/alternative/signed statement skips differ from actual shared execution.");
            var completed = MainState(scheduler).Compiled!.LastCompletedEvents.Single().Cursor;
            Require(completed.ControlHistory is { Count: 14 } && completed.Branches.Count == 0,
                "Source branch control decisions or real marker commits were discarded.");
        });
    }

    private static void StructuralColdFlags(string directory)
    {
        var predicate = Join(Ascii(" "), [(byte)'f'], U16(1), Ascii(" 0 =="));
        var body = Join(Branch(0x16, 1, predicate), RecurrenceAdd(1, 1), StructuralBranch(0x18, 1, " 1"),
            RecurrenceAdd(1, 100), Instruction(0x17, U16(1)), RecurrenceAdd(1, 1000), Instruction(0x19), Instruction(0x19));
        StructuralOwnedFixture(directory, StructuralStandalone(Block(0, body)), records =>
        {
            var quests = new FalloutQuestState(records); quests.SetRunning(Key(0x20), true);
            using var world = new FalloutReferenceWorld(records);
            var scheduler = RecurrenceScheduler(records, quests, world);
            scheduler.AdvanceClaimed(Key(0x20), 0, RecurrenceHost(StructuralExecutor(records, world, quests),
                _ => quests.Variable(Key(0x20), 1) == 0));
            var saved = Copy(scheduler.Capture()); var state = MainState(scheduler);
            Require(state.Compiled?.Pending?.Cursor is { CommittedInstructions: 2, Branches.Count: 1,
                ControlHistory.Count: 1 } && quests.Variable(Key(0x20), 1) == 1,
                "Actual compiled suspension did not retain its taken base flag and source decision.");
            using var coldWorld = new FalloutReferenceWorld(records);
            var coldQuests = new FalloutQuestState(records); coldQuests.Restore(Copy(quests.Capture()));
            var cold = RecurrenceScheduler(records, coldQuests, coldWorld); cold.Restore(saved);
            cold.AdvanceClaimed(Key(0x20), 0, RecurrenceHost(StructuralExecutor(records, coldWorld, coldQuests)));
            Require(coldQuests.Variable(Key(0x20), 1) == 1 && MainState(cold) is { Error: null, Compiled.Pending: null },
                "Cold branch resumed through re-evaluation, skipped marker effects or a replayed alternative.");
            var program = FalloutQuestScriptAuthority.ReadCompiled(records.GetEffective(Key(0x30)))!;
            var flow = FalloutCompiledControlFlow.Read(program.EventInstructions(program.Events[0]), program.Events[0].End);
            var cursor = state.Compiled!.Pending!.Cursor!;
            Reject(() => flow.ValidateCursor(cursor with { ControlHistory = null }));
            Reject(() => flow.ValidateCursor(cursor with { ControlHistory = [] }));
            Reject(() => flow.ValidateCursor(cursor with { Branches = cursor.Branches.Select(frame => frame with { HasIf = false }).ToArray() }));
            Reject(() => flow.ValidateCursor(cursor with { ControlHistory = cursor.ControlHistory!.Select(decision => decision with { Predicate = false }).ToArray() }));
            Reject(() => flow.ValidateCursor(cursor with { NextOffset = cursor.NextOffset + 1 }));
        });
    }

    private static void StructuralUnknownPrefix(string directory)
    {
        var body = Join(Instruction(0x19), Set('f', 1, " 7"), Instruction(0x3800), Set('f', 2, " 99"));
        StructuralOwnedFixture(directory, StructuralStandalone(Block(0, body)), records =>
        {
            var quests = new FalloutQuestState(records); quests.SetRunning(Key(0x20), true);
            using var world = new FalloutReferenceWorld(records);
            var scheduler = RecurrenceScheduler(records, quests, world);
            var host = RecurrenceHost(StructuralExecutor(records, world, quests));
            Reject(() => scheduler.AdvanceClaimed(Key(0x20), 0, host));
            var saved = Copy(scheduler.Capture()); var state = MainState(scheduler);
            Require(state.Error?.Contains("3800", StringComparison.Ordinal) == true &&
                state.Compiled!.Pending!.Cursor!.CommittedInstructions == 2 &&
                state.Compiled.Pending.Cursor.ControlHistory is { Count: 1 } &&
                quests.Variable(Key(0x20), 1) == 7 && quests.Variable(Key(0x20), 2) == 0,
                "Unknown command after a base ENDIF lost the real prefix or became successful.");
            var before = JsonSerializer.Serialize(saved);
            Reject(() => scheduler.AdvanceClaimed(Key(0x20), 0, host));
            Require(JsonSerializer.Serialize(scheduler.Capture()) == before, "Failed structural prefix replayed or changed.");
        });
    }

    private static void StructuralBoundaryPrefix(string directory)
    {
        var body = Join(StructuralBranch(0x16, 0, " 1"), Set('f', 1, " 5"));
        StructuralOwnedFixture(directory, StructuralStandalone(Block(0, body)), records =>
        {
            var quests = new FalloutQuestState(records); quests.SetRunning(Key(0x20), true);
            using var world = new FalloutReferenceWorld(records);
            var scheduler = RecurrenceScheduler(records, quests, world);
            Reject(() => scheduler.AdvanceClaimed(Key(0x20), 0,
                RecurrenceHost(StructuralExecutor(records, world, quests))));
            var saved = Copy(scheduler.Capture()); var state = MainState(scheduler);
            Require(state.Error?.Contains("cross-event", StringComparison.Ordinal) == true &&
                state.Compiled!.Pending!.Cursor is { Completed: false, CommittedInstructions: 2, Branches.Count: 1 } &&
                quests.Variable(Key(0x20), 1) == 5 && state.Executions == 0,
                "Unowned cross-event boundary discarded its returned last command or manufactured completion.");
            using var coldWorld = new FalloutReferenceWorld(records);
            var coldQuests = new FalloutQuestState(records); coldQuests.Restore(Copy(quests.Capture()));
            var cold = RecurrenceScheduler(records, coldQuests, coldWorld); cold.Restore(saved);
            Require(coldQuests.Variable(Key(0x20), 1) == 5 && JsonSerializer.Serialize(cold.Capture()) == JsonSerializer.Serialize(saved),
                "Cold failed boundary replayed the committed command or dropped source branch flags.");
        });
    }

    private static void StructuralMenuFilters(string directory)
    {
        var code = StructuralStandalone(Block(0, StructuralAdd('s', 4, 1)), StructuralMenu(0, RecurrenceAdd(3, 1)),
            StructuralMenu(1001, RecurrenceAdd(1, 10)), StructuralMenu(2, RecurrenceAdd(2, 20)),
            StructuralMenu(1002, StructuralAdd('s', 4, 99)), StructuralMenu(-1, StructuralAdd('s', 4, 500)));
        StructuralOwnedFixture(directory, code, records =>
        {
            var quests = new FalloutQuestState(records); quests.SetRunning(Key(0x20), true);
            using var world = new FalloutReferenceWorld(records);
            var scheduler = RecurrenceScheduler(records, quests, world);
            var host = RecurrenceHost(StructuralExecutor(records, world, quests));
            scheduler.ExecuteClaimedMenu(Key(0x20), 1001, host);
            var first = MainState(scheduler);
            Require(quests.Variable(Key(0x20), 1) == 10 && quests.Variable(Key(0x20), 2) == 20 &&
                quests.Variable(Key(0x20), 3) == 1 && quests.Variable(Key(0x20), 4) == 0 &&
                first.Compiled!.LastCompletedEvents.Select(receipt => receipt.EventOrdinal).SequenceEqual(new[] { 1, 2, 3 }) &&
                first.Compiled.LastCompletedMenus!.SequenceEqual(new uint[] { 2, 1001 }),
                "Numeric event filters did not consume the actual specific/category menu frame and source order.");
            var saved = Copy(scheduler.Capture());
            using var coldWorld = new FalloutReferenceWorld(records);
            var coldQuests = new FalloutQuestState(records); coldQuests.Restore(Copy(quests.Capture()));
            var cold = RecurrenceScheduler(records, coldQuests, coldWorld); cold.Restore(saved);
            cold.ExecuteClaimedMenu(Key(0x20), 1002, RecurrenceHost(StructuralExecutor(records, coldWorld, coldQuests)));
            Require(coldQuests.Variable(Key(0x20), 1) == 10 && coldQuests.Variable(Key(0x20), 2) == 20 &&
                coldQuests.Variable(Key(0x20), 3) == 2 && coldQuests.Variable(Key(0x20), 4) == 99,
                "Cold event filter fabricated readiness or replayed another menu's body.");
            var program = FalloutQuestScriptAuthority.ReadCompiled(records.GetEffective(Key(0x30)))!;
            var missing = new FalloutScriptMenus(); missing.Publish(false);
            Reject(() => FalloutCompiledRecurringEventFilter.Read(program.Events[2]).Matches(missing));
            var last = first.Compiled;
            var drift = saved with { Instances = saved.Instances.Select(row => row.Quest != Key(0x20) ? row :
                row with { Compiled = last! with { LastCompletedMenus = [1, 1002] } }).ToArray() };
            using var refusedWorld = new FalloutReferenceWorld(records);
            var refused = RecurrenceScheduler(records, new(records), refusedWorld);
            Reject(() => refused.Restore(drift));
        });
    }

    private static void StructuralMalformed(string directory)
    {
        foreach (var body in new[] { Instruction(0x19, [1]), Instruction(0x17, Join(U16(0), [1])),
            Instruction(0x16, Join(U16(1), U16(50), Ascii(" 1"))), StructuralBranch(0x18, -1, " 1"),
            StructuralBranch(0x16, 2, " 1") })
            StructuralOwnedFixture(directory, StructuralStandalone(Block(0, body)), records =>
            {
                var program = FalloutQuestScriptAuthority.ReadCompiled(records.GetEffective(Key(0x30)))!;
                Reject(() => FalloutQuestScriptAuthority.RequireRecurringProgram(program));
            });
        StructuralOwnedFixture(directory, StructuralStandalone(Join(
            Instruction(0x10, Join(U16(1), U32(4), U16(1), [(byte)'f'], U16(1))), Instruction(0x11))), records =>
            Reject(() => FalloutQuestScriptAuthority.RequireRecurringProgram(
                FalloutQuestScriptAuthority.ReadCompiled(records.GetEffective(Key(0x30)))!)));
        // A source-declared skip may name an ordinary statement. It is not
        // rewritten to an ELSE/ENDIF inferred from diagnostic text.
        var bodyCode = Join(StructuralBranch(0x16, 1, " 0"), Set('f', 1, " 999"),
            Set('f', 2, " 9"), Instruction(0x19));
        StructuralOwnedFixture(directory, StructuralStandalone(Block(0, bodyCode)), records =>
        {
            var quests = new FalloutQuestState(records); quests.SetRunning(Key(0x20), true);
            using var world = new FalloutReferenceWorld(records); var scheduler = RecurrenceScheduler(records, quests, world);
            scheduler.AdvanceClaimed(Key(0x20), 0, RecurrenceHost(StructuralExecutor(records, world, quests)));
            Require(quests.Variable(Key(0x20), 1) == 0 && quests.Variable(Key(0x20), 2) == 9,
                "Source statement target was replaced by a lexical matching marker.");
        });
    }

    private static void StructuralMasterOverride(string directory)
    {
        var selected = Path.Combine(directory, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(selected);
        var beforeCode = StructuralStandalone(Block(0, Join(Instruction(0x19), Set('f', 1, " 1"))));
        var afterCode = StructuralStandalone(StructuralMenu(1001, Join(Instruction(0x19), Set('f', 1, " 2"))));
        File.WriteAllBytes(Path.Combine(selected, "Bytecode.esm"), RecurrenceFixture(null, beforeCode));
        using var original = FalloutPluginStack.Load(selected, ["Bytecode.esm"]);
        var old = FalloutQuestScriptAuthority.ReadCompiled(original.GetEffective(Key(0x30)))!;
        File.WriteAllBytes(Path.Combine(selected, "Override.esp"), Join(Tes4("Bytecode.esm"),
            Record("SCPT", 0x30, Field("SCHR", Header(afterCode, 0, 4, 1)), Field("SCDA", afterCode),
                Local(1, "value"), Local(2, "suffix"), Local(3, "stage"), Local(4, "integral", true))));
        using var winning = FalloutPluginStack.Load(selected, ["Bytecode.esm", "Override.esp"]);
        var replacement = FalloutQuestScriptAuthority.ReadCompiled(winning.GetEffective(Key(0x30)))!;
        Require(replacement.Source.FormKey == old.Source.FormKey && replacement.Source.Plugin.Name == "Override.esp" &&
            replacement.ProgramSha256 != old.ProgramSha256 && replacement.Scope.RecordSha256 != old.Scope.RecordSha256,
            "Winning MAST identity or changed compiled/filter source hashes were replaced by an unrelated script.");
        FalloutQuestScriptAuthority.RequireRecurringProgram(replacement);
    }
}
