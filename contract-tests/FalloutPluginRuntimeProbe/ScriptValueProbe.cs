using OpenNV.Runtime.Content;

internal static class ScriptValueProbe
{
    internal static void Run()
    {
        var values = new Dictionary<string, FalloutScriptValue>(StringComparer.OrdinalIgnoreCase)
        {
            ["index"] = 3,
            ["form"] = FalloutScriptValue.Form(0x14),
            ["path"] = string.Empty,
        };
        var writes = new List<string>();
        var context = new FalloutScriptValueContext(
            name => values.TryGetValue(name, out var value)
                ? value
                : throw new InvalidDataException($"Unbound typed value {name}."),
            (name, value) => { values[name] = value; writes.Add(name); },
            id => id == 0x14 ? "PlayerRef" : $"Unnamed:{id:x6}");

        Require(FalloutNvseNumericExpression.EvaluateValue(
                FalloutGameModeProgram.Tokens("\"Start/\" + $index + \"/enabled\""), context).Text ==
            "Start/3/enabled", "String concatenation or numeric string conversion changed.");
        Require(FalloutNvseNumericExpression.EvaluateValue(
                FalloutGameModeProgram.Tokens("\"Target/\" + $form"), context).Text ==
            "Target/PlayerRef", "Form string conversion used its runtime ID instead of its name.");
        Require(FalloutNvseNumericExpression.EvaluateValue(
                FalloutGameModeProgram.Tokens("ToString index"), context).Text == "3",
            "ToString did not preserve invariant numeric formatting.");
        Require(FalloutNvseNumericExpression.EvaluateValue(
                FalloutGameModeProgram.Tokens("\"start\" == \"START\""), context).Truth,
            "String comparison did not use the admitted case-insensitive source contract.");

        var sideEffects = 0;
        FalloutScriptFunction? Function(string name) => name.Equals("Touch", StringComparison.OrdinalIgnoreCase)
            ? FalloutScriptFunction.Typed([], _ => { ++sideEffects; return (FalloutScriptValue)"touched"; })
            : null;
        Require(FalloutNvseNumericExpression.EvaluateValue(
                FalloutGameModeProgram.Tokens("\"\" && Touch"), context, Function).Number == 0 &&
            sideEffects == 0, "An inactive typed logical branch invoked a function.");
        Require(FalloutNvseNumericExpression.EvaluateValue(
                FalloutGameModeProgram.Tokens("Echo \"ok\""), context,
                name => name.Equals("Echo", StringComparison.OrdinalIgnoreCase)
                    ? FalloutScriptFunction.Typed([FalloutScriptArgumentKind.String], args => args[0].Text + "!")
                    : null).Text == "ok!", "Typed string function arguments or results were lost.");

        var program = FalloutGameModeProgram.Read(
            "begin GameMode\nlet path := \"Start/\" + $index + \"/enabled\"\nset path to path + \"/changed\"\nend");
        program.Execute(name => values[name].Number, (name, value) => values[name] = value,
            (_, _) => throw new InvalidOperationException("Typed assignment executed an unexpected command."),
            values: context);
        Require(values["path"].Text == "Start/3/enabled/changed" && writes.SequenceEqual(["path", "path"]),
            "Typed let/set assignments did not reach the shared value owner.");

        var store = new FalloutScriptValueStore();
        var first = store.Write(FalloutScriptLocalKind.String, 0, "first", "Synthetic.esp");
        var second = store.Write(FalloutScriptLocalKind.String, 0, "second", "Synthetic.esp");
        Require(first != second && store.Read(FalloutScriptLocalKind.String, first).Text == "first" &&
            store.Read(FalloutScriptLocalKind.String, second).Text == "second",
            "String assignment aliased text that should have been copied.");
        var saved = store.Capture();
        var restored = new FalloutScriptValueStore();
        restored.Restore(saved);
        Require(restored.Read(FalloutScriptLocalKind.String, first).Text == "first" &&
            restored.Read(FalloutScriptLocalKind.String, second).Text == "second",
            "String handles did not survive a cold value-store restore.");
        restored.DestroyString(first);
        Reject(() => restored.Read(FalloutScriptLocalKind.String, first));
        Reject(() => restored.Restore(saved with { Strings = [saved.Strings[0] with { Id = 0 }] }));

        var resolved = FalloutGameModeProgram.ResolveCommandArguments(
            FalloutGameModeProgram.Tokens("(\"Start/\" + $index + \"/_enable\") 1"), context);
        Require(resolved.SequenceEqual(["\"Start/3/_enable\"", "1"]),
            "A constructed command path was split instead of reaching its owner as one value.");

        Require(FalloutGameModeProgram.WasRejectedByParser("set path to \"x\" + $index", 3) &&
            !FalloutGameModeProgram.WasRejectedByParser("set path to \"x\" + $index", 4),
            "Parser migration did not isolate the newly admitted string syntax.");
        Console.WriteLine("OPENNV_SCRIPT_VALUES_PASS typed=true strings=true forms=true concat=true udf=true coldStore=true commandGrouping=true");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidOperationException("An invalid typed script value was admitted.");
    }
}
