using System.Text.Json;
using OpenNV.Runtime.Content;

internal static class ScriptArrayProbe
{
    internal static void Run()
    {
        var store = new FalloutScriptValueStore();
        var locals = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        { ["list"] = 0, ["alias"] = 0, ["copy"] = 0, ["deep"] = 0, ["map"] = 0, ["names"] = 0, ["nested"] = 0 };
        FalloutScriptValue Read(string name) => store.Read(FalloutScriptLocalKind.Array, locals[name]);
        void Write(string name, FalloutScriptValue value) => locals[name] = store.Write(
            FalloutScriptLocalKind.Array, locals[name], value, "Synthetic.esp", "fixture:" + name);
        var context = new FalloutScriptValueContext(Read, Write, Arrays: store.Arrays);
        void Execute(string body) => FalloutGameModeProgram.Read("begin GameMode\n" + body + "\nend")
            .Execute(name => Read(name).Number, (name, value) => Write(name, value),
                (_, _) => throw new InvalidOperationException("Array command escaped to a gameplay host."), values: context);
        FalloutScriptValue Evaluate(string expression, Func<string, FalloutScriptFunction?>? function = null) =>
            FalloutNvseNumericExpression.EvaluateValue(FalloutGameModeProgram.Tokens(expression), context, function);

        Execute("list = Ar_List 4 \"text\"\nalias = list\nalias[0] += 3\nAr_Append list 9\ncopy = Ar_Copy list\ncopy[0] = 42");
        Require(Evaluate("list[0]").Number == 7 && Evaluate("alias[0]").Number == 7 &&
            Evaluate("copy[0]").Number == 42 && Evaluate("list[1]").Text == "text" &&
            Evaluate("Ar_Size list").Number == 3, "Packed arrays lost alias identity, mixed values or shallow copy isolation.");
        Execute("map = Ar_Construct \"map\"\nmap[-1.5] = 6\nmap[200] = \"sparse\"\n" +
            "names = Ar_Construct \"stringmap\"\nnames[\"Index\"] = list\n" +
            "nested = Ar_List names\nnested[0][\"INDEX\"][0] += 1\n" +
            "deep = Ar_DeepCopy nested\ndeep[0][\"index\"][0] = 90");
        Require(Evaluate("map[-1.5]").Number == 6 && Evaluate("map[200]").Text == "sparse" &&
            Evaluate("names[\"index\"][0]").Number == 8 && Evaluate("deep[0][\"index\"][0]").Number == 90 &&
            Evaluate("TypeOf names").Text == "StringMap" && Evaluate("TypeOf map").Text == "Map" &&
            Evaluate("TypeOf list").Text == "Array", "Map keys, case-insensitive string keys, nested indexing or deep copies differ.");
        Execute("Ar_Erase list 1\nAr_Resize list 4 \"padding\"");
        Require(Evaluate("list[1]").Number == 9 && Evaluate("list[3]").Text == "padding" &&
            Evaluate("Ar_HasKey list 4").Number == 0, "Packed erase or resize did not conserve and reindex the retained values.");

        var calls = 0;
        FalloutScriptFunction? Function(string name) => name == "Next"
            ? new([], _ => { ++calls; return 0; }) : null;
        Require(Evaluate("list[Next] += 1", Function).Number == 9 && calls == 1,
            "An indexed compound assignment evaluated its location more than once.");
        Require(Evaluate("0 && (list[Next] := 0)", Function).Number == 0 && calls == 1,
            "An inactive branch evaluated an array index.");
        foreach (var expression in new[] { "list[Next] := 1 +", "list[] := 3", "list[0 := 4", "list[99] := 2",
            "list[0.5] := 2", "names[0] := 1", "list[\"0\"] := 1", "list + 1", "list == 1", "-list" })
            Reject(() => Evaluate(expression, Function));
        Require(calls == 1 && Evaluate("list[0]").Number == 9, "Invalid array syntax committed an index or assignment.");
        foreach (var kind in new[] { FalloutScriptLocalKind.Number, FalloutScriptLocalKind.Form, FalloutScriptLocalKind.String })
            Reject(() => store.Write(kind, 0, Read("list"), "Synthetic.esp"));
        Reject(() => Evaluate("Ar_Resize list 2147483647"));
        Require(Evaluate("Ar_Size list").Number == 4, "Rejected oversized resize partially changed the live array.");
        Require(FalloutGameModeProgram.ResolveCommandArguments(FalloutGameModeProgram.Tokens(
            "\"HUD/value\" names[\"index\"][0] 1"), context).SequenceEqual(["\"HUD/value\"", "9", "1"]),
            "A bare indexed command argument lost grouping before reaching its gameplay owner.");

        // Cycles are legal identity graphs. Root reclamation must not depend on
        // reference counting alone or recursive traversal of their elements.
        Execute("map[201] = map");
        var saved = JsonSerializer.Deserialize<FalloutScriptValueStoreSnapshot>(JsonSerializer.Serialize(store.Capture()))!;
        var cold = new FalloutScriptValueStore();
        cold.Restore(saved);
        foreach (var (name, raw) in locals) cold.ValidateLocal(FalloutScriptLocalKind.Array, raw, "fixture:" + name);
        cold.Arrays.ValidateRestoredRoots();
        Require(cold.Arrays.Get(cold.Read(FalloutScriptLocalKind.Array, locals["names"]), "INDEX").Number == locals["list"] &&
            cold.Arrays.Get(cold.Read(FalloutScriptLocalKind.Array, locals["map"]), 201).Number == locals["map"],
            "Serialized restoration copied aliases or lost cyclic/nested identities.");
        var before = JsonSerializer.Serialize(cold.Capture());
        var first = saved.Arrays![0];
        Reject(() => cold.Restore(saved with { Arrays = [first with { Elements = first.Elements.Concat([first.Elements[0]]).ToArray() }] }));
        Reject(() => cold.Restore(saved with { Arrays = saved.Arrays.Select(array => array.Id == first.Id
            ? array with { Elements = [new(new(FalloutScriptValueKind.Number, Number: 0), new(FalloutScriptValueKind.Array, Array: uint.MaxValue - 1))] }
            : array).ToArray() }));
        Require(JsonSerializer.Serialize(cold.Capture()) == before, "Malformed array restoration partially replaced live storage.");
        var unowned = new FalloutScriptValueStore();
        unowned.Restore(saved);
        Reject(unowned.Arrays.ValidateRestoredRoots);
        Reject(() => cold.Write(FalloutScriptLocalKind.Array, 0, locals["list"], "Synthetic.esp", "bad"));
        Execute(string.Join('\n', locals.Keys.Select(name => name + " = Ar_Null")));
        Require(store.Arrays.Count == 0 && Evaluate("Ar_Size list").Number == -1 &&
            Evaluate("Ar_HasKey list 0").Number == 0, "Clearing declared roots leaked aliases, nested graphs or cycles.");
        Require(FalloutGameModeProgram.WasRejectedByParser("list[0] = 1", 5) &&
            !FalloutGameModeProgram.WasRejectedByParser("Notify \"list[0]\" ; list[0] = 1", 5) &&
            !FalloutGameModeProgram.WasRejectedByParser("list[0] = 1", 6), "Indexed-expression migration admitted unrelated owners.");
        Console.WriteLine("OPENNV_SCRIPT_ARRAYS_PASS aliases=true typedKeys=true nested=true indexOnce=true cold=true malformedAtomic=true cycleCleanup=true");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidOperationException("Invalid script array operation was admitted.");
    }
}
