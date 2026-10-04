using OpenNV.Runtime.Content;

internal static class ScriptInspectionContracts
{
    internal static void Run()
    {
        var calls = 0;
        var reads = 0;
        var writes = 0;
        var symbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        FalloutScriptFunction? Function(string name) => name.Equals("ConsumeState", StringComparison.OrdinalIgnoreCase)
            ? new([], _ => { ++calls; throw new InvalidOperationException("Inspection invoked a function."); }) : null;
        var values = new FalloutScriptValueContext(_ => { ++reads; throw new InvalidOperationException("Inspection read state."); },
            (_, _) => { ++writes; throw new InvalidOperationException("Inspection wrote state."); });
        var context = new FalloutScriptInspectionContext(values, Function,
            (_, _) => throw new NotSupportedException("No user function in this fixture."));
        var program = FalloutGameModeProgram.Read("begin GameMode\n" +
            "if 0 && ConsumeState\nset counter to MissingRead\nelseif ConsumeState\n" +
            "set counter to ConsumeState\nelse\nRewardItem player\nendif\n" +
            "while MissingLoop\nlet counter := ConsumeState\nbreak\nloop\nend");
        var rows = program.Inspect(context, name =>
        {
            symbols.Add(name);
            if (!name.Equals("counter", StringComparison.OrdinalIgnoreCase))
                throw new NotSupportedException("Unbound fixture symbol: " + name);
        });
        if (rows.Count != 11 || rows.Count(row => row.PredicateOutcomes == 2) != 3 ||
            rows.Count(row => row.Error is not null) != 2 ||
            rows.Single(row => row.Operation == "rewarditem").Ownership != "statement-dispatch-not-inspected" ||
            !symbols.SetEquals(["counter", "MissingRead", "MissingLoop"]) || calls != 0 || reads != 0 || writes != 0)
            throw new InvalidDataException("Static inspection lost an inactive branch, loop, fault or ownership boundary.");
        var malformed = FalloutGameModeProgram.Read("begin GameMode\nif 0\nset counter to ( 1 + )\nendif\nend")
            .Inspect(context, _ => { });
        if (malformed.Count(row => row.Error is not null) != 1 || reads != 0 || writes != 0 || calls != 0)
            throw new InvalidDataException("Inactive malformed expression escaped inspection or changed state.");
        var deferred = values with { HasValueOwner = _ => throw new InvalidOperationException("Inactive source-string declaration was read.") };
        FalloutScriptFunction? SourceString(string name) => name == "SourceName"
            ? new([FalloutScriptArgumentKind.SourceString], _ => throw new InvalidOperationException("Inactive function was called.")) : null;
        if (FalloutNvseNumericExpression.EvaluateValue(["0", "&&", "SourceName", "UnboundBareName"],
            deferred, SourceString).Number != 0)
            throw new InvalidDataException("Ordinary inactive source-string expression changed its short circuit.");
        Console.WriteLine("OPENNV_SCRIPT_INSPECTION_CONTRACT_PASS everyArm=true loopPredicate=true noInvocation=true noStateRead=true noStateWrite=true retainedFailures=true statementBoundary=true");
    }
}
