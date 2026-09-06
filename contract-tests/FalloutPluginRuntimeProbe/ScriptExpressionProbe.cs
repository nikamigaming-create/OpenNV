using OpenNV.Runtime.Content;

internal static class ScriptExpressionProbe
{
    internal static void Run()
    {
        var calls = new List<string>();
        FalloutScriptFunction? Function(string name) => name.ToLowerInvariant() switch
        {
            "state" => new([FalloutScriptArgumentKind.Identifier], args =>
            {
                calls.Add("state:" + args[0].Identifier);
                return args[0].Identifier == "Example" ? 7 : throw new InvalidDataException("Unexpected source identity.");
            }),
            "measure" => new([FalloutScriptArgumentKind.Number], args =>
            {
                calls.Add("measure"); return Math.Abs(args[0].Number);
            }),
            "elapsed" => new([], _ => { calls.Add("elapsed"); return 0.25; }),
            "invalid" => new([], _ => double.NaN),
            _ => null,
        };
        double Evaluate(string expression) => FalloutGameModeProgram.Evaluate(FalloutGameModeProgram.Tokens(expression),
            name => name == "offset" ? 2 : throw new InvalidOperationException("Unbound variable: " + name), Function);
        Require(Evaluate("State Example == 7 && Measure (offset - 5) + Elapsed * 4 == 4") == 1 &&
            calls.SequenceEqual(["state:Example", "measure", "elapsed"]), "Function arguments, precedence or invocation order differ.");
        calls.Clear();
        Require(Evaluate("0 && State Example || 1 || Measure (-3)") == 1 && calls.Count == 0,
            "Short circuit invoked an inactive function.");
        Require(Evaluate("Measure -2 + 3") == 5 && Evaluate("Measure (Measure -2 - 4)") == 2,
            "A numeric function argument consumed an outer operator or nested expression incorrectly.");
        Reject(() => Evaluate("State 7"));
        Reject(() => Evaluate("Measure"));
        Reject(() => Evaluate("Invalid == 0"));
        var source = "begin GameMode\nif State Example == 7\nset result to Measure (offset - 5)\nelse\nUnbound Unreached\nendif\nend\nbegin MenuMode 1234\nset result to 19\nend";
        var result = 0d;
        void Execute(FalloutGameModeProgram program) => program.Execute(_ => 2, (_, value) => result = value,
            (_, _) => throw new InvalidOperationException("Inactive command executed."), Function);
        Execute(FalloutGameModeProgram.Read(source));
        Require(result == 3, "The reached source branch lost its function expression.");
        Execute(FalloutGameModeProgram.Read(source, "MenuMode", 1234));
        Require(result == 19, "The declared menu event did not execute.");
        Execute(FalloutGameModeProgram.Read(source, "MenuMode", 4321));
        Require(result == 19, "An unrelated menu event executed.");
        Console.WriteLine("OPENNV_SCRIPT_EXPRESSION_PASS functions=true arguments=true precedence=true inactiveCalls=false events=true");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is NotSupportedException or InvalidDataException) { return; }
        throw new InvalidOperationException("An invalid script expression was admitted.");
    }
}
