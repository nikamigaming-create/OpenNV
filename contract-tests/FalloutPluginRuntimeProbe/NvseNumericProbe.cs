using OpenNV.Runtime.Content;

internal static class NvseNumericProbe
{
    internal static void Run()
    {
        var state = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        { ["charge"] = 12, ["result"] = 0, ["timer"] = 0, ["other"] = 0 };
        var writes = new List<string>();
        var calls = 0;
        double Read(string name) => state.TryGetValue(name, out var value) ? value : throw new NotSupportedException(name);
        void Write(string name, double value) { _ = Read(name); state[name] = value; writes.Add(name); }
        FalloutScriptFunction? Function(string name) => name.ToLowerInvariant() switch
        {
            "tick" => new([], _ => { ++calls; return 0.25; }),
            "measure" => new([FalloutScriptArgumentKind.Number], args => { ++calls; return Math.Abs(args[0].Number); }),
            "invalid" => new([], _ => double.NaN),
            _ => null,
        };
        double Evaluate(string expression) => FalloutNvseNumericExpression.Evaluate(
            FalloutGameModeProgram.Tokens(expression), Read, Write, Function);
        void Execute(string statements) => FalloutGameModeProgram.Read("begin GameMode\n" + statements + "\nend")
            .Execute(Read, Write, (_, _) => throw new InvalidOperationException("An assignment escaped to the command host."), Function);

        Execute("let result := other := 2 + 3 * 4\ncharge -= 2\ncharge *= 3\ncharge /= 5\ncharge += 1");
        Require(state["result"] == 14 && state["other"] == 14 && state["charge"] == 7 &&
            writes.Take(2).SequenceEqual(["other", "result"]), "Numeric assignment ordering or arithmetic differs.");
        writes.Clear();
        Execute("timer = Tick\nresult = Measure (charge - 10) + 1\nresult += Measure -2");
        Require(state["timer"] == 0.25 && state["result"] == 6 && calls == 3,
            "Function arguments or invocation count differ in numeric assignments.");
        Require(Evaluate("0 || -7") == -7 && Evaluate("3 || 8") == 3 && Evaluate("-2 && -9") == -9 &&
            Evaluate("0 && 9") == 0, "NVSE logical operators discarded their numeric operand values.");
        Require(FalloutGameModeProgram.Evaluate(FalloutGameModeProgram.Tokens("0 || -7"), Read) == 1,
            "NVSE rules changed vanilla boolean results.");

        calls = 0; writes.Clear();
        Require(Evaluate("1 || (timer := Tick)") == 1 && Evaluate("0 && (timer := Tick)") == 0 &&
            Evaluate("1 || missing") == 1 && Evaluate("0 && missing") == 0 && calls == 0 && writes.Count == 0,
            "Short-circuited expressions read, wrote or invoked inactive state.");
        Execute("if eval (other := 4) > 2\nresult = other\nelseif eval (other := Tick)\nresult = 99\nendif\n" +
            "if 0\nlet timer := Tick\nelse\nresult += 2\nendif");
        Require(state["result"] == 6 && state["other"] == 4 && calls == 0,
            "Eval conditions, assignment results or inactive branches differ.");
        Require(Evaluate("charge += 0 || 9") == 7 && state["charge"] == 7,
            "Compound assignment lost its documented precedence relative to logical OR.");
        writes.Clear(); calls = 0;
        foreach (var invalid in new[] { "result := Tick 3", "result := 1 +", "2 := Tick", "result := (3 + 4",
            "result := 1 / 0", "result := 1e308 * 1e308 < 1", "result := Invalid", "result := \"text\"", "result := unknown" })
            Reject(() => Evaluate(invalid));
        Require(writes.Count == 0 && calls == 0, "An invalid expression committed state or ran a function before syntax validation.");
        foreach (var source in new[] { "charge = 1", "let charge := 1", "charge += 1", "charge /= 2" })
            Require(FalloutGameModeProgram.WasRejectedByParser(source, 1), "Legacy migration lost newly supported assignment syntax.");
        Require(!FalloutGameModeProgram.WasRejectedByParser("Notify \"a=b\" ; charge += 1", 1) &&
            !FalloutGameModeProgram.WasRejectedByParser("if charge == 2\nendif", 1) &&
            !FalloutGameModeProgram.WasRejectedByParser("charge = 1", 2), "Migration admitted an unrelated missing owner.");
        Console.WriteLine("OPENNV_NVSE_NUMERIC_PASS assignments=true eval=true numericLogic=true inactiveEffects=false vanillaPreserved=true");
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is NotSupportedException or InvalidDataException) { return; }
        throw new InvalidOperationException("An invalid NVSE numeric expression was admitted.");
    }
}
