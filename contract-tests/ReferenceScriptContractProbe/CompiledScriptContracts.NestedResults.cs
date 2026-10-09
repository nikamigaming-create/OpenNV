using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class CompiledScriptContracts
{
    // Synthetic binary full-reader fixtures. All IDs/bytes below are generated
    // first-party inputs. The structure covers nested result/query semantics;
    // it is not a copy of an owned quest or an inferred detection producer.
    internal static void NestedResults()
    {
        TypedPostfixQuery();
        NumericDomains();
        var directory = Path.Combine(Path.GetTempPath(), "opennv-compiled-nested-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            foreach (var source in new[] { false, true })
            {
                var selected = Path.Combine(directory, source ? "diagnostic-source" : "source-removed");
                Directory.CreateDirectory(selected);
                var path = Path.Combine(selected, "Bytecode.esm");
                File.WriteAllBytes(path, NestedFixture(source));
                var hash = SHA256.HashData(File.ReadAllBytes(path));
                using var records = FalloutPluginStack.Load(selected, ["Bytecode.esm"]);
                using var world = new FalloutReferenceWorld(records);
                var quests = new FalloutQuestState(records); var globals = FalloutGlobalState.Read(records);
                var effects = new List<FalloutReferenceScriptEffect>();
                FalloutQuestStages? stages = null;
                var scripts = new FalloutReferenceScripts(records, world, quests, new((_, _) => false, effect =>
                {
                    effects.Add(effect);
                    if (effect.Kind == FalloutReferenceEffectKind.SetStage) stages!.Enter(effect.Target!.Value, effect.Stage);
                    else Require(effect.Kind is FalloutReferenceEffectKind.HeadTracking or FalloutReferenceEffectKind.EvaluatePackages,
                        "Compiled nested fixture reached an unexpected effect.");
                }, Globals: globals, Command: (_, _, _, _) => throw new InvalidDataException("Binary result used a source command fallback.")));
                stages = new(records, quests, scripts.StageSteps,
                    _ => throw new InvalidDataException("Synthetic compiled result invented entry conditions."));
                stages.Enter(Key(0x20), 0);
                Require(quests.Variable(Key(0x20), 1) == 5 && quests.Variable(Key(0x20), 2) == 11 &&
                    globals.Get(Key(0x50)) == 5.5f && effects.Count == 0,
                    "Source-removed nested branch, postfix arithmetic, global or skipped alternative changed.");
                Require(stages.CaptureResults().Single().Completed && world.ScriptManualSaves.EnteredInvocations == 0,
                    "Compiled branch result did not close its actual invocation lease.");

                world.SetActorAlert(Key(0x901), 1);
                quests.SetVariable(Key(0x20), 1, 9); quests.SetVariable(Key(0x20), 2, 0);
                var random = JsonSerializer.Serialize(world.ScriptValues.Capture());
                var fault = Failure(() => stages.Enter(Key(0x20), 120));
                Require(fault.Contains("102d", StringComparison.Ordinal) && fault.Contains("SCDA offset 35", StringComparison.Ordinal) &&
                    fault.Contains("SCDA offset 56", StringComparison.Ordinal) &&
                    quests.Variable(Key(0x20), 1) == 0 && quests.Variable(Key(0x20), 2) == 0 &&
                    !world.ActorAlerted(Key(0x901)) &&
                    effects.Count == 4 && effects[0] is { Kind: FalloutReferenceEffectKind.SetStage, Stage: 38 } &&
                    effects[1] is { Kind: FalloutReferenceEffectKind.HeadTracking, Argument: null } &&
                    effects[1].Target == Key(0x901) && effects[2].Target == Key(0x901) && effects[3].Target == Key(0x902) &&
                    effects[2].Kind == FalloutReferenceEffectKind.EvaluatePackages && effects[3].Kind == FalloutReferenceEffectKind.EvaluatePackages,
                    "Reached unowned detection query lost its exact binary cursor or replayed/omitted consumed effects.");
                var quest = quests.Capture().Single();
                var objectives = quest.Objectives ?? throw new InvalidDataException("Binary quest result lost its objective owner.");
                Require(objectives.Single(item => item.Index == 40).Completed &&
                    objectives.Single(item => item.Index == 60).Completed && objectives.Single(item => item.Index == 70).Displayed,
                    "Outer binary stage lost its three committed objective writes.");
                var journal = Copy(stages.CaptureResults().ToArray());
                Require(journal.Single(item => item.Stage == 38) is { Completed: false, Steps: 5 } &&
                    journal.Single(item => item.Stage == 120) is { Completed: false, Steps: 4 } &&
                    journal.Where(item => item.Stage != 0).All(item => item.Error == fault || item.Error!.Contains("102d", StringComparison.Ordinal)) &&
                    !stages.HasPendingResults && world.ScriptManualSaves.EnteredInvocations == 0 &&
                    random == JsonSerializer.Serialize(world.ScriptValues.Capture()),
                    "Binary nested failure invented pending work, source progress or random effects.");
                var references = Copy(world.Capture().ToArray());
                var overrides = Copy(world.CaptureActorOverrides().ToArray()); var savedQuests = Copy(quests.Capture().ToArray());
                var effectsAfterFailure = effects.Count;
                Require(Failure(() => stages.Enter(Key(0x20), 120)) == fault && effects.Count == effectsAfterFailure,
                    "A repeated stage input replayed its failed binary prefix.");
                using var coldWorld = new FalloutReferenceWorld(records); coldWorld.Restore(references); coldWorld.RestoreActorOverrides(overrides);
                var coldQuests = new FalloutQuestState(records); coldQuests.Restore(savedQuests);
                var coldCalls = 0;
                var coldScripts = new FalloutReferenceScripts(records, coldWorld, coldQuests, new((_, _) => false, _ => ++coldCalls));
                var coldStages = new FalloutQuestStages(records, coldQuests, coldScripts.StageSteps,
                    _ => throw new InvalidDataException("Cold closed binary stage replayed a condition."));
                coldStages.RestoreResults(journal);
                Require(Failure(() => coldStages.Enter(Key(0x20), 120)) == fault && coldCalls == 0 &&
                    JsonSerializer.Serialize(journal) == JsonSerializer.Serialize(coldStages.CaptureResults()) &&
                    JsonSerializer.Serialize(savedQuests) == JsonSerializer.Serialize(coldQuests.Capture()) &&
                    !coldWorld.ActorAlerted(Key(0x901)),
                    "Cold closed binary failure replayed prefix, query, objective or native effects.");
                foreach (var stage in new short[] { 201, 202, 203, 204 })
                    Require(Failure(() => stages.Enter(Key(0x20), stage)).Contains(stage switch
                    {
                        201 => "1539", 202 => "153b", 203 => "153c", _ => "3800"
                    }, StringComparison.Ordinal), "Reached extension instruction was skipped or executed from SCTX.");
                Require(hash.AsSpan().SequenceEqual(SHA256.HashData(File.ReadAllBytes(path))), "Compiled nested fixture mutated its binary input.");
            }
        }
        finally { Directory.Delete(directory, true); }
        Console.WriteLine("OPENNV_COMPILED_NESTED_RESULT_CONTRACT_PASS syntheticFullReader=true sourceRemoved=true " +
            "contradictorySourceIgnored=true realQuestQueryAndObjectives=true statementCountBranches=true " +
            "nestedClosedPrefixCold=true typedDetectionOperandOnly=true actualDetectionUnbound=true " +
            "letWhileLoop3800Refused=true noSourceFallback=true campaign=unverified");
    }

    private static void TypedPostfixQuery()
    {
        foreach (var result in new[] { 0d, 1d })
        {
            var budget = new FalloutScriptExecutionBudget(); var calls = 0;
            var operands = new FalloutCompiledOperandContext(index => FalloutScriptValue.Form(index == 5 ? 0x14u : 0x903u),
                _ => throw new InvalidDataException("Typed detection expression read an unrelated local."),
                _ => throw new InvalidDataException("Typed detection expression read an unrelated global."), command =>
                {
                    ++calls;
                    Require(command.Opcode == 0x102d && command.Receiver == 3 && command.Arguments.Count == 1 &&
                        command.Arguments[0] is { SourceParameterType: 6 } argument && argument.Value.Kind == FalloutScriptValueKind.Form &&
                        argument.Value.Number == 0x14, "Binary GetDetected receiver/Actor operand differs from the decoded table.");
                    // Isolated decoder fixture owns only this already-supplied
                    // query value. It provides no process/cache/sensory producer.
                    return result;
                }, budget);
            var expression = Join(Query(0x102d, Join(U16(1), Form(5)), 3), Ascii(" 0 >"));
            Require(FalloutCompiledOperands.Expression(expression, operands).Number == result && calls == 1,
                "Binary postfix predicate changed the typed query or its numeric comparison.");
            var eager = Join(Ascii(" 0 "), Query(0x102d, Join(U16(1), Form(5)), 3), Ascii(" &&"));
            Require(FalloutCompiledOperands.Expression(eager, operands).Number == 0 && calls == 2,
                "Postfix logical evaluation incorrectly skipped an encoded query operand.");
        }
    }

    private static void NumericDomains()
    {
        FalloutCompiledOperandContext Operands() => new(_ => FalloutScriptValue.Form(0x14),
            _ => 1.25, _ => 2.5, _ => throw new InvalidDataException("Scalar fixture evaluated a command."), new());
        Require(FalloutCompiledOperands.Expression(Ascii(" 1.25e2 4 / 2 ~ +"), Operands()).Number == 29.25,
            "Vanilla postfix exponent, division or unary minus lost its numeric order.");
        var nullCompare = Join([(byte)'Z'], U16(1), Ascii(" 0 !="));
        Require(FalloutCompiledOperands.Expression(nullCompare, Operands()).Number == 1,
            "Typed source form/null equality lost its declared identity.");
        Reject(() => FalloutCompiledOperands.Expression(Join([(byte)'Z'], U16(1), Ascii(" 1 +")), Operands()));
        Reject(() => FalloutCompiledOperands.Expression(Ascii(" 1 0 /"), Operands()));
        Reject(() => FalloutCompiledOperands.Expression(Ascii(" 1e+"), Operands()));
        Reject(() => FalloutCompiledOperands.Expression(Ascii(" 1 2"), Operands()));
        Reject(() => FalloutCompiledOperands.Expression(Ascii(" +"), Operands()));
    }

    private static byte[] NestedFixture(bool source)
    {
        byte[] Remote(char type, ushort slot) => Join([(byte)'r'], U16(1), [(byte)type], U16(slot));
        byte[] Assignment(char type, ushort slot, byte[] expression) => Instruction(0x15,
            Join(Remote(type, slot), U16((ushort)expression.Length), expression));
        byte[] Objective(ushort opcode, int index) => Call(opcode, Join(U16(3), Form(1), Integer(index), Integer(1)));
        var trueStage = Join(Query(0x103b, Join(U16(2), Form(1), Integer(0))), Ascii(" 1 =="));
        var initial = Assignment('s', 1, Ascii(" 2 3 +"));
        var inner = Branch(0x16, 1, Join(Remote('s', 1), Ascii(" 5 ==")));
        var globalExpression = Join(Remote('s', 1), Ascii(" 0.5 +"));
        var positive = Join(initial, Branch(0x16, 5, trueStage), inner, Assignment('s', 2, Ascii(" 11")),
            Instruction(0x17, U16(1)), Instruction(0x2f03), Instruction(0x19),
            Branch(0x18, 1, Join(Query(0x102d, Join(U16(1), Form(4)), 3), Ascii(" 0 >"))),
            Instruction(0x3800), Instruction(0x17, U16(1)), Assignment('s', 2, Ascii(" 99")), Instruction(0x19),
            Instruction(0x15, Join([(byte)'G'], U16(2), U16((ushort)globalExpression.Length), globalExpression)));
        var nested = Join(Assignment('s', 1, Ascii(" 0")), Call(0x105a, Join(U16(1), Integer(0)), 2),
            Call(0x105d, [], 2), Call(0x105e, [], 2), Call(0x105e, [], 3),
            Branch(0x16, 1, Join(Query(0x102d, Join(U16(1), Form(5)), 4), Ascii(" 0 >"))),
            Call(0x1033, Join(U16(1), Form(6)), 4), Instruction(0x19),
            Branch(0x16, 1, Join(Remote('s', 2), Ascii(" 0 =="))), Assignment('s', 2, Ascii(" 1")), Instruction(0x19));
        var outer = Join(Objective(0x11a2, 40), Objective(0x11a2, 60), Objective(0x11a3, 70),
            Branch(0x16, 1, Join(Query(0x103b, Join(U16(2), Form(1), Integer(38))), Ascii(" 0 =="))),
            Call(0x1039, Join(U16(2), Form(1), Integer(38))), Instruction(0x19));
        byte[] Result(short stage, byte[] code, params uint[] refs) => Join(Field("INDX", U16((ushort)stage)),
            Field("QSDT", [0]), Field("SCHR", Header(code, refs.Length)), Field("SCDA", code),
            refs.Select(id => Field("SCRO", U32(id))).SelectMany(value => value).ToArray(),
            source ? Field("SCTX", Text("set diagnosticOnly to 999\nSourceFallbackMustNeverRun")) : []);
        var positiveRefs = new uint[] { 0x20, 0x50, 0x903, 0x14 };
        var nestedRefs = new uint[] { 0x20, 0x901, 0x902, 0x903, 0x14, 0x60 };
        return Join(Tes4(), Record("NPC_", 7), Record("NPC_", 2),
            Record("SCPT", 0x30, Field("SCHR", Header(Instruction(0x1d), 0, 2, 1)), Field("SCDA", Instruction(0x1d)),
                Local(1, "counter", true), Local(2, "suffix", true)),
            Record("GLOB", 0x50, Field("FNAM", [(byte)'f']), Field("FLTV", BitConverter.GetBytes(0f))),
            Record("DIAL", 0x60, Field("QSTI", U32(0x20)), Field("DATA", [0, 0])),
            Record("QUST", 0x20, Field("DATA", new byte[8]), Field("SCRI", U32(0x30)),
                Result(0, positive, positiveRefs), Result(38, nested, nestedRefs), Result(120, outer, 0x20),
                Result(201, Instruction(0x1539)), Result(202, Instruction(0x153b)), Result(203, Instruction(0x153c)), Result(204, Instruction(0x3800)),
                new uint[] { 40, 60, 70 }.Select(index => Join(Field("QOBJ", U32(index)), Field("NNAM", Text("Synthetic objective " + index))))
                    .SelectMany(value => value).ToArray()),
            Record("CELL", 0x800, Field("DATA", [1])), NestedActorGroup());
    }

    private static byte[] NestedActorGroup()
    {
        var actors = new uint[] { 0x901, 0x902, 0x903 }.Select(id => Record("ACHR", id,
            Field("NAME", U32(2)), Field("DATA", new byte[24]))).SelectMany(value => value).ToArray();
        var group = new byte[24 + actors.Length]; Encoding.ASCII.GetBytes("GRUP").CopyTo(group, 0);
        UInt(group, 4, (uint)group.Length); UInt(group, 8, 0x800); UInt(group, 12, 6); actors.CopyTo(group, 24);
        return group;
    }

    private static byte[] Query(ushort opcode, byte[] arguments, ushort? receiver = null) => Join(Ascii(" "),
        receiver is { } value ? Join([(byte)'r'], U16(value)) : [], [(byte)'X'], U16(opcode), U16((ushort)arguments.Length), arguments);
    private static byte[] Branch(ushort opcode, ushort skip, byte[] expression) => Instruction(opcode,
        Join(U16(skip), U16((ushort)expression.Length), expression));
    private static byte[] Ascii(string value) => Encoding.ASCII.GetBytes(value);
    private static string Failure(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is NotSupportedException or InvalidDataException or InvalidOperationException or KeyNotFoundException)
        { return error.Message; }
        throw new InvalidDataException("Unowned compiled operation was admitted or skipped.");
    }
}
