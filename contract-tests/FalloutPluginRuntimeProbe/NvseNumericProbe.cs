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
        Require(Evaluate("0b101 & 0x3") == 1 && Evaluate("-7.9 % 3.8") == -1 &&
            Evaluate("24 / 2 % 5 * 2") == 4 && Evaluate("1 == 3 & 1") == 1 &&
            Evaluate("1 | 6 & 3 << 1 + 1") == 5,
            "Integer truncation, literal bases or NVSE operator precedence differs.");
        Require(Evaluate("1 << 40") == 1099511627776 && Evaluate("-8 >> 2") == -2 &&
            Evaluate("(1 << 40) | 0b1") == 1099511627777 && Evaluate("0xffffffff") == uint.MaxValue,
            "Integer operators used 32-bit arithmetic or unsigned right shift.");
        Execute("other = 0b101\nother |= 0b10\nother &= 0x3\nother %= 2");
        Require(state["other"] == 1 && Evaluate("0 && (1 % 0)") == 0 &&
            Evaluate("1 || (1 << 64)") == 1, "Compound integer writes or inactive operands differ.");
        foreach (var source in new[] { "other = 3 % 2", "other |= 2", "other &= 1", "if eval 3 & 1\nendif" })
            Require(FalloutGameModeProgram.WasRejectedByParser(source, 6), "Parser migration lost a lexical integer operator rejection.");
        Require(!FalloutGameModeProgram.WasRejectedByParser("Notify \"% & |\" ; other %= 1", 6) &&
            !FalloutGameModeProgram.WasRejectedByParser("other = 0b1 << 2", 6) &&
            !FalloutGameModeProgram.WasRejectedByParser("other %= 2", 7), "Integer migration admitted an unrelated missing owner.");
        writes.Clear(); calls = 0;
        foreach (var invalid in new[] { "result := Tick 3", "result := 1 +", "2 := Tick", "result := (3 + 4",
            "result := 1 / 0", "result := 1e308 * 1e308 < 1", "result := Invalid", "result := \"text\"", "result := unknown",
            "result := 1 % 0.9", "result := 1 << 64", "result := 1 >> -1", "result := 1e100 & 1",
            "result := 0b102", "result := 0x100000000", "result := 0b", "result := \"3\" & 1" })
            Reject(() => Evaluate(invalid));
        Require(writes.Count == 0 && calls == 0, "An invalid expression committed state or ran a function before syntax validation.");
        foreach (var source in new[] { "charge = 1", "let charge := 1", "charge += 1", "charge /= 2" })
            Require(FalloutGameModeProgram.WasRejectedByParser(source, 1), "Legacy migration lost newly supported assignment syntax.");
        Require(!FalloutGameModeProgram.WasRejectedByParser("Notify \"a=b\" ; charge += 1", 1) &&
            !FalloutGameModeProgram.WasRejectedByParser("if charge == 2\nendif", 1) &&
            !FalloutGameModeProgram.WasRejectedByParser("charge = 1", 2), "Migration admitted an unrelated missing owner.");
        ReusedSourceContracts();
        Console.WriteLine("OPENNV_NVSE_NUMERIC_PASS assignments=true eval=true numericLogic=true integers=64-bit literals=32-bit inactiveEffects=false vanillaPreserved=true");
    }

    private static void ReusedSourceContracts()
    {
        var owners = new List<string>();
        var invocations = 0;
        var values = new FalloutScriptValueContext(_ => 9, (_, _) => throw new InvalidOperationException("Unexpected assignment."));
        FalloutScriptFunction UserFunction(string command, string name)
        {
            owners.Add(command + ":" + name);
            return new([], _ => { invocations++; return 7; });
        }
        foreach (var command in new[] { "call", "Receiver.CALL", "Receiver..cAlL" })
            Require(FalloutNvseNumericExpression.EvaluateValue([command, "FunctionIdentity"], values,
                userFunction: UserFunction).Number == 7, "Qualified CALL lost its last-segment semantics.");
        foreach (var token in new[] { "Receiver.call.", "Receiver.call..", "Receiver.CALL.tail" })
            Require(FalloutNvseNumericExpression.EvaluateValue([token], values, userFunction: UserFunction).Number == 9,
                "A trailing/other last segment was misclassified as CALL.");
        Require(owners.SequenceEqual(["call:FunctionIdentity", "Receiver.CALL:FunctionIdentity", "Receiver..cAlL:FunctionIdentity"]) &&
            invocations == 3, "CALL resolution changed the exact receiver spelling or invocation count.");
        Require(FalloutNvseNumericExpression.EvaluateValue(["1", "||", "Receiver.CALL", "FunctionIdentity"], values,
            userFunction: UserFunction).Number == 1 && invocations == 3, "An inactive CALL invoked its runtime owner.");
        Reject(() => FalloutNvseNumericExpression.EvaluateValue(["1", "||", "Receiver.CALL"], values, userFunction: UserFunction));
        Require(invocations == 3, "Malformed inactive CALL syntax executed a runtime owner.");

        var reusable = FalloutGameModeProgram.Read("begin GameMode\nif eval enabled\nset result to Receiver.CALL SourceFunction + value\n" +
            "else\nset result to value\nendif\nwhile remaining > 0\nlet result += 1\nlet remaining -= 1\nloop\nNotify Original\nend");
        var commandArguments = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var userFrames = new List<int>();
        for (var frame = 1; frame <= 3; frame++)
        {
            var currentFrame = frame;
            var locals = new Dictionary<string, FalloutScriptValue>
                { ["enabled"] = frame == 2 ? 0 : 1, ["result"] = 0, ["value"] = frame + 1, ["remaining"] = 2 };
            var context = new FalloutScriptValueContext(name => locals[name], (name, value) => locals[name] = value);
            FalloutScriptFunction CurrentUser(string command, string name)
            {
                Require(command == "Receiver.CALL" && name == "SourceFunction", "Program changed its current user-function identity.");
                return new([], _ => { userFrames.Add(currentFrame); return currentFrame * 10; });
            }
            reusable.Execute(name => locals[name].Number, (name, value) => locals[name] = value,
                (command, arguments) =>
                {
                    Require(command == "Notify" && arguments.SequenceEqual(["Original"]) && commandArguments.Add(arguments),
                        "A command dispatch reused caller-owned arguments or changed source text.");
                    ((string[])arguments)[0] = "Caller mutation";
                }, userFunction: CurrentUser, values: context);
            Require(locals["result"].Number == (frame == 2 ? 5 : frame * 10 + frame + 3) && locals["remaining"].Number == 0,
                "Reused source expression views retained a previous frame/context or changed branches/loop order.");
        }
        Require(userFrames.SequenceEqual([1, 3]), "Reused source retained a user-function owner or evaluated the inactive branch.");

        // Compare the same deferred operand with the old caller-owned tail
        // copy and the offset API. Neither run caches a receiver or a value.
        var tokens = new[] { "unparsed", "prefix", "(", "value", ")" }.Concat(Enumerable.Repeat("tail", 32)).ToArray();
        var operandValues = new FalloutScriptValueContext(_ => 7, (_, _) => throw new InvalidOperationException("Unexpected write."));
        long Allocated(bool copy)
        {
            double sum = 0;
            void Read()
            {
                var operand = copy ? FalloutNvseNumericExpression.ReadOperand(tokens.Skip(2).ToArray(), operandValues) :
                    FalloutNvseNumericExpression.ReadOperand(tokens, operandValues, start: 2);
                Require(operand.Count == 3, "Offset operand consumption became absolute or parsed trailing arguments.");
                sum += operand.Value().Number;
            }
            for (var index = 0; index < 256; index++) Read();
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var index = 0; index < 2048; index++) Read();
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Require(sum == 2304 * 7, "Offset/copy operand evaluation changed current values.");
            return allocated;
        }
        var copiedBytes = Allocated(true);
        var offsetBytes = Allocated(false);
        Require(offsetBytes < copiedBytes, $"Operand offsets did not remove tail-copy allocation: copied={copiedBytes}, offset={offsetBytes}.");
        var numeric = new FalloutScriptFunction([], _ => 3);
        for (var index = 0; index < 256; index++) _ = numeric.Invoke(Array.Empty<FalloutScriptArgument>());
        var invocationBefore = GC.GetAllocatedBytesForCurrentThread();
        double total = 0;
        for (var index = 0; index < 2048; index++) total += numeric.Invoke(Array.Empty<FalloutScriptArgument>());
        var invocationBytes = GC.GetAllocatedBytesForCurrentThread() - invocationBefore;
        Require(total == 6144 && invocationBytes == 0, "Vanilla numeric invocation allocated an adapter or changed the function result.");
        Console.WriteLine($"OPENNV_SCRIPT_SOURCE_REUSE_PASS callLastSegment=true inactiveSyntaxChecked=true newContexts=true dynamicUserOwners=true " +
            $"commandBuffersIndependent=true warmedOperands=2048 copiedTailBytes={copiedBytes} offsetBytes={offsetBytes} numericInvokeBytes={invocationBytes}");
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is NotSupportedException or InvalidDataException) { return; }
        throw new InvalidOperationException("An invalid NVSE numeric expression was admitted.");
    }
}
