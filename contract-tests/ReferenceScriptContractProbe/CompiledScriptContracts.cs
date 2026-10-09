using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static partial class CompiledScriptContracts
{
    internal static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-compiled-contract-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            foreach (var sourcePresent in new[] { false, true })
            {
                var selected = Path.Combine(directory, sourcePresent ? "with-source" : "source-removed");
                Directory.CreateDirectory(selected);
                var path = Path.Combine(selected, "Bytecode.esm");
                File.WriteAllBytes(path, Fixture(sourcePresent));
                var hash = SHA256.HashData(File.ReadAllBytes(path));
                using var records = FalloutPluginStack.Load(selected, ["Bytecode.esm"]);
                var cell = FalloutCellSceneReader.Read(records, Key(0x800));
                using var world = new FalloutReferenceWorld(records); world.LoadCell(cell);
                var quests = new FalloutQuestState(records);
                var effects = new List<FalloutReferenceScriptEffect>();
                FalloutQuestStages? stages = null;
                FalloutReferenceScripts Scripts(FalloutReferenceWorld selectedWorld, FalloutQuestState selectedQuests) =>
                    new(records, selectedWorld, selectedQuests, new((_, _) => false, effect =>
                    {
                        effects.Add(effect);
                        if (effect.Kind == FalloutReferenceEffectKind.SetStage) stages!.Enter(effect.Target!.Value, effect.Stage);
                        else if (effect.Kind is not (FalloutReferenceEffectKind.ReferenceEnable or FalloutReferenceEffectKind.DefaultActivate))
                            throw new InvalidDataException("Unexpected compiled fixture effect.");
                    }, Command: (_, _, _, _) => throw new InvalidDataException("Compiled execution called the source command interpreter.")));
                var scripts = Scripts(world, quests);
                stages = new(records, quests, scripts.StageSteps, _ => throw new InvalidDataException("Unexpected compiled condition."));
                var physicalBefore = JsonSerializer.Serialize(world.Capture());
                Reject(() => world.Get(Key(0x900)).Write(3, .5));
                Reject(() => world.Get(Key(0x900)).Write(4, .5));
                Require(physicalBefore == JsonSerializer.Serialize(world.Capture()),
                    "Direct reference write changed physical locals before its typed refusal.");
                var questBefore = JsonSerializer.Serialize(quests.Capture());
                Reject(() => quests.SetVariable(Key(0x20), 4, .5));
                Require(questBefore == JsonSerializer.Serialize(quests.Capture()),
                    "Direct quest write allocated or changed physical locals before its typed refusal.");
                world.Get(Key(0x900)).Write(3, records.RuntimeFormId(Key(0x901)));
                var result = scripts.DispatchFrame(Key(0x900), [new("OnLoad"), new("GameMode")], .25);
                Require(result.All(value => value is { Error: null, Blocks: 1 }) && world.Get(Key(0x900)).Read(1) == 23 &&
                    world.Get(Key(0x900)).Read(2) == 0 && quests.Variable(Key(0x20), 1) == 21 && quests.StageDone(Key(0x20), 10) &&
                    !quests.IsRunning(Key(0x21)) && world.IsEnabled(Key(0x901)),
                    "Source-removed compiled state/order/Return or real quest-stage effects differ.");
                Require(effects.Count == 3 && effects[0] is { Kind: FalloutReferenceEffectKind.ReferenceEnable, Enable: false, Fade: false } &&
                    effects[1].Kind == FalloutReferenceEffectKind.SetStage &&
                    effects[2] is { Kind: FalloutReferenceEffectKind.ReferenceEnable, Enable: true, Fade: true },
                    "Compiled static/dynamic receivers or default fade operands changed.");
                var beforeActivation = effects.Count;
                Require(scripts.Activate(Key(0x903), Key(0x900)) is { Blocks: 1, Error: null } &&
                    world.Get(Key(0x903)).Read(1) == 29 && effects.Count == beforeActivation,
                    "Compiled OnActivate did not suppress independent default activation.");
                var effectsBeforeFailure = effects.Count;
                var failure = scripts.Dispatch(Key(0x902), "GameMode");
                var stoppedEffects = effects.Count;
                var failedInstruction = failure.Error ?? throw new InvalidDataException("Unknown compiled instruction did not stop.");
                Require(failedInstruction.Contains("2f03", StringComparison.Ordinal) &&
                    failedInstruction.Contains("SCDA offset", StringComparison.Ordinal) && stoppedEffects == effectsBeforeFailure + 1 &&
                    world.Get(Key(0x902)).Read(1) == 31 &&
                    world.Get(Key(0x902)).Read(2) == 0 && world.Get(Key(0x902)).ScriptStoppedFrame is null,
                    "Unknown compiled instruction lost its prefix/offset or invented a source cursor.");
                var stopped = JsonSerializer.Serialize(world.Get(Key(0x902)).Capture());
                Require(scripts.Dispatch(Key(0x902), "GameMode").Error == failure.Error &&
                    JsonSerializer.Serialize(world.Get(Key(0x902)).Capture()) == stopped && effects.Count == stoppedEffects,
                    "Stopped compiled invocation replayed its prefix.");
                Require(scripts.Activate(Key(0x902), Key(0x900)) is { Blocks: 0, Error: null } &&
                    JsonSerializer.Serialize(world.Get(Key(0x902)).Capture()) == stopped,
                    "Independent compiled default activation changed the stopped VM.");

                var saved = Copy(world.Capture().ToArray()); var savedQuests = Copy(quests.Capture().ToArray());
                var legacyReference = saved.Single(value => value.Reference == Key(0x900));
                var legacyFrame = new FalloutReferenceScriptStoppedFrame("Legacy source cursor", legacyReference.ScriptSha256!,
                    0, 0, "GameMode", null, .25, null, null, null);
                foreach (var completed in new[] { false, true })
                {
                    using var rejectedWorld = new FalloutReferenceWorld(records);
                    var rejected = false;
                    try { rejectedWorld.Restore(saved.Select(value => value.Reference != legacyReference.Reference ? value :
                        value with { ScriptError = legacyFrame.Error, ScriptStoppedFrame = completed ? null : legacyFrame,
                            CompletedScriptContinuation = completed ? legacyFrame : null }).ToArray()); }
                    catch (NotSupportedException error) when (error.Message.StartsWith("Legacy source statement continuation", StringComparison.Ordinal))
                    { rejected = true; }
                    Require(rejected && rejectedWorld.InstanceCount == 0, "Legacy source cursor parsed SCTX, mutated cold state or became a compiled offset.");
                }
                var warmCursorOwner = world.Get(Key(0x900)); warmCursorOwner.ScriptStoppedFrame = legacyFrame;
                Require(scripts.Dispatch(Key(0x900), "GameMode").Error?.Contains("Source statement cursor", StringComparison.Ordinal) == true &&
                    warmCursorOwner.Read(1) == 23, "Warm legacy source cursor replayed compiled instructions or lost refusal.");
                warmCursorOwner.ScriptStoppedFrame = null;
                using var cold = new FalloutReferenceWorld(records); cold.Restore(saved); cold.LoadCell(cell);
                var coldQuests = new FalloutQuestState(records); coldQuests.Restore(savedQuests);
                Require(Scripts(cold, coldQuests).Dispatch(Key(0x902), "GameMode").Error == failure.Error &&
                    JsonSerializer.Serialize(cold.Get(Key(0x902)).Capture()) == stopped && effects.Count == stoppedEffects + 1,
                    "Cold compiled failure lost its exact source/locals or replayed its prefix.");

                var info = FalloutDialogueTopic.Decode(records.GetEffective(Key(0x40)));
                scripts.ExecuteResult(info, Key(0x900), begin: false);
                Require(quests.Variable(Key(0x20), 1) == 37 && quests.Variable(Key(0x21), 1) == 0,
                    "Compiled INFO result used QSTI or SCTX instead of its ordered SCRO destination.");
                var empty = FalloutDialogueTopic.Decode(records.GetEffective(Key(0x41)));
                scripts.ExecuteResult(empty, Key(0x900), begin: false);
                Require(quests.Variable(Key(0x20), 1) == 37 && effects.Count == stoppedEffects + 1,
                    "An authored empty SCDA result executed contradictory SCTX or invented an effect.");

                var stageFields = records.GetEffective(Key(0x20)).ReadSubrecords().ToArray();
                var begin = Array.FindIndex(stageFields, field => field.Signature == "QSDT");
                using (var steps = scripts.StageSteps(records.GetEffective(Key(0x20)), FalloutScriptScope.QuestEntry(records.GetEffective(Key(0x20)), begin - 1, begin), "").GetEnumerator())
                {
                    Require(steps.MoveNext() && world.ScriptManualSaves.EnteredInvocations == 1, "Compiled iterator lost its invocation lease.");
                    Reject(world.ScriptManualSaves.RequireCapture);
                    Require(!steps.MoveNext(), "Single compiled stage invented another instruction.");
                }
                world.ScriptManualSaves.RequireCapture();
                var sourceProgram = FalloutGameModeProgram.Read("begin GameMode\nset value to 999\nend");
                Reject(() => scripts.ExecuteProgram(records.GetEffective(Key(0x20)), records.GetEffective(Key(0x30)), sourceProgram, 0));
                Require(quests.Variable(Key(0x20), 1) == 21, "Compiled quest invocation admitted diagnostic source fallback.");
                MalformedAndUnknown(records, world, quests, scripts);
                Require(hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(path))), "Synthetic input changed during compiled execution.");
                SourceDrift(selected, saved);
            }
            MasterOrder(directory);
        }
        finally { Directory.Delete(directory, true); }
        Console.WriteLine("OPENNV_COMPILED_SCRIPT_CONTRACT_PASS syntheticFullReader=true sourceRemoved=true " +
            "contradictorySourceIgnored=true orderedStaticAndDynamicReferences=true sharedQuestAndEnableOwners=true " +
            "authoredEventOrder=true return=true unknownPrefixCold=true legacySourceCursorRefused=true malformedAtomic=true pendingLease=true sourceDrift=true masterOrder=true");
    }

    private static void MalformedAndUnknown(FalloutPluginStack records, FalloutReferenceWorld world,
        FalloutQuestState quests, FalloutReferenceScripts scripts)
    {
        for (uint id = 0x200; id < 0x20b; ++id)
        {
            var beforeWorld = JsonSerializer.Serialize(world.Capture());
            var beforeQuests = JsonSerializer.Serialize(quests.Capture());
            var record = records.GetEffective(Key(id));
            Reject(() => { foreach (var _ in scripts.CompiledSteps(Key(0x900),
                FalloutCompiledScriptProgram.Read(record, FalloutScriptScope.QuestEntry(record, 2, 3), standalone: false))) { } });
            Require(beforeWorld == JsonSerializer.Serialize(world.Capture()) && beforeQuests == JsonSerializer.Serialize(quests.Capture()) &&
                world.ScriptManualSaves.EnteredInvocations == 0, "Malformed/unowned compiled instruction changed shared state or leaked its lease.");
        }
        var source = records.GetEffective(Key(0x20));
        var fake = new FalloutPluginSubrecord[] { new("SCHR", Header(Set('f', 1, " 999"), 0)), new("SCDA", Set('f', 1, " 999")) };
        Reject(() => FalloutCompiledScriptProgram.Read(source, fake, standalone: false));
        var empty = records.GetEffective(Key(0x30));
        var program = FalloutCompiledScriptProgram.Read(empty, FalloutScriptScope.Standalone(empty), standalone: true);
        Reject(() => program.ResultInstructions().ToArray());
        var absent = scripts.Dispatch(Key(0x904), "GameMode");
        Require(absent.Error?.StartsWith("Compiled:", StringComparison.Ordinal) == true && world.Get(Key(0x904)).Read(1) == 0,
            "Missing SCN framing was admitted or ran source fallback.");
        var death = scripts.Dispatch(Key(0x905), "OnMurder");
        Require(death.Error?.Contains("filter/callback semantics are unowned", StringComparison.Ordinal) == true &&
            world.Get(Key(0x905)).Read(1) == 0, "Unowned compiled event semantics were silently dropped.");
    }

    private static void SourceDrift(string directory, FalloutReferenceSnapshot[] saved)
    {
        var changed = Path.Combine(directory, "changed"); Directory.CreateDirectory(changed);
        File.WriteAllBytes(Path.Combine(changed, "Bytecode.esm"), Fixture(false, 32));
        using var records = FalloutPluginStack.Load(changed, ["Bytecode.esm"]);
        using var world = new FalloutReferenceWorld(records);
        Reject(() => world.Restore(saved));
    }

    private static void MasterOrder(string directory)
    {
        var selected = Path.Combine(directory, "master-order"); Directory.CreateDirectory(selected);
        File.WriteAllBytes(Path.Combine(selected, "Bytecode.esm"), Fixture(false));
        File.WriteAllBytes(Path.Combine(selected, "Unrelated.esm"), Tes4());
        var code = Set('f', 1, " 47", reference: 1);
        File.WriteAllBytes(Path.Combine(selected, "Patch.esp"), Join(Tes4("Unrelated.esm", "Bytecode.esm"),
            Record("QUST", 0x01000020, Field("DATA", new byte[8]), Field("SCRI", U32(0x01000030)), Field("INDX", U16(10)), Field("QSDT", [0]),
                Field("SCHR", Header(code, 1)), Field("SCDA", code), Field("SCRO", U32(0x01000020)))));
        using var records = FalloutPluginStack.Load(selected, ["Bytecode.esm", "Unrelated.esm", "Patch.esp"]);
        using var world = new FalloutReferenceWorld(records); var quests = new FalloutQuestState(records);
        var scripts = new FalloutReferenceScripts(records, world, quests, new((_, _) => false,
            _ => throw new InvalidDataException("Unexpected master-order effect.")));
        var source = records.GetEffective(Key(0x20));
        var program = FalloutCompiledScriptProgram.Read(source, FalloutScriptScope.QuestEntry(source, 2, 3), standalone: false);
        Require(program.References.Single().Form == Key(0x20), "Compiled SCRO used runtime order instead of declaring masters.");
        foreach (var _ in scripts.CompiledSteps(Key(0x20), program)) { }
        Require(quests.Variable(Key(0x20), 1) == 47, "Adjusted compiled destination did not reach the shared quest owner.");
    }

    private static byte[] Fixture(bool sourcePresent, int failurePrefix = 31)
    {
        var main = Join(Instruction(0x1d), Block(0, Join(Set('f', 1, " 17"), Call(0x1022, [0, 0], 1),
            Call(0x1039, Join(U16(2), Form(2), Integer(10))), Call(0x1036, Join(U16(1), Form(3))),
            Call(0x1037, Join(U16(1), Form(3))), Call(0x1021, [0, 0], 4), Instruction(0x1e), Set('f', 2, " 99"))),
            Block(21, Set('f', 1, " 23")));
        var fault = Join(Instruction(0x1d), Block(0, Join(Set('f', 1, " " + failurePrefix),
            Call(0x1039, Join(U16(2), Form(1), Integer(10))), Instruction(0x2f03), Set('f', 2, " 88"))));
        var activation = Join(Instruction(0x1d), Block(2, Set('f', 1, " 29")));
        var stage = Set('f', 1, " 21", reference: 1);
        var result = Set('f', 1, " 37", reference: 1);
        var malformed = new byte[][]
        {
            [0x15, 0, 16, 0, 0, 0], Call(0x1022, [0xff, 0xff], 1), Call(0x1022, [0, 0, 1], 1),
            Set('f', 9, " 1", 1), Set('s', 1, " 1", 1), Set('f', 1, " 1 2 $", 1), Set('s', 4, " 2.5", 1),
            Call(0x1022, [0, 0], 2), Set('Q', 1, " 1", 1), Call(0x1037, [0, 0]),
            Block(0, Set('f', 1, " 1"))
        };
        var references = Enumerable.Range(0, 6).Select(index => Record("REFR", (uint)(0x900 + index),
            Field("NAME", U32((uint)(1 + index))), Field("DATA", new byte[24]))).SelectMany(value => value).ToArray();
        var group = new byte[24 + references.Length]; Encoding.ASCII.GetBytes("GRUP").CopyTo(group, 0);
        UInt(group, 4, (uint)group.Length); UInt(group, 8, 0x800); UInt(group, 12, 6); references.CopyTo(group, 24);
        return Join(Tes4(), Enumerable.Range(0, 6).Select(index => Record("ACTI", (uint)(1 + index),
                Field("SCRI", U32((uint)(0x50 + index))))).SelectMany(value => value).ToArray(),
            Script(0x50, main, [Field("SCRO", U32(0x901)), Field("SCRO", U32(0x20)), Field("SCRO", U32(0x21)), Field("SCRV", U32(3))],
                sourcePresent ? "array_var value\nfloat suffix\nshort target\nshort integral\nfloat sourceOnlyWithoutSlot\nbegin GameMode\nset value to 999\nend\nbegin OnLoad\nset value to 998\ninvalid diagnostic source without end" : null),
            Script(0x51, Join(Instruction(0x1d)), []), Script(0x52, fault, [Field("SCRO", U32(0x20))]), Script(0x53, activation, []),
            Script(0x54, Set('f', 1, " 999"), []), Script(0x55, Join(Instruction(0x1d), Block(11, Set('f', 1, " 999"))), []),
            Script(0x30, Instruction(0x1d), [], quest: true),
            Record("QUST", 0x20, Field("DATA", new byte[8]), Field("SCRI", U32(0x30)), Field("INDX", U16(10)), Field("QSDT", [0]),
                Field("SCHR", Header(stage, 1)), Field("SCDA", stage), Field("SCRO", U32(0x20)),
                sourcePresent ? Field("SCTX", Text("set value to 999")) : []),
            Record("QUST", 0x21, Field("DATA", new byte[8]), Field("SCRI", U32(0x30))),
            Record("INFO", 0x40, Field("DATA", [0, 0, 0, 0]), Field("QSTI", U32(0x21)), Field("NEXT", []),
                Field("SCHR", Header(result, 1)), Field("SCDA", result), Field("SCRO", U32(0x20)),
                sourcePresent ? Field("SCTX", Text("set unrelated.value to 999")) : []),
            Record("INFO", 0x41, Field("DATA", [0, 0, 0, 0]), Field("QSTI", U32(0x20)), Field("NEXT", []),
                Field("SCHR", Header([], 1)), Field("SCDA", []), Field("SCRO", U32(0x20)),
                Field("SCTX", Text("set unrelated.value to 999\nContradictorySourceMustNeverRun"))),
            malformed.Select((code, index) => Record("QUST", (uint)(0x200 + index), Field("DATA", new byte[8]), Field("SCRI", U32(0x30)), Field("INDX", U16(10)), Field("QSDT", [0]),
                Field("SCHR", Header(code, 1)), Field("SCDA", code), Field("SCRO", U32(0x901)))).SelectMany(value => value).ToArray(),
            Record("CELL", 0x800, Field("DATA", [1])), group);
    }

    private static byte[] Script(uint id, byte[] code, byte[][] references, string? source = null, bool quest = false) =>
        Record("SCPT", id, Field("SCHR", Header(code, references.Length, 4, quest ? (ushort)1 : (ushort)0)), Field("SCDA", code),
            Local(1, "value"), Local(2, "suffix"), Local(3, "target"), Local(4, "integral", true), Join(references),
            source is null ? [] : Field("SCTX", Text(source)));
    private static byte[] Local(uint index, string name, bool integer = false)
    {
        var local = new byte[24]; UInt(local, 0, index); local[16] = integer ? (byte)1 : (byte)0;
        return Join(Field("SLSD", local), Field("SCVR", Text(name)));
    }
    private static byte[] Header(byte[] code, int references, uint locals = 0, ushort scriptType = 0)
    { var header = new byte[20]; UInt(header, 4, (uint)references); UInt(header, 8, (uint)code.Length); UInt(header, 12, locals); BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(16), scriptType); header[18] = 1; return header; }
    private static byte[] Block(ushort type, byte[] body) => Join(Instruction(0x10, Join(U16(type), U32((uint)body.Length + 4))), body, Instruction(0x11));
    private static byte[] Set(char type, ushort index, string literal, ushort? reference = null)
    {
        var value = Encoding.ASCII.GetBytes(literal);
        return Instruction(0x15, Join(reference is { } target ? Join([(byte)'r'], U16(target)) : [], [(byte)type], U16(index), U16((ushort)value.Length), value));
    }
    private static byte[] Call(ushort opcode, byte[] arguments, ushort? receiver = null) =>
        receiver is { } reference ? Join(U16(0x1c), U16(reference), Instruction(opcode, arguments)) : Instruction(opcode, arguments);
    private static byte[] Instruction(ushort opcode, byte[]? payload = null) => Join(U16(opcode), U16((ushort)(payload?.Length ?? 0)), payload ?? []);
    private static byte[] Form(ushort reference) => Join([(byte)'r'], U16(reference));
    private static byte[] Integer(int value) => Join([(byte)'n'], BitConverter.GetBytes(value));
    private static byte[] Tes4(params string[] masters)
    { var header = new byte[12]; BinaryPrimitives.WriteSingleLittleEndian(header, 1.34f); return Record("TES4", 0, Field("HEDR", header), masters.Select(name => Join(Field("MAST", Text(name)), Field("DATA", new byte[8]))).SelectMany(value => value).ToArray()); }
    private static byte[] U16(ushort value) => BitConverter.GetBytes(value);
    private static byte[] U32(uint value) => BitConverter.GetBytes(value);
    private static byte[] Text(string value) => Encoding.ASCII.GetBytes(value + '\0');
    private static byte[] Join(params byte[][] values) => values.SelectMany(value => value).ToArray();
    private static void UInt(byte[] data, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset), value);
    private static byte[] Field(string signature, byte[] data)
    { var result = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(signature).CopyTo(result, 0); BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(4), checked((ushort)data.Length)); data.CopyTo(result, 6); return result; }
    private static byte[] Record(string signature, uint id, params byte[][] fields)
    { var data = Join(fields); var result = new byte[24 + data.Length]; Encoding.ASCII.GetBytes(signature).CopyTo(result, 0); UInt(result, 4, (uint)data.Length); UInt(result, 12, id); data.CopyTo(result, 24); return result; }
    private static FalloutFormKey Key(uint id) => new("Bytecode.esm", id);
    private static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
    private static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }
    private static void Reject(Action action)
    { try { action(); } catch (Exception error) when (error is InvalidDataException or InvalidOperationException or NotSupportedException or KeyNotFoundException) { return; } throw new InvalidDataException("Malformed/unowned compiled input was admitted."); }
}
