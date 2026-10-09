namespace OpenNV.Runtime.Content;

internal sealed record FalloutCompiledBranchCursor(int IfOffset, bool Taken);
internal sealed record FalloutCompiledCursorSnapshot(int NextOffset, int CommittedInstructions,
    int BudgetSpent, bool Completed, IReadOnlyList<FalloutCompiledBranchCursor> Branches);

// This cursor is updated only after a reached instruction returns. Branch
// decisions are retained; restoring a cursor never re-evaluates its prefix.
internal sealed class FalloutCompiledExecutionCursor(FalloutCompiledCursorSnapshot state)
{
    internal FalloutCompiledCursorSnapshot State { get; private set; } = state;
    internal void Commit(int next, bool completed, IReadOnlyList<FalloutCompiledBranchCursor> branches, int spent) =>
        State = new(next, checked(State.CommittedInstructions + 1), spent, completed, branches.ToArray());
}

internal sealed partial class FalloutCompiledControlFlow
{
    private int Boundary => Instructions.Count == 0 ? _emptyBoundary : Instructions[^1].End;
    internal FalloutCompiledCursorSnapshot InitialCursor => new(
        Instructions.Count == 0 ? Boundary : Instructions[0].Offset, 0, 0, Instructions.Count == 0, []);

    internal void ValidateCursor(FalloutCompiledCursorSnapshot state)
    {
        if (state is null || state.Branches is null || state.CommittedInstructions < 0 ||
            state.CommittedInstructions > Instructions.Count || state.BudgetSpent < state.CommittedInstructions ||
            state.BudgetSpent > 100_000)
            throw new InvalidDataException("Compiled cursor instruction/budget ownership is invalid.");
        if (state.Completed)
        {
            if (state.NextOffset != Boundary || state.Branches.Count != 0 || Instructions.Count != 0 && state.CommittedInstructions == 0)
                throw new InvalidDataException("Completed compiled cursor has a live suffix/branch.");
            return;
        }
        var index = Index(state.NextOffset);
        if (state.CommittedInstructions == 0 && (index != 0 || state.Branches.Count != 0 || state.BudgetSpent != 0))
            throw new InvalidDataException("Unentered compiled cursor has a consumed prefix.");
        var open = Enumerable.Range(0, Instructions.Count).Where(candidate =>
            Instructions[candidate].Opcode == 0x16 && candidate < index && _ends[candidate] >= index)
            .Select(candidate => Instructions[candidate].Offset).ToArray();
        if (!open.SequenceEqual(state.Branches.Select(branch => branch.IfOffset)))
            throw new InvalidDataException("Compiled cursor branch stack differs from its exact scope/offset.");
        foreach (var branch in state.Branches.Where(branch => !branch.Taken))
        {
            // An untaken branch can only stop at its next ELSEIF/ELSE/ENDIF
            // boundary. Its body cannot be entered until a predicate or ELSE
            // has genuinely selected it; restoring never re-tests that predicate.
            var beginning = Index(branch.IfOffset);
            var alternative = _alternatives[beginning];
            while (alternative != index && Instructions[alternative].Opcode == 0x18)
                alternative = _alternatives[alternative];
            if (alternative != index && _ends[beginning] != index)
                throw new InvalidDataException("Compiled cursor enters the body of an untaken branch.");
        }
    }

    private int Index(int offset)
    {
        for (var index = 0; index < Instructions.Count; ++index)
            if (Instructions[index].Offset == offset) return index;
        throw new InvalidDataException("Compiled cursor is not an instruction boundary in its event range.");
    }

    internal IEnumerable<bool> ExecuteResumable(Func<ReadOnlyMemory<byte>, FalloutScriptValue> evaluate,
        Action<FalloutCompiledInstruction> apply, FalloutScriptExecutionBudget budget,
        FalloutCompiledExecutionCursor cursor, Action<FalloutCompiledInstruction> reached)
    {
        ValidateCursor(cursor.State);
        var budgetBefore = cursor.State.BudgetSpent;
        while (!cursor.State.Completed)
        {
            var index = Index(cursor.State.NextOffset);
            var instruction = Instructions[index];
            var branches = cursor.State.Branches.ToList();
            var completed = false;
            budget.Spend(); reached(instruction);
            var operands = new FalloutCompiledOperandCursor(instruction.Payload);
            switch (instruction.Opcode)
            {
                case 0x16:
                    _ = operands.UInt16();
                    var condition = evaluate(operands.Bytes(operands.UInt16())); operands.RequireEnd();
                    if (condition.Kind != FalloutScriptValueKind.Number)
                        throw new NotSupportedException("Compiled IF requires an owned numeric predicate.");
                    branches.Add(new(instruction.Offset, condition.Number != 0));
                    index = condition.Number != 0 ? index + 1 : _alternatives[index];
                    break;
                case 0x18:
                    if (branches[^1].Taken) { index = _ends[index]; break; }
                    _ = operands.UInt16();
                    var alternative = evaluate(operands.Bytes(operands.UInt16())); operands.RequireEnd();
                    if (alternative.Kind != FalloutScriptValueKind.Number)
                        throw new NotSupportedException("Compiled ELSEIF requires an owned numeric predicate.");
                    branches[^1] = branches[^1] with { Taken = alternative.Number != 0 };
                    index = alternative.Number != 0 ? index + 1 : _alternatives[index];
                    break;
                case 0x17:
                    if (branches[^1].Taken) index = _ends[index];
                    else { branches[^1] = branches[^1] with { Taken = true }; ++index; }
                    break;
                case 0x19:
                    branches.RemoveAt(branches.Count - 1); ++index; break;
                case 0x1e:
                    if (instruction.Receiver is not null || !instruction.Payload.IsEmpty)
                        throw new InvalidDataException("Compiled Return has an invalid payload/receiver.");
                    completed = true; branches.Clear(); index = Instructions.Count; break;
                default:
                    apply(instruction); ++index; break;
            }
            completed |= index == Instructions.Count;
            if (completed && branches.Count != 0)
                throw new InvalidDataException("Compiled execution left an open branch.");
            cursor.Commit(completed ? Boundary : Instructions[index].Offset, completed, branches,
                checked(budgetBefore + budget.Spent));
            yield return true;
        }
    }
}
