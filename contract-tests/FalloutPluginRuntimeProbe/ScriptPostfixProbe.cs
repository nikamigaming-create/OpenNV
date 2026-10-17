using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class ScriptPostfixProbe
{
    internal static void Run()
    {
        Expressions();
        Owners();
        Console.WriteLine("OPENNV_SCRIPT_POSTFIX_PASS typedReceiver=true resultAndIndex=true evaluateOnce=true shortCircuit=true commandGrouping=true compiledSlots=true coldOwners=true failurePrefix=true unsupportedVisible=true parity=unverified");
    }

    private static void Expressions()
    {
        var calls = new List<string>();
        var reads = 0;
        var arrays = new FalloutScriptArrayStore();
        var refs = arrays.Function("Ar_List")!.InvokeValue([new(FalloutScriptValue.Form(17), null), new(FalloutScriptValue.Form(18), null)]);
        var locals = new Dictionary<string, FalloutScriptValue> { ["refs"] = refs, ["ref"] = FalloutScriptValue.Form(17), ["result"] = 0, ["index"] = 1 };
        FalloutScriptFunction? Function(string name) => name.ToLowerInvariant() switch
        {
            "nextref" => FalloutScriptFunction.Typed([], _ => { calls.Add("receiver"); return FalloutScriptValue.Form(17); }),
            "nextindex" => new([], _ => { calls.Add("index"); return 0; }),
            "argument" => new([], _ => { calls.Add("argument"); return 3; }),
            "bound.read" => new([], _ => 41),
            "getcurrenttime" => new([], _ => 12) { ReadOnly = true },
            _ => null,
        };
        FalloutScriptFunction? ReferenceFunction(string name)
        {
            var signature = name.ToLowerInvariant() switch
            {
                "read" or "link" => FalloutScriptFunction.Typed([], _ => throw new InvalidOperationException("Signature was executed."), readOnly: true),
                "measure" => FalloutScriptFunction.Typed([FalloutScriptArgumentKind.Number], _ => throw new InvalidOperationException("Signature was executed.")),
                "optional" => FalloutScriptFunction.Typed([FalloutScriptArgumentKind.OptionalNumber], _ => throw new InvalidOperationException("Signature was executed.")),
                _ => null,
            };
            return signature is null ? null : FalloutScriptFunction.Reference(signature, (caller, arguments) =>
            {
                calls.Add(name.ToLowerInvariant());
                return name.ToLowerInvariant() switch
                {
                    "read" => caller.Number == 17 ? 7 : 11,
                    "link" => FalloutScriptValue.Form(18),
                    _ => caller.Number + (arguments.Count == 0 ? 0 : arguments[0].Number),
                };
            });
        }
        var context = new FalloutScriptValueContext(name => { ++reads; return locals[name]; }, (name, value) => locals[name] = value,
            Arrays: arrays, ReferenceFunction: ReferenceFunction, IsForm: name => name is "ref" or "nullRef");
        FalloutScriptValue Evaluate(string expression) => FalloutNvseNumericExpression.EvaluateValue(
            FalloutGameModeProgram.Tokens(expression), context, Function);
        Require(Evaluate("(NextRef).Link.Read * 2 + refs[NextIndex].Read").Number == 29 &&
            calls.SequenceEqual(["receiver", "link", "read", "index", "read"]), "Postfix calls lost a typed result, index, binding or evaluation order.");
        calls.Clear();
        Require(Evaluate("result := (NextRef).Measure (Argument) + 2").Number == 22 && locals["result"].Number == 22 &&
            calls.SequenceEqual(["receiver", "argument", "measure"]), "Receiver/argument evaluation repeated or consumed an outer operator.");
        calls.Clear();
        Require(Evaluate("1 || (NextRef).Read").Number == 1 && Evaluate("0 && refs[NextIndex].Read").Number == 0 && calls.Count == 0,
            "An inactive postfix expression read its receiver, index or method.");
        reads = 0;
        Require(Evaluate("1 || (NextRef).Optional index").Number == 1 &&
            Evaluate("1 || (NextRef).Optional UnresolvedInactiveName").Number == 1 && calls.Count == 0 && reads == 0,
            "Optional argument classification read an inactive variable or unresolved name.");
        Require(Evaluate("(ref).Optional index").Number == 18 && reads == 2 && calls.SequenceEqual(["optional"]),
            "An optional numeric argument did not use pure kind metadata and once-only reads.");
        calls.Clear();
        Require(Evaluate("Bound.Read + .5 + 1.25").Number == 42.75 &&
            FalloutGameModeProgram.Tokens("Quest.local PlayerRef.GetAV .5").SequenceEqual(["Quest.local", "PlayerRef.GetAV", ".5"]),
            "Postfix tokenization changed static compiled names or decimal literals.");
        foreach (var expression in new[] { "(NextRef).", "(NextRef).1", "(NextRef).Read +", "(NextRef).Measure", "(NextRef).Missing", "(NextRef).Read := 1" })
            Reject(() => Evaluate(expression));
        Require(calls.Count == 0, "Malformed or unowned postfix syntax invoked a receiver before parsing completed.");
        foreach (var receiver in new[] { "17", "0", "\"ref\"", "refs", "(ref + 0)" })
            Reject(() => Evaluate($"({receiver}).Measure (Argument)"));
        locals["nullRef"] = FalloutScriptValue.Form(0);
        Reject(() => Evaluate("(nullRef).Measure (Argument)"));
        Require(calls.Count == 0, "An invalid typed receiver invoked its stateful argument.");
        var arguments = FalloutGameModeProgram.ResolveCommandArguments(FalloutGameModeProgram.Tokens(
            "\"HUD/value\" (NextRef).Measure (Argument) refs[NextIndex].Read -(ref).Read 9"), context, Function);
        Require(arguments.SequenceEqual(["\"HUD/value\"", "20", "7", "-7", "9"]) &&
            calls.SequenceEqual(["receiver", "argument", "measure", "index", "read", "read"]),
            "A postfix statement argument was split or evaluated more than once.");
        Require(FalloutGameModeProgram.WasRejectedByParser("begin GameMode\nset result to (ref).Read\nend", 8) &&
            !FalloutGameModeProgram.WasRejectedByParser("Notify \"(ref).Read\" ; refs[0].Read\nset result to .5 + Bound.Read", 8) &&
            !FalloutGameModeProgram.WasRejectedByParser("set result to (ref).Read", 9), "Postfix migration admitted an unrelated missing owner.");
        Require(!FalloutGameModeProgram.Read("begin GameMode\nif (ref).Read\nset result to GetCurrentTime\nendif\nend")
                .CanRetryMissingRead("GetCurrentTime", Function) &&
            !FalloutGameModeProgram.Read("begin GameMode\nif (ref).Read\nPlayGroup Forward 1\nendif\nend")
                .CanRetryMissingCommand("PlayGroup", Function), "Legacy recovery could repeat a postfix receiver or method.");
    }

    private static void Owners()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-postfix-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            const string body = "prefix += 1\ntargets = Ar_List GetSelf PeerREF\n" +
                "result = targets[0].GetDisabled + targets[1].GetDisabled * 2\n" +
                "holder = (targets[0]).AuxiliaryVariableGetRef \"link\"\nresult += (holder).GetDisabled * 4\n";
            var fields = Header().Concat(Script(0x100, body, 1)).Concat(Script(0x101,
                "prefix += 1\nresult = (GetSelf).GetEquippedItemRef 5\nprefix += 100\n", 1))
                .Concat(Script(0x102, "prefix += 1\nholder = PlayerRef\ntargets = Ar_List holder\n" +
                    "result = targets[0].GetAV Health\nresult += (holder).AuxiliaryVariableGetFloat \"offset\"\n", 1))
                .Concat(Record("ACTI", 0x400, Field("SCRI", BitConverter.GetBytes(0x100u))))
                .Concat(Record("ACTI", 0x401, Field("SCRI", BitConverter.GetBytes(0x101u))))
                .Concat(Record("ACTI", 0x402))
                .Concat(Record("QUST", 0x700, Field("EDID", Text("PostfixQuest")), Field("DATA", new byte[8]), Field("SCRI", BitConverter.GetBytes(0x102u))))
                .Concat(Cell()).ToArray();
            File.WriteAllBytes(Path.Combine(directory, "Postfix.esm"), fields);
            File.WriteAllBytes(Path.Combine(directory, "PostfixPatch.esp"), Header("Postfix.esm").Concat(Script(0x100, body, 11)).ToArray());
            using var records = FalloutPluginStack.Load(directory, ["Postfix.esm", "PostfixPatch.esp"]);
            var cell = FalloutCellSceneReader.Read(records, Key(0x600));
            using var world = new FalloutReferenceWorld(records);
            world.LoadCell(cell);
            world.SetEnabled(Key(0x501), false);
            world.AdvanceEnableChanges(0, new(1, 1), _ => false);
            world.Auxiliary.SetForm(Key(0x500), "Postfix.esm", "link", Key(0x501));
            FalloutReferenceScripts Executor(FalloutReferenceWorld owner) => new(records, owner, new(records),
                new((_, _) => false, _ => throw new InvalidOperationException("Postfix query invented a presentation effect.")));
            var scripts = Executor(world);
            var first = scripts.Dispatch(Key(0x500), "GameMode");
            Require(first is { Blocks: 1, Error: null } &&
                world.Get(Key(0x500)).Read(14) == 6 && world.Get(Key(0x500)).Read(15) == 1 &&
                world.Get(Key(0x500)).Read(11) == records.RuntimeFormId(Key(0x501)),
                $"Postfix execution ignored winning compiled slots, per-object enable state or auxiliary form identity: {first.Error}; " +
                    JsonSerializer.Serialize(world.Get(Key(0x500)).Capture().Variables));
            var activation = scripts.Activate(Key(0x500), Key(0x501));
            Require(activation is { Blocks: 1, Error: null } && world.Get(Key(0x500)).Read(12) == records.RuntimeFormId(Key(0x501)),
                "GetActionRef lost its typed activation identity before a postfix query.");
            world.Get(Key(0x500)).Write(11, records.RuntimeFormId(Key(0x600)));
            Reject(() => scripts.ExecuteProgram(records.GetEffective(Key(0x500)), records.GetEffective(Key(0x100)),
                FalloutGameModeProgram.Read("begin GameMode\nresult = (holder).AuxiliaryVariableGetFloat \"link\" 0 PeerREF\nend"), 0));
            Require(world.Get(Key(0x500)).Read(14) == 1, "An explicit auxiliary owner hid an invalid calling reference.");
            Require(scripts.Dispatch(Key(0x502), "GameMode").Error?.Contains("GetEquippedItemRef", StringComparison.Ordinal) == true &&
                world.Get(Key(0x502)).Read(5) == 1, "Unowned inventory identity was swallowed or discarded its executed prefix.");
            var snapshots = RoundTrip(world.Capture().ToArray());
            using var cold = new FalloutReferenceWorld(records);
            cold.Restore(snapshots);
            cold.ScriptValues.Restore(RoundTrip(world.ScriptValues.Capture()));
            cold.Auxiliary.RestorePermanent(RoundTrip(world.Auxiliary.CapturePermanent()));
            cold.ValidateValueHandles(); cold.ScriptValues.Arrays.ValidateRestoredRoots(); cold.LoadCell(cell);
            var restored = Executor(cold);
            for (var frame = 0; frame < 3; ++frame)
            {
                Require(scripts.Dispatch(Key(0x500), "GameMode").Error is null && restored.Dispatch(Key(0x500), "GameMode").Error is null,
                    "Postfix recurrence failed after cold restoration.");
                scripts.Dispatch(Key(0x502), "GameMode"); restored.Dispatch(Key(0x502), "GameMode");
            }
            Require(JsonSerializer.Serialize(world.Capture()) == JsonSerializer.Serialize(cold.Capture()) &&
                JsonSerializer.Serialize(world.ScriptValues.Capture()) == JsonSerializer.Serialize(cold.ScriptValues.Capture()) &&
                cold.Get(Key(0x502)).Read(5) == 1, "Cold postfix execution changed identities or replayed a faulted prefix.");
            var state = new FalloutQuestState(records); state.SetRunning(Key(0x700), true);
            FalloutQuestScripts QuestScripts(FalloutQuestState owner) => new(records, owner, new HashSet<FalloutFormKey>(),
                new FalloutPlayerInventory(), defaultProcessingDelay: 1);
            var quests = QuestScripts(state);
            var initial = quests.Capture();
            quests.Restore(initial with { Instances = [], ParserVersion = 8 });
            Require(quests.Capture().Instances.Single().Executions == 0, "Lexical migration invented a prior quest execution.");
            Reject(() => QuestScripts(state).Restore(initial with { Instances = [] }));
            var host = new FalloutQuestScriptHost((_, _) => throw new InvalidOperationException("Unexpected stage effect."),
                name => name == "Health" ? 31 : throw new InvalidOperationException("Unexpected actor value."));
            quests.Host = host;
            quests.Auxiliary.SetFloat(Key(0x14), "Postfix.esm", "offset", 2);
            quests.Advance(1);
            Require(state.Variable(Key(0x700), 4) == 33 && quests.Capture().Instances.Single().Error is null,
                $"Quest fallback lost its typed player/auxiliary receiver or gameplay host: {quests.Capture().Instances.Single().Error}; " +
                    JsonSerializer.Serialize(state.Capture()));
            var coldState = new FalloutQuestState(records); coldState.Restore(RoundTrip(state.Capture()));
            var coldQuests = QuestScripts(coldState); coldQuests.Restore(RoundTrip(quests.Capture())); coldQuests.Host = host;
            quests.Advance(1); coldQuests.Advance(1);
            Require(JsonSerializer.Serialize(state.Capture()) == JsonSerializer.Serialize(coldState.Capture()) &&
                JsonSerializer.Serialize(quests.Capture()) == JsonSerializer.Serialize(coldQuests.Capture()),
                "Fallback quest postfix state changed across serialized cold restoration.");
        }
        finally
        {
            File.Delete(Path.Combine(directory, "Postfix.esm")); File.Delete(Path.Combine(directory, "PostfixPatch.esp"));
            Directory.Delete(directory);
        }
    }

    private static T RoundTrip<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
    private static FalloutFormKey Key(uint id) => new("Postfix.esm", id);
    private static byte[] Script(uint id, string body, uint first)
    {
        var source = "ref holder\nref alias\narray_var targets\nshort result\nshort prefix\nbegin GameMode\n" + body +
            "end\nbegin OnActivate\nalias = GetActionRef\nresult = (alias).GetDisabled\nend";
        var header = new byte[20]; header[16] = id == 0x102 ? (byte)1 : (byte)0;
        var fields = new List<byte[]> { Field("EDID", Text("PostfixScript" + id)), Field("SCHR", header), Field("SCTX", Text(source)),
            Field("SCRO", BitConverter.GetBytes(0x501u)), Field("SCRO", BitConverter.GetBytes(0x14u)) };
        foreach (var (name, offset, kind) in new[] { ("holder", 0u, (byte)1), ("alias", 1u, (byte)1),
            ("targets", 2u, (byte)0), ("result", 3u, (byte)0), ("prefix", 4u, (byte)0) })
        {
            var local = new byte[24]; BinaryPrimitives.WriteUInt32LittleEndian(local, first + offset); local[16] = kind;
            fields.Add(Field("SLSD", local)); fields.Add(Field("SCVR", Text(name)));
        }
        return Record("SCPT", id, fields.ToArray());
    }
    private static byte[] Cell()
    {
        var parent = BitConverter.GetBytes(0x600u);
        var payload = new[] { Reference(0x500, 0x400, "SourceREF"), Reference(0x501, 0x402, "PeerREF"), Reference(0x502, 0x401, "FaultREF") }
            .SelectMany(value => value).ToArray();
        var group = new byte[24 + payload.Length]; Encoding.ASCII.GetBytes("GRUP").CopyTo(group, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(4), (uint)group.Length); parent.CopyTo(group, 8);
        BinaryPrimitives.WriteInt32LittleEndian(group.AsSpan(12), 9); payload.CopyTo(group, 24);
        var children = new byte[24 + group.Length]; Encoding.ASCII.GetBytes("GRUP").CopyTo(children, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(children.AsSpan(4), (uint)children.Length); parent.CopyTo(children, 8);
        BinaryPrimitives.WriteInt32LittleEndian(children.AsSpan(12), 6); group.CopyTo(children, 24);
        return Record("CELL", 0x600, Field("DATA", [1])).Concat(children).ToArray();
    }
    private static byte[] Reference(uint id, uint baseId, string name) => Record("REFR", id,
        Field("EDID", Text(name)), Field("NAME", BitConverter.GetBytes(baseId)), Field("DATA", new byte[24]));
    private static byte[] Header(string? master = null)
    {
        var header = new byte[12]; BinaryPrimitives.WriteSingleLittleEndian(header, 1.34f);
        return master is null ? Record("TES4", 0, Field("HEDR", header)) :
            Record("TES4", 0, Field("HEDR", header), Field("MAST", Text(master)), Field("DATA", new byte[8]));
    }
    private static byte[] Text(string text) => Encoding.ASCII.GetBytes(text + '\0');
    private static byte[] Field(string signature, byte[] data)
    {
        var result = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(signature).CopyTo(result, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(4), checked((ushort)data.Length)); data.CopyTo(result, 6); return result;
    }
    private static byte[] Record(string signature, uint id, params byte[][] fields)
    {
        var data = fields.SelectMany(value => value).ToArray(); var result = new byte[24 + data.Length];
        Encoding.ASCII.GetBytes(signature).CopyTo(result, 0); BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), (uint)data.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(12), id); data.CopyTo(result, 24); return result;
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidDataException("Invalid postfix operation was admitted.");
    }
}
