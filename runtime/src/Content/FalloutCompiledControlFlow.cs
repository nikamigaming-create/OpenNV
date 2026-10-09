namespace OpenNV.Runtime.Content;

// Branch skips count framed statements after the branch header. Each target
// must be the next alternative or the matching ENDIF in the same scope.
internal sealed partial class FalloutCompiledControlFlow
{
    internal IReadOnlyList<FalloutCompiledInstruction> Instructions { get; }
    private readonly IReadOnlyDictionary<int, int> _alternatives;
    private readonly IReadOnlyDictionary<int, int> _ends;
    private readonly int _emptyBoundary;
    private FalloutCompiledControlFlow(IReadOnlyList<FalloutCompiledInstruction> instructions,
        IReadOnlyDictionary<int, int> alternatives, IReadOnlyDictionary<int, int> ends, int emptyBoundary)
    { Instructions = instructions; _alternatives = alternatives; _ends = ends; _emptyBoundary = emptyBoundary; }

    internal static FalloutCompiledControlFlow Read(IEnumerable<FalloutCompiledInstruction> source, int emptyBoundary = 0)
    {
        var instructions = source.ToArray();
        var stack = new Stack<(List<int> Arms, bool HasElse)>();
        var alternatives = new Dictionary<int, int>();
        var ends = new Dictionary<int, int>();
        for (var index = 0; index < instructions.Length; ++index)
        {
            var instruction = instructions[index];
            if (instruction.Opcode is not (0x16 or 0x17 or 0x18 or 0x19)) continue;
            if (instruction.Receiver is not null)
                throw new InvalidDataException("Compiled branch has an invalid calling reference.");
            var operand = new FalloutCompiledOperandCursor(instruction.Payload);
            if (instruction.Opcode == 0x19)
            {
                operand.RequireEnd();
                if (!stack.TryPop(out var group)) throw new InvalidDataException("Compiled ENDIF has no IF.");
                var chain = group.Arms.Append(index).ToArray();
                for (var arm = 0; arm < chain.Length - 1; ++arm)
                {
                    var from = chain[arm]; var target = chain[arm + 1];
                    var count = new FalloutCompiledOperandCursor(instructions[from].Payload).UInt16();
                    if (from + 1 + count != target)
                        throw new InvalidDataException("Compiled branch skip differs from its exact next statement boundary.");
                    alternatives.Add(from, target); ends.Add(from, index);
                }
                continue;
            }
            _ = operand.UInt16();
            if (instruction.Opcode == 0x17) operand.RequireEnd();
            else { _ = operand.Bytes(operand.UInt16()); operand.RequireEnd(); }
            if (instruction.Opcode == 0x16)
            {
                if (stack.Count >= 128) throw new NotSupportedException("Compiled branch nesting exceeds its finite bound.");
                stack.Push(([index], false));
            }
            else
            {
                if (!stack.TryPop(out var group) || group.HasElse)
                    throw new InvalidDataException("Compiled alternative has no matching open IF or follows ELSE.");
                group.Arms.Add(index); stack.Push((group.Arms, instruction.Opcode == 0x17));
            }
        }
        if (stack.Count != 0) throw new InvalidDataException("Compiled IF has no exact ENDIF.");
        return new(instructions, alternatives, ends, emptyBoundary);
    }

    internal IEnumerable<bool> Execute(Func<ReadOnlyMemory<byte>, FalloutScriptValue> evaluate,
        Action<FalloutCompiledInstruction> apply, FalloutScriptExecutionBudget budget,
        Action<FalloutCompiledInstruction>? reached = null)
    {
        var branches = new Stack<bool>();
        var index = 0;
        while (index < Instructions.Count)
        {
            budget.Spend();
            var instruction = Instructions[index]; reached?.Invoke(instruction);
            var operands = new FalloutCompiledOperandCursor(instruction.Payload);
            switch (instruction.Opcode)
            {
                case 0x16:
                    _ = operands.UInt16();
                    var condition = evaluate(operands.Bytes(operands.UInt16()));
                    operands.RequireEnd();
                    if (condition.Kind != FalloutScriptValueKind.Number)
                        throw new NotSupportedException("Compiled IF requires an owned numeric predicate.");
                    branches.Push(condition.Number != 0);
                    index = condition.Number != 0 ? index + 1 : _alternatives[index];
                    break;
                case 0x18:
                    if (branches.Peek()) { index = _ends[index]; break; }
                    _ = operands.UInt16();
                    var alternative = evaluate(operands.Bytes(operands.UInt16()));
                    operands.RequireEnd();
                    if (alternative.Kind != FalloutScriptValueKind.Number)
                        throw new NotSupportedException("Compiled ELSEIF requires an owned numeric predicate.");
                    _ = branches.Pop(); branches.Push(alternative.Number != 0);
                    index = alternative.Number != 0 ? index + 1 : _alternatives[index];
                    break;
                case 0x17:
                    if (branches.Peek()) index = _ends[index];
                    else { _ = branches.Pop(); branches.Push(true); ++index; }
                    break;
                case 0x19:
                    _ = branches.Pop(); ++index; break;
                case 0x1e:
                    if (instruction.Receiver is not null || !instruction.Payload.IsEmpty)
                        throw new InvalidDataException("Compiled Return has an invalid payload/receiver.");
                    // Return is a reached, committed instruction even when it
                    // is the entire result. The shared invocation must retain
                    // that prefix before closing; its suffix never executes.
                    yield return true;
                    yield break;
                default:
                    apply(instruction); ++index; break;
            }
            yield return true;
        }
        if (branches.Count != 0) throw new InvalidDataException("Compiled execution left an open branch.");
    }
}
