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
        Require(FalloutGameModeProgram.Tokens("Notify \"one, two\", Example, -2")
            .SequenceEqual(["Notify", "\"one, two\"", "Example", "-", "2"]),
            "Optional argument commas changed quoted text or signed arguments.");
        Require(Evaluate("State, Example == 7 && Measure, -2 == 2") == 1,
            "Optional commas prevented source function evaluation.");
        var separated = new List<string>();
        FalloutGameModeProgram.Read("begin GameMode\nNotify Example,1,2\nend")
            .Execute(_ => 0, (_, _) => { }, (name, arguments) => separated.Add(name + ":" + string.Join('|', arguments)));
        Require(separated.SequenceEqual(["Notify:Example|1|2"]), "Comma-separated command arguments changed order.");
        Reject(() => FalloutGameModeProgram.Tokens("Notify Example@, 1"));
        Reject(() => Evaluate("1, 2"));
        Require(FalloutGameModeProgram.HasArgumentSeparator("Notify One, 2") &&
            !FalloutGameModeProgram.HasArgumentSeparator("Notify \"one, two\" ; comment, too\nNotify One"),
            "Legacy comma migration confused quoted/comment text with argument syntax.");
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
        DigitLeadingForms();
        Console.WriteLine("OPENNV_SCRIPT_EXPRESSION_PASS functions=true arguments=true precedence=true inactiveCalls=false events=true");
    }

    private static void DigitLeadingForms()
    {
        var reads = 0;
        var calls = 0;
        var names = new HashSet<string>(["1EActor", "2ERoom", "3CReference.GetValue"], StringComparer.OrdinalIgnoreCase);
        var values = new FalloutScriptValueContext(name =>
        {
            ++reads;
            return names.Contains(name) ? 17 : throw new InvalidDataException("Unbound fixture identity.");
        }, (_, _) => throw new InvalidOperationException("Unexpected write."));
        FalloutScriptFunction? Function(string name) => name == "SameForm"
            ? new([FalloutScriptArgumentKind.Identifier], arguments =>
            {
                ++calls;
                return names.Contains(arguments[0].Identifier!) || arguments[0].Identifier is "NaN" or "Infinity"
                    ? 17 : throw new InvalidDataException("Wrong form argument.");
            }) : null;
        foreach (var name in names)
        {
            var tokens = FalloutGameModeProgram.Tokens(name + " == 17 && SameForm " + name + " == 17");
            Require(tokens[0] == name, "Digit-leading editor ID was split into numeric tokens.");
            var inspected = new List<string>();
            var beforeReads = reads;
            var beforeCalls = calls;
            FalloutNvseNumericExpression.Inspect(tokens, values, Function, null, inspected.Add);
            Require(inspected.SequenceEqual([name]) && reads == beforeReads && calls == beforeCalls,
                "Digit-leading form inspection invoked gameplay or lost its identity.");
            Require(FalloutNvseNumericExpression.EvaluateValue(tokens, values, Function).Number == 1 &&
                reads == beforeReads + 1 && calls == beforeCalls + 1,
                "Typed expression refused a complete digit-leading source identity.");
            Require(FalloutGameModeProgram.Evaluate(tokens, operand => values.Read(operand).Number, Function) == 1,
                "Legacy identifier argument disagrees with typed source identity.");
        }
        var literal = FalloutGameModeProgram.Tokens("1e3 + 2E-2 + 0x10 + 0b10");
        Require(FalloutNvseNumericExpression.EvaluateValue(literal, values).Number == 1018.02,
            "Editor ID admission changed scientific or based numeric literal semantics.");
        foreach (var numeric in new[] { "17", "1e3", "0x10", "0b10" })
            Reject(() => FalloutNvseNumericExpression.EvaluateValue(["SameForm", numeric], values, Function));
        Reject(() => FalloutGameModeProgram.Evaluate(["SameForm", "1e3"], _ => 0, Function));
        Reject(() => FalloutNvseNumericExpression.EvaluateValue(["0xWrong"], values));
        foreach (var name in new[] { "NaN", "Infinity" })
            Require(FalloutNvseNumericExpression.EvaluateValue(["SameForm", name], values, Function).Number == 17 &&
                FalloutGameModeProgram.Evaluate(["SameForm", name], _ => 0, Function) == 17,
                "Digit-leading disambiguation changed existing letter-leading identifier arguments.");
        var savedReads = reads;
        Require(FalloutNvseNumericExpression.EvaluateValue(["0", "&&", "1EActor"], values).Number == 0 && reads == savedReads,
            "Inactive digit-leading operand read gameplay state.");
        Console.WriteLine("OPENNV_DIGIT_LEADING_SCRIPT_FORMS_PASS typed=true legacy=true inspection=true literalsUnchanged=true inactiveReads=false");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is NotSupportedException or InvalidDataException) { return; }
        throw new InvalidOperationException("An invalid script expression was admitted.");
    }
}
