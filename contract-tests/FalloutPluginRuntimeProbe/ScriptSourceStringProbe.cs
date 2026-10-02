using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class ScriptSourceStringProbe
{
    internal static void Run()
    {
        Expressions();
        Owners();
        StatementSetterRefusals();
        IniOwners();
        Console.WriteLine("OPENNV_SCRIPT_SOURCE_STRING_PASS explicitSignature=true declaredTypes=true lazyNames=true evaluateOnce=true compiledSlots=true functionFrame=true coldHandles=true failurePrefix=true statementOrder=true parity=unverified");
    }

    private static void Expressions()
    {
        var calls = new List<string>();
        var metadata = 0;
        var arrays = new FalloutScriptArrayStore();
        var locals = new Dictionary<string, FalloutScriptValue>(StringComparer.OrdinalIgnoreCase)
        {
            ["name"] = "fValue",
            ["number"] = 7,
            ["form"] = FalloutScriptValue.Form(17),
            ["names"] = arrays.Function("Ar_List")!.InvokeValue([new((FalloutScriptValue)"fValue")]),
            ["Scope.name"] = "fValue",
        };
        var context = new FalloutScriptValueContext(
            name =>
            {
                calls.Add("read:" + name); return locals.TryGetValue(name, out var value) ? value :
                throw new NotSupportedException("No typed operand owner: " + name);
            },
            (name, value) => locals[name] = value, Arrays: arrays,
            HasValueOwner: name => { ++metadata; return locals.ContainsKey(name); });
        FalloutScriptFunction? Function(string name) => name.ToLowerInvariant() switch
        {
            "query" => new([FalloutScriptArgumentKind.SourceString], arguments =>
                { calls.Add("query:" + arguments[0].Text); return arguments[0].Text == "fValue" ? 11 : -1; }),
            "store" => new([FalloutScriptArgumentKind.SourceString, FalloutScriptArgumentKind.Number], arguments =>
                { calls.Add("store:" + arguments[0].Text); return arguments[1].Number; }),
            "text" => FalloutScriptFunction.Typed([], _ => { calls.Add("text"); return "fValue"; }),
            "index" => new([], _ => { calls.Add("index"); return 0; }),
            "argument" => new([], _ => { calls.Add("argument"); return 3; }),
            "ordinarystring" => new([FalloutScriptArgumentKind.String], arguments => arguments[0].Text.Length),
            _ => null,
        };
        FalloutScriptValue Evaluate(string expression) => FalloutNvseNumericExpression.EvaluateValue(
            FalloutGameModeProgram.Tokens(expression), context, Function);
        Require(Evaluate("Query fValue").Number == 11 && calls.SequenceEqual(["query:fValue"]) && metadata == 1,
            "An explicit bare source constant read a variable or lost its name.");
        calls.Clear();
        Require(Evaluate("Query name").Number == 11 && calls.SequenceEqual(["read:name", "query:fValue"]),
            "A declared string variable became literal identifier text.");
        calls.Clear();
        Require(Evaluate("Query Scope.name").Number == 11 && calls.SequenceEqual(["read:Scope.name", "query:fValue"]),
            "A qualified compiled variable lost its owner.");
        calls.Clear();
        Require(Evaluate("Store (Text) (Argument)").Number == 3 && calls.SequenceEqual(["text", "argument", "store:fValue"]),
            "Grouped source-string results or later arguments executed out of order or repeated.");
        calls.Clear();
        Require(Evaluate("Query names[Index]").Number == 11 &&
            calls.SequenceEqual(["read:names", "index", "query:fValue"]), "An indexed string argument became a bare array name.");
        calls.Clear(); metadata = 0;
        Require(Evaluate("1 || Query MissingName").Number == 1 &&
            Evaluate("0 && Query MissingOwner.name").Number == 0 &&
            Evaluate("1 || Query names[Index]").Number == 1 && calls.Count == 0 && metadata == 0,
            "An inactive argument read state, bound unresolved metadata, or ran its index.");
        foreach (var operand in new[] { "number", "form", "names", "7", "(Argument)" })
            Reject(() => Evaluate("Store " + operand + " (Text)"));
        Require(!calls.Contains("text") && !calls.Any(value => value.StartsWith("store:", StringComparison.Ordinal)),
            "A wrong typed source-string value invoked a later stateful argument or setter.");
        calls.Clear();
        Reject(() => Evaluate("OrdinaryString Undeclared"));
        Reject(() => Evaluate("Undeclared + 1"));
        Reject(() => Evaluate("Query MissingOwner.name"));
        Require(!calls.Any(value => value.StartsWith("query:", StringComparison.Ordinal)),
            "The source-string contract leaked into ordinary strings, arithmetic or unknown qualified names.");
        calls.Clear(); metadata = 0;
        foreach (var expression in new[] { "Query fValue extra", "Store fValue", "Query fValue +", "Query (Text))" })
            Reject(() => Evaluate(expression));
        Require(calls.Count == 0 && metadata == 0, "Malformed syntax invoked a source-string read before parsing completed.");
        var noOwner = new FalloutScriptValueContext(_ => throw new InvalidDataException("Unexpected read."), (_, _) => { });
        Require(FalloutNvseNumericExpression.EvaluateValue(FalloutGameModeProgram.Tokens("Query \"fValue\""),
            noOwner, Function).Number == 11, "A literal string required variable metadata.");
        Reject(() => FalloutNvseNumericExpression.EvaluateValue(FalloutGameModeProgram.Tokens("Query fValue"), noOwner, Function));
    }

    private static void Owners()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-source-string-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            const string body = "if !name\nname = \"fSourceNumber\"\nendif\nprefix += 1\n" +
                "literal = GetNumericGameSetting fSourceNumber\nobserved = GetNumericGameSetting name\n" +
                "frame = Call SourceStringUDF name\nqualified = GetNumericGameSetting SourceQuest.name\n" +
                "if prefix == 1\nstatus = SetNumericGameSetting name (observed + 1)\n" +
                "SetNumericGameSetting fSourceNumber (GetNumericGameSetting name + 1)\nendif\n" +
                "observed = GetNumericGameSetting name\n";
            const string fallbackBody = "if !name\nname = \"fSourceNumber\"\nendif\nprefix += 1\n" +
                "literal = GetNumericGameSetting fSourceNumber\n" +
                "if prefix == 1\nstatus = SetNumericGameSetting name (literal + 1)\n" +
                "SetNumericGameSetting name (GetNumericGameSetting name + 1)\nendif\n" +
                "observed = GetNumericGameSetting name\n";
            var root = Header().Concat(Setting(0x800, 2.5f))
                .Concat(Script(0x100, body, 1, false))
                .Concat(Script(0x101, "prefix += 1\nliteral = GetNumericGameSetting prefix\nprefix += 100\n", 1, false))
                .Concat(Script(0x102, fallbackBody, 31, true))
                .Concat(Function())
                .Concat(Record("ACTI", 0x400, Field("SCRI", BitConverter.GetBytes(0x100u))))
                .Concat(Record("ACTI", 0x401, Field("SCRI", BitConverter.GetBytes(0x101u))))
                .Concat(Record("QUST", 0x700, Field("EDID", Text("SourceQuest")), Field("DATA", new byte[8]),
                    Field("SCRI", BitConverter.GetBytes(0x102u))))
                .Concat(Cell()).ToArray();
            File.WriteAllBytes(Path.Combine(directory, "SourceString.esm"), root);
            File.WriteAllBytes(Path.Combine(directory, "SourceStringPatch.esp"),
                Header("SourceString.esm").Concat(Setting(0x800, 3.5f)).Concat(Script(0x100, body, 11, false)).ToArray());
            using var records = FalloutPluginStack.Load(directory, ["SourceString.esm", "SourceStringPatch.esp"]);
            var cell = FalloutCellSceneReader.Read(records, Key(0x600));
            var state = new FalloutQuestState(records);
            using var world = new FalloutReferenceWorld(records);
            world.LoadCell(cell);
            var questName = world.ScriptValues.Write(FalloutScriptLocalKind.String, 0, "fSourceNumber", "SourceString.esm");
            state.SetVariable(Key(0x700), 31, questName);
            FalloutReferenceScripts Executor(FalloutReferenceWorld owner) => new(records, owner, state,
                new((_, _) => false, _ => throw new InvalidDataException("Source-string query invented a presentation effect.")));
            var executor = Executor(world);
            var first = executor.Dispatch(Key(0x500), "GameMode");
            Require(first.Error is null && world.Get(Key(0x500)).Read(12) == 1 &&
                world.Get(Key(0x500)).Read(13) == 3.5 && world.Get(Key(0x500)).Read(14) == 5.5 &&
                world.Get(Key(0x500)).Read(15) == 3.5 && world.Get(Key(0x500)).Read(16) == 3.5 &&
                world.Get(Key(0x500)).Read(17) == 1 && records.NumericSettings.Get("fSourceNumber") == 5.5,
                "Winning slots, function-frame strings, qualified locals or getter/setter ownership failed: " + first.Error);
            var handle = world.Get(Key(0x500)).Read(11);
            Require(handle != 0 && world.ScriptValues.Read(FalloutScriptLocalKind.String, handle).Text == "fSourceNumber",
                "A declared string lost its stored handle.");
            Require(executor.Dispatch(Key(0x501), "GameMode").Error is not null && world.Get(Key(0x501)).Read(2) == 1,
                "A declared numeric local became literal text or discarded its failed prefix.");
            using var cold = new FalloutReferenceWorld(records);
            cold.Restore(RoundTrip(world.Capture().ToArray()));
            cold.ScriptValues.Restore(RoundTrip(world.ScriptValues.Capture()));
            cold.ValidateValueHandles(); cold.LoadCell(cell);
            var restored = Executor(cold);
            executor.Dispatch(Key(0x500), "GameMode"); restored.Dispatch(Key(0x500), "GameMode");
            executor.Dispatch(Key(0x501), "GameMode"); restored.Dispatch(Key(0x501), "GameMode");
            Require(JsonSerializer.Serialize(world.Capture()) == JsonSerializer.Serialize(cold.Capture()) &&
                JsonSerializer.Serialize(world.ScriptValues.Capture()) == JsonSerializer.Serialize(cold.ScriptValues.Capture()) &&
                cold.Get(Key(0x500)).Read(11) == handle && cold.Get(Key(0x501)).Read(2) == 1,
                "Cold source-string continuation changed handles or replayed a failed prefix.");

            records.NumericSettings.Set("fSourceNumber", 3.5);
            var questState = new FalloutQuestState(records); questState.SetRunning(Key(0x700), true);
            FalloutQuestScripts Scripts(FalloutQuestState owner) => new(records, owner, new HashSet<FalloutFormKey>(),
                new FalloutPlayerInventory(), defaultProcessingDelay: 1);
            var scripts = Scripts(questState); scripts.Advance(1);
            var questHandle = questState.Variable(Key(0x700), 31);
            Require(scripts.Capture().Instances.Single().Error is null && questState.Variable(Key(0x700), 33) == 3.5 &&
                questState.Variable(Key(0x700), 34) == 5.5 && questState.Variable(Key(0x700), 37) == 1 && questHandle != 0,
                "Fallback quest source-string getter/setter or compiled declarations failed.");
            var coldState = new FalloutQuestState(records); coldState.Restore(RoundTrip(questState.Capture()));
            var coldScripts = Scripts(coldState); coldScripts.Restore(RoundTrip(scripts.Capture()));
            scripts.Advance(1); coldScripts.Advance(1);
            Require(JsonSerializer.Serialize(questState.Capture()) == JsonSerializer.Serialize(coldState.Capture()) &&
                JsonSerializer.Serialize(scripts.Capture()) == JsonSerializer.Serialize(coldScripts.Capture()) &&
                coldState.Variable(Key(0x700), 31) == questHandle,
                "Fallback source-string restoration changed saved handles or execution.");
            using var coldRecords = FalloutPluginStack.Load(directory, ["SourceString.esm", "SourceStringPatch.esp"]);
            Require(coldRecords.NumericSettings.Get("fSourceNumber") == 3.5,
                "The argument capability save-baked a loaded-stack setting mutation.");
        }
        finally
        {
            File.Delete(Path.Combine(directory, "SourceString.esm")); File.Delete(Path.Combine(directory, "SourceStringPatch.esp"));
            Directory.Delete(directory);
        }
    }

    private static void StatementSetterRefusals()
    {
        foreach (var statement in new[]
        {
            "SetNumericGameSetting prefix (GetGameLoaded)",
            "SetNumericGameSetting fSourceNumber (GetGameLoaded) trailing",
            "SetNumericGameSetting fSourceNumber (GetGameLoaded) (1 +)",
        })
        {
            var directory = Path.Combine(Path.GetTempPath(), "opennv-source-string-statement-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var body = "prefix += 1\n" + statement + "\nprefix += 100\n";
                var plugin = Header().Concat(Setting(0x800, 3.5f))
                    .Concat(Script(0x100, body, 1, false))
                    .Concat(Script(0x102, body, 31, true))
                    .Concat(Function())
                    .Concat(Record("ACTI", 0x400, Field("SCRI", BitConverter.GetBytes(0x100u))))
                    .Concat(Record("ACTI", 0x401))
                    .Concat(Record("QUST", 0x700, Field("EDID", Text("SourceQuest")), Field("DATA", new byte[8]),
                        Field("SCRI", BitConverter.GetBytes(0x102u))))
                    .Concat(Cell()).ToArray();
                File.WriteAllBytes(Path.Combine(directory, "SourceString.esm"), plugin);
                using var records = FalloutPluginStack.Load(directory, ["SourceString.esm"]);
                var state = new FalloutQuestState(records);
                using var world = new FalloutReferenceWorld(records);
                world.LoadCell(FalloutCellSceneReader.Read(records, Key(0x600)));
                var referenceEvents = new FalloutScriptEvents(); referenceEvents.LoadGame();
                var executor = new FalloutReferenceScripts(records, world, state,
                    new((_, _) => false, _ => throw new InvalidDataException("A refused setter invented a presentation effect."),
                        Events: referenceEvents));
                var failed = executor.Dispatch(Key(0x500), "GameMode");
                Require(failed.Error is not null && executor.Dispatch(Key(0x500), "GameMode").Error == failed.Error &&
                    world.Get(Key(0x500)).Read(2) == 1 && records.NumericSettings.Get("fSourceNumber") == 3.5,
                    "Root statement refusal lost its failure/prefix or mutated the setting: " + statement);
                Require(referenceEvents.GetGameLoaded(Key(0x100)) && !referenceEvents.GetGameLoaded(Key(0x100)),
                    "A refused root statement consumed its later GetGameLoaded argument: " + statement);

                var questState = new FalloutQuestState(records); questState.SetRunning(Key(0x700), true);
                var questEvents = new FalloutScriptEvents(); questEvents.LoadGame();
                var scripts = new FalloutQuestScripts(records, questState, new HashSet<FalloutFormKey>(),
                    new FalloutPlayerInventory(), defaultProcessingDelay: 1, events: questEvents);
                scripts.Advance(1);
                var error = scripts.Capture().Instances.Single().Error;
                scripts.Advance(1);
                Require(error is not null && scripts.Capture().Instances.Single().Error == error &&
                    questState.Variable(Key(0x700), 32) == 1 && records.NumericSettings.Get("fSourceNumber") == 3.5,
                    "Fallback statement refusal lost its failure/prefix or mutated the setting: " + statement);
                Require(questEvents.GetGameLoaded(Key(0x102)) && !questEvents.GetGameLoaded(Key(0x102)),
                    "A refused fallback statement consumed its later GetGameLoaded argument: " + statement);
            }
            finally
            {
                File.Delete(Path.Combine(directory, "SourceString.esm")); Directory.Delete(directory);
            }
        }
    }

    private static T RoundTrip<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
    private static FalloutFormKey Key(uint id) => new("SourceString.esm", id);
    private static byte[] Script(uint id, string body, uint first, bool quest)
    {
        const string declarations = "string_var name\nshort prefix\nfloat literal\nfloat observed\nfloat frame\nfloat qualified\nshort status\n";
        var header = new byte[20]; header[16] = quest ? (byte)1 : (byte)0;
        var fields = new List<byte[]> { Field("EDID", Text("SourceStringScript" + id)), Field("SCHR", header),
            Field("SCTX", Text(declarations + "begin GameMode\n" + body + "end")),
            Field("SCRO", BitConverter.GetBytes(0x300u)), Field("SCRO", BitConverter.GetBytes(0x700u)) };
        AddLocals(fields, first, ["name", "prefix", "literal", "observed", "frame", "qualified", "status"]);
        return Record("SCPT", id, fields.ToArray());
    }
    private static byte[] Function()
    {
        var fields = new List<byte[]> { Field("EDID", Text("SourceStringUDF")), Field("SCHR", new byte[20]),
            Field("SCTX", Text("string_var selected\nbegin function {selected}\nSetFunctionValue GetNumericGameSetting selected\nend")) };
        AddLocals(fields, 21, ["selected"]);
        return Record("SCPT", 0x300, fields.ToArray());
    }
    private static void AddLocals(List<byte[]> fields, uint first, IReadOnlyList<string> names)
    {
        for (var index = 0; index < names.Count; ++index)
        {
            var local = new byte[24]; BinaryPrimitives.WriteUInt32LittleEndian(local, first + (uint)index);
            fields.Add(Field("SLSD", local)); fields.Add(Field("SCVR", Text(names[index])));
        }
    }
    private static byte[] Setting(uint id, float value) => Record("GMST", id,
        Field("EDID", Text("fSourceNumber")), Field("DATA", BitConverter.GetBytes(value)));
    private static byte[] Cell()
    {
        var children = Record("REFR", 0x500, Field("NAME", BitConverter.GetBytes(0x400u)), Field("DATA", new byte[24]))
            .Concat(Record("REFR", 0x501, Field("NAME", BitConverter.GetBytes(0x401u)), Field("DATA", new byte[24]))).ToArray();
        byte[] Group(int kind, byte[] data)
        {
            var group = new byte[24 + data.Length]; Encoding.ASCII.GetBytes("GRUP").CopyTo(group, 0);
            BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(4), (uint)group.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(8), 0x600); BinaryPrimitives.WriteInt32LittleEndian(group.AsSpan(12), kind);
            data.CopyTo(group, 24); return group;
        }
        return Record("CELL", 0x600, Field("DATA", [1])).Concat(Group(6, Group(9, children))).ToArray();
    }
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
        catch (Exception error) when (error is InvalidDataException or NotSupportedException or FalloutPluginFormatException) { return; }
        throw new InvalidDataException("An invalid source-string operation was admitted.");
    }
}
