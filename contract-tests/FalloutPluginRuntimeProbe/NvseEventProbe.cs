using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class NvseEventProbe
{
    internal static void Run()
    {
        Loops();
        var directory = Path.Combine(Path.GetTempPath(), $"opennv-script-events-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllBytes(Path.Combine(directory, "Events.esm"), Fixture());
            using var records = FalloutPluginStack.Load(directory, ["Events.esm"]);
            using var world = new FalloutReferenceWorld(records);
            var quests = new FalloutQuestState(records);
            var events = new FalloutScriptEvents();
            var scripts = Executor(records, world, quests, events);
            var quest = records.GetEffective(new("Events.esm", 0x100));
            var definition = records.GetEffective(new("Events.esm", 0x200));
            var program = FalloutGameModeProgram.Read(definition.ReadSubrecords().Single(field => field.Signature == "SCTX").Data.Span);
            void Initialize() => scripts.ExecuteProgram(quest, definition, program, 0.1);
            FalloutFormKey Form(uint id) => new("Events.esm", id);
            var player = records.RuntimeFormKey(0x14);
            double Value(uint index) => quests.Variable(quest.FormKey, index);
            Initialize(); Initialize();
            Require(Value(1) == 1 && Value(2) == 0, "Restart/load calls are not consumptive per source script.");
            events.LoadGame(); Initialize(); Initialize();
            Require(Value(1) == 1 && Value(2) == 1, "Loading reset process restart consumption or repeated a load query.");
            Require(events.GetGameRestarted(Form(0x220)) && !events.GetGameRestarted(Form(0x220)) &&
                events.GetGameLoaded(Form(0x220)) && !events.GetGameLoaded(Form(0x220)), "Independent source consumers shared lifecycle state.");

            Require(scripts.InvokeFunction(Form(0x220), player, [5], 0.1) == 15 &&
                scripts.InvokeFunction(Form(0x220), player, [3], 0.1) == 6, "Recursive arguments, locals or return values leaked between calls.");
            Require(scripts.InvokeFunction(Form(0x225), player, [20], 0.1) == 90,
                "Reference parameter or PlayerRef alias did not reach the authoritative actor-value host.");
            Require(scripts.InvokeFunction(Form(0x226), player, [], 0.1) == 20 &&
                scripts.InvokeFunction(Form(0x226), null, [], 0.1) == 0, "Function caller identity was invented or lost.");
            Require(scripts.InvokeFunction(Form(0x227), player, [], 0.1) == 4, "SetFunctionValue incorrectly terminated the function.");
            Require(scripts.InvokeFunction(Form(0x228), player, [], 0.1) == 6,
                "Short-circuited function calls executed or lost their returned values.");
            Reject(() => scripts.InvokeFunction(Form(0x220), player, [], 0));
            Reject(() => scripts.InvokeFunction(Form(0x220), player, [double.NaN], 0));
            Reject(() => scripts.InvokeFunction(Form(0x220), player, [35], 0));
            Require(scripts.InvokeFunction(Form(0x220), player, [2], 0) == 3, "Recursion failure retained a poisoned call stack.");

            events.Advance(0.05, false, scripts.InvokeFunction);
            Require(Value(4) == 0, "GameMode-only callback ran while paused.");
            events.Advance(0.05, true, scripts.InvokeFunction);
            Require(Value(4) == 0, "Callback ignored its frame delay.");
            events.Advance(0.05, true, scripts.InvokeFunction);
            events.Advance(0.05, true, scripts.InvokeFunction);
            events.Advance(0.05, true, scripts.InvokeFunction);
            Require(Value(4) == 2 && Value(5) == 0.05, "Callbacks shared local frames or used quest cadence instead of frame cadence.");
            events.Key(42, true, scripts.InvokeFunction); events.Key(42, true, scripts.InvokeFunction);
            events.Key(45, true, scripts.InvokeFunction); events.Key(42, false, scripts.InvokeFunction);
            Require(Value(6) == 42 && Value(7) == 42 && !events.IsKeyPressed(42) && events.IsKeyPressed(45),
                "Key edges, repeats, argument identity or release handling differ.");
            scripts.InvokeFunction(Form(0x229), null, [], 0);
            events.Key(42, true, scripts.InvokeFunction);
            Require(Value(6) == 42, "Omitted-key removal did not remove the script's key registrations.");

            // Surviving callbacks contain identities, never delegates to the old
            // world. A fresh executor after load must mutate only restored state.
            var saved = JsonSerializer.Deserialize<FalloutQuestSnapshot[]>(JsonSerializer.Serialize(quests.Capture()))!;
            var restored = new FalloutQuestState(records); restored.Restore(saved);
            using var restoredWorld = new FalloutReferenceWorld(records);
            var restoredScripts = Executor(records, restoredWorld, restored, events);
            events.LoadGame();
            events.Advance(0.05, true, restoredScripts.InvokeFunction);
            events.Advance(0.05, true, restoredScripts.InvokeFunction);
            Require(Value(4) == 2 && restored.Variable(quest.FormKey, 4) == 3 &&
                !events.GetGameRestarted(Form(0x200)) && events.GetGameLoaded(Form(0x200)),
                "Cold owner replacement retained stale delegates or reset process lifecycle.");

            CallbackMutation(events, scripts, Form, player);
            Failure(events, scripts, quests, quest.FormKey, Form, player);
            FallbackLifecycle(records);
            ReferenceTypedStrings(directory);
        }
        finally
        {
            File.Delete(Path.Combine(directory, "Events.esm"));
            File.Delete(Path.Combine(directory, "StringEvents.esm"));
            Directory.Delete(directory);
        }
        Console.WriteLine("OPENNV_NVSE_EVENTS_PASS lifecycle=per-script functions=isolated recursion=true loops=true callbacks=frame-and-key reload=rebound failure=visible typedStrings=true");
    }

    private static void ReferenceTypedStrings(string directory)
    {
        var data = new byte[8]; data[0] = 1; BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(4), 0.01f);
        var quest = Record("QUST", 0x100, Field("EDID", Text("StringQuest"))
            .Concat(Field("DATA", data)).Concat(Field("SCRI", BitConverter.GetBytes(0x240u))).ToArray());
        var source = "string_var text\nstring_var copy\nbegin Result\n" +
            "text = call StringFunction \"reference/owner\"\ncopy = text\ntext += \"/changed\"\nend";
        File.WriteAllBytes(Path.Combine(directory, "StringEvents.esm"),
            Record("TES4", 0, Field("HEDR", new byte[12])).Concat(quest)
                .Concat(Script(0x240, "StringProgram", source, ["text", "copy"], referenceForms: [0x241]))
                .Concat(Script(0x241, "StringFunction", "string_var value\nbegin Function {value}\nvalue += \"/udf\"\nSetFunctionValue value\nend", ["value"], referenceForms: []))
                .ToArray());
        using var records = FalloutPluginStack.Load(directory, ["StringEvents.esm"]);
        using var world = new FalloutReferenceWorld(records);
        var quests = new FalloutQuestState(records);
        var typedScripts = Executor(records, world, quests, new FalloutScriptEvents());
        var questRecord = records.GetEffective(new FalloutFormKey("StringEvents.esm", 0x100));
        var script = records.GetEffective(new FalloutFormKey("StringEvents.esm", 0x240));
        var program = FalloutGameModeProgram.Read(
            script.ReadSubrecords().Single(field => field.Signature == "SCTX").Data.Span, "Result");
        typedScripts.ExecuteProgram(questRecord, script, program, 0);
        var textHandle = quests.Variable(questRecord.FormKey, 1);
        var copyHandle = quests.Variable(questRecord.FormKey, 2);
        Require(textHandle != copyHandle &&
            world.ScriptValues.Read(FalloutScriptLocalKind.String, textHandle).Text == "reference/owner/udf/changed" &&
            world.ScriptValues.Read(FalloutScriptLocalKind.String, copyHandle).Text == "reference/owner/udf",
            "The reference executor did not persist typed string locals through its shared world owner.");
    }

    private static FalloutReferenceScripts Executor(FalloutPluginStack records, FalloutReferenceWorld world,
        FalloutQuestState quests, FalloutScriptEvents events) => new(records, world, quests,
            new((_, _) => throw new InvalidOperationException("Unexpected furniture query."),
                _ => throw new InvalidOperationException("Unexpected presentation effect."),
                ActorValue: (actor, value) => records.RuntimeFormId(actor) == 0x14 && value == "Health" ? 70 :
                    throw new InvalidOperationException("Actor query has the wrong caller or value."), Events: events));

    private static void CallbackMutation(FalloutScriptEvents events, FalloutReferenceScripts scripts,
        Func<uint, FalloutFormKey> form, FalloutFormKey player)
    {
        var order = new List<uint>();
        events.SetMainLoop(form(0x230), player, true, modes: 3);
        events.SetMainLoop(form(0x231), player, true, modes: 11);
        double Invoke(FalloutFormKey script, FalloutFormKey? caller, IReadOnlyList<double> arguments, double seconds)
        {
            if (script.ObjectId < 0x230) return scripts.InvokeFunction(script, caller, arguments, seconds);
            order.Add(script.ObjectId);
            events.SetMainLoop(form(0x230), player, false);
            events.SetMainLoop(form(0x231), player, false);
            events.SetMainLoop(form(0x232), player, true, modes: 2);
            return 0;
        }
        events.Advance(0.1, false, Invoke);
        Require(order.SequenceEqual([0x230u]), "Callback mutation ran a removed handler or recursively ran a new registration.");
        events.Advance(0.1, false, Invoke);
        Require(order.SequenceEqual([0x230u, 0x232u]), "New callback did not enter the next eligible frame.");
        events.SetMainLoop(form(0x232), player, false);
        events.SetMainLoop(form(0x231), player, true, modes: 11);
        events.EnterMainMenu();
        events.Advance(0.1, false, Invoke);
        Require(order.Count == 2, "Main menu retained an auto-remove callback.");
        events.SetMainLoop(form(0x231), player, true, modes: 11);
        events.LoadGame(); events.Advance(0.1, false, Invoke);
        Require(order.Count == 2, "Loading retained an auto-remove callback.");
    }

    private static void Failure(FalloutScriptEvents events, FalloutReferenceScripts scripts, FalloutQuestState quests,
        FalloutFormKey quest, Func<uint, FalloutFormKey> form, FalloutFormKey player)
    {
        events.SetMainLoop(form(0x233), player, true);
        events.Advance(0.1, false, scripts.InvokeFunction); events.Advance(0.1, false, scripts.InvokeFunction);
        Require(quests.Variable(quest, 8) == 1 && JsonSerializer.Serialize(events.State).Contains("MissingCommand", StringComparison.Ordinal),
            "Faulted callback lost its prefix, retried or hid the error.");
        events.SetMainLoop(form(0x233), player, false);
        Reject(() => events.SetMainLoop(form(0x231), player, true, 0));
        Reject(() => events.SetMainLoop(form(0x231), player, true, modes: 4));
        Reject(() => events.SetKey(form(0x231), player, true, true, null));
    }

    private static void FallbackLifecycle(FalloutPluginStack records)
    {
        var quests = new FalloutQuestState(records);
        var quest = new FalloutFormKey("Events.esm", 0x100);
        quests.SetVariable(quest, 3, 1); // Skip registration: this host has no world executor.
        var events = new FalloutScriptEvents(); events.LoadGame();
        var scripts = new FalloutQuestScripts(records, quests, new HashSet<FalloutFormKey>(), new FalloutPlayerInventory(),
            defaultProcessingDelay: 0.01f, events: events);
        for (var i = 0; i < 3; ++i) scripts.Advance(1);
        Require(quests.Variable(quest, 1) == 1 && quests.Variable(quest, 2) == 1 &&
            scripts.Capture().Instances.Single().Error is null, "Fallback quest executor has different lifecycle semantics.");
    }

    private static void Loops()
    {
        var values = new Dictionary<string, double> { ["i"] = 0, ["j"] = 0, ["total"] = 0 };
        void Run(string body, int limit = 100_000) => FalloutGameModeProgram.Read("begin GameMode\n" + body + "\nend")
            .Execute(name => values[name], (name, value) => values[name] = value,
                (_, _) => throw new InvalidOperationException("Unexpected host command."), budget: new(limit));
        Run("while i < 5\ni += 1\nif i == 2\ncontinue\nendif\nj = 0\nwhile j < 3\nj += 1\nif j == 2\nbreak\nendif\ntotal += i\nloop\nloop");
        Require(values["total"] == 13 && values["i"] == 5 && values["j"] == 2, "Nested break/continue damaged loop or branch state.");
        Run("if 0\nwhile missing\nUnknownCommand\nloop\nelse\ntotal += 1\nendif\nwhile 0\nUnknownCommand\nloop");
        Require(values["total"] == 14, "Inactive loops executed unknown operands or lost the parent branch.");
        Reject(() => Run("while 1\ntotal += 1\nloop", 15));
        foreach (var body in new[] { "loop", "break", "while 1\nendif\nloop", "if 1\nwhile 1\nendif\nloop", "if 1\nelse\nelse\nendif" })
            Reject(() => FalloutGameModeProgram.Read("begin GameMode\n" + body + "\nend"));
        Reject(() => FalloutGameModeProgram.ReadEvents("begin Function {x x}\nend"));
        Reject(() => FalloutGameModeProgram.ReadEvents("begin Function x\nend"));
    }

    private static byte[] Fixture()
    {
        var header = new byte[12]; BinaryPrimitives.WriteSingleLittleEndian(header, 1.34f);
        var data = new byte[8]; data[0] = 1; BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(4), 0.01f);
        var fields = Field("EDID", Text("ProbeQuest")).Concat(Field("DATA", data)).Concat(Field("SCRI", BitConverter.GetBytes(0x200u))).ToArray();
        var bytes = Record("TES4", 0, Field("HEDR", header)).Concat(Record("QUST", 0x100, fields));
        const string main = "begin GameMode\nif GetGameRestarted\nrestarts += 1\nendif\nif GetGameLoaded\nloads += 1\nendif\n" +
            "if initialized == 0\nSetGameMainLoopCallback Tick 1 2 1\nSetOnKeyDownEventHandler KeyDown 1 42\nSetOnKeyUpEventHandler KeyUp 1 42\ninitialized = 1\nendif\nend";
        bytes = bytes.Concat(Script(0x200, "Main", main, ["restarts", "loads", "initialized", "total", "elapsed", "keyDown", "keyUp", "failed"], quest: true));
        bytes = bytes.Concat(Script(0x210, "Tick", "int scratch\nbegin Function {}\nscratch += 1\nProbeQuest.total += scratch\nProbeQuest.elapsed = GetSecondsPassed\nend", ["scratch"]));
        bytes = bytes.Concat(Script(0x211, "KeyDown", "int key\nbegin Function {key}\nProbeQuest.keyDown += key\nend", ["key"]));
        bytes = bytes.Concat(Script(0x212, "KeyUp", "int key\nbegin Function {key}\nProbeQuest.keyUp += key\nend", ["key"]));
        bytes = bytes.Concat(Script(0x220, "Sum", "int n\nint subtotal\nbegin Function {n}\nif n > 0\nsubtotal = call Sum (n - 1)\nSetFunctionValue (n + subtotal)\nendif\nend", ["n", "subtotal"]));
        bytes = bytes.Concat(Script(0x225, "Reference", "ref actor\nbegin Function {actor}\nif actor == PlayerRef\nSetFunctionValue (actor.GetAV Health + 20)\nendif\nend", ["actor"]));
        bytes = bytes.Concat(Script(0x226, "Caller", "begin Function {}\nSetFunctionValue GetSelfAlt\nend", []));
        bytes = bytes.Concat(Script(0x227, "Returns", "begin Function {}\nSetFunctionValue 3\nSetFunctionValue 4\nend", []));
        bytes = bytes.Concat(Script(0x228, "ShortCircuit", "int n\nbegin Function {}\nn = 1 || call Fault\nn = 0 && call Fault\nSetFunctionValue (call Sum 3)\nend", ["n"]));
        bytes = bytes.Concat(Script(0x229, "RemoveKeys", "begin Function {}\nSetOnKeyDownEventHandler KeyDown 0\nend", []));
        bytes = bytes.Concat(Script(0x233, "Fault", "begin Function {}\nProbeQuest.failed += 1\nMissingCommand\nProbeQuest.failed += 10\nend", []));
        return bytes.ToArray();
    }

    private static byte[] Script(uint id, string name, string source, string[] locals, bool quest = false,
        IReadOnlyList<uint>? referenceForms = null)
    {
        var header = new byte[20]; BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), (uint)locals.Length);
        if (quest) header[16] = 1;
        var fields = Field("EDID", Text(name)).Concat(Field("SCHR", header)).Concat(Field("SCTX", Text(source)));
        foreach (var form in referenceForms ?? [0x14u, 0x100u, 0x210u, 0x211u, 0x212u, 0x220u, 0x233u])
            fields = fields.Concat(Field("SCRO", BitConverter.GetBytes(form)));
        for (var i = 0; i < locals.Length; ++i)
        {
            var slot = new byte[24]; BinaryPrimitives.WriteUInt32LittleEndian(slot, (uint)i + 1);
            fields = fields.Concat(Field("SLSD", slot)).Concat(Field("SCVR", Text(locals[i])));
        }
        return Record("SCPT", id, fields.ToArray());
    }
    private static byte[] Text(string text) => Encoding.ASCII.GetBytes(text + '\0');
    private static byte[] Field(string name, byte[] data)
    {
        var bytes = new byte[data.Length + 6]; Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), checked((ushort)data.Length)); data.CopyTo(bytes, 6); return bytes;
    }
    private static byte[] Record(string name, uint id, byte[] data)
    {
        var bytes = new byte[data.Length + 24]; Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)data.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), id); data.CopyTo(bytes, 24); return bytes;
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidOperationException("Invalid script operation was admitted.");
    }
}
