namespace OpenNV.Runtime.Content;

// Retained decisions are source control-flow evidence, never a command/effect
// replay. They permit exact cold-prefix validation without re-evaluation.
internal sealed record FalloutCompiledBranchDecision(int Offset, bool? Predicate);

internal sealed partial class FalloutCompiledControlFlow
{
    private static FalloutCompiledControlFlow ReadSourceStructure(
        IEnumerable<FalloutCompiledInstruction> source, int emptyBoundary)
    {
        var instructions = source.ToArray();
        var skips = new Dictionary<int, int>();
        for (var index = 0; index < instructions.Length; ++index)
        {
            var instruction = instructions[index];
            if (instruction.Offset < 0 || instruction.End <= instruction.Offset ||
                index > 0 && instructions[index - 1].End != instruction.Offset)
                throw new InvalidDataException("Compiled structural range is not contiguous original instruction extents.");
            if (instruction.Opcode is not (0x16 or 0x17 or 0x18 or 0x19)) continue;
            if (instruction.Receiver is not null)
                throw new InvalidDataException("Compiled branch has an invalid calling reference.");
            var operand = new FalloutCompiledOperandCursor(instruction.Payload);
            if (instruction.Opcode == 0x19) { operand.RequireEnd(); continue; }
            var count = unchecked((short)operand.UInt16());
            if (instruction.Opcode != 0x17)
            {
                var expression = operand.UInt16();
                if (expression > short.MaxValue)
                    throw new NotSupportedException("Compiled branch has an unowned signed expression extent.");
                _ = operand.Bytes(expression);
            }
            operand.RequireEnd();
            // The original runner decrements a nonzero signed skip counter for
            // each framed statement. Negative/escaping counters require the
            // cross-event runner; this event-local owner cannot invent it.
            var target = index + 1 + count;
            if (count < 0 || target > instructions.Length)
                throw new NotSupportedException("Compiled signed statement skip requires its cross-event runner owner.");
            skips.Add(index, target);
        }
        return new(instructions, skips, emptyBoundary);
    }

    private static bool Taken(IReadOnlyList<FalloutCompiledBranchCursor> frames) =>
        frames.Count != 0 && frames[^1].Taken;

    private int ApplySourceBranch(int index, List<FalloutCompiledBranchCursor> frames, bool? predicate)
    {
        var instruction = Instructions[index];
        switch (instruction.Opcode)
        {
            case 0x16:
                if (predicate is null) throw new InvalidDataException("Compiled IF decision is absent.");
                // A zero base slot is reused. A nonzero current slot selects
                // the next physical slot, including a base ELSE/ELSEIF flag.
                if (frames.Count >= 10)
                    throw new NotSupportedException("Reached compiled IF exceeds the original ten-slot branch storage.");
                frames.Add(new(instruction.Offset, predicate.Value, HasIf: true));
                return predicate.Value ? index + 1 : _skips[index];
            case 0x18:
                if (Taken(frames))
                {
                    if (predicate is not null) throw new InvalidDataException("Taken compiled ELSEIF has a manufactured predicate.");
                    return _skips[index];
                }
                if (predicate is null) throw new InvalidDataException("Compiled ELSEIF decision is absent.");
                if (frames.Count != 0) frames[^1] = frames[^1] with { Taken = predicate.Value };
                else if (predicate.Value) frames.Add(new(instruction.Offset, true, HasIf: false));
                return predicate.Value ? index + 1 : _skips[index];
            case 0x17:
                if (predicate is not null) throw new InvalidDataException("Compiled ELSE has a manufactured predicate.");
                if (Taken(frames)) return _skips[index];
                if (frames.Count != 0) frames[^1] = frames[^1] with { Taken = true };
                else frames.Add(new(instruction.Offset, true, HasIf: false));
                return index + 1;
            case 0x19:
                if (predicate is not null) throw new InvalidDataException("Compiled ENDIF has a manufactured predicate.");
                // ENDIF clears the current slot. The original depth decrement
                // clamps at zero; an already clear base remains clear.
                if (frames.Count != 0) frames.RemoveAt(frames.Count - 1);
                return index + 1;
            default: throw new InvalidDataException("Structural decision does not name a source branch.");
        }
    }

    private void ValidateSourceCursor(FalloutCompiledCursorSnapshot state)
    {
        if (state is null || state.Branches is null || state.ControlHistory is null ||
            state.CommittedInstructions < 0 || state.CommittedInstructions > Instructions.Count ||
            state.BudgetSpent < state.CommittedInstructions || state.BudgetSpent > 100_000 ||
            state.ControlHistory.Count > state.CommittedInstructions)
            throw new InvalidDataException("Compiled cursor instruction/budget/control ownership is invalid.");
        var frames = new List<FalloutCompiledBranchCursor>();
        var historyIndex = 0;
        var index = 0;
        var completed = Instructions.Count == 0;
        for (var committed = 0; committed < state.CommittedInstructions; ++committed)
        {
            if (completed || index >= Instructions.Count)
                throw new InvalidDataException("Compiled cursor claims an instruction after its retired suffix.");
            var instruction = Instructions[index];
            if (instruction.Opcode is 0x16 or 0x17 or 0x18 or 0x19)
            {
                if (historyIndex >= state.ControlHistory.Count ||
                    state.ControlHistory[historyIndex] is not { } decision || decision.Offset != instruction.Offset)
                    throw new InvalidDataException("Compiled cursor lacks its exact ordered branch decisions.");
                ++historyIndex;
                index = ApplySourceBranch(index, frames, decision.Predicate);
            }
            else if (instruction.Opcode == 0x1e)
            {
                RequireReturn(instruction); frames.Clear(); index = Instructions.Count;
            }
            else ++index;
            completed = index == Instructions.Count && frames.Count == 0;
        }
        var next = index == Instructions.Count ? Boundary : Instructions[index].Offset;
        if (state.Completed != completed || state.NextOffset != next ||
            historyIndex != state.ControlHistory.Count || !frames.SequenceEqual(state.Branches))
            throw new InvalidDataException("Compiled cursor differs from its source path, branch flags or committed prefix.");
    }

    private static void RequireReturn(FalloutCompiledInstruction instruction)
    {
        if (instruction.Receiver is not null || !instruction.Payload.IsEmpty)
            throw new InvalidDataException("Compiled Return has an invalid payload/receiver.");
    }

    private IEnumerable<bool> ExecuteSourceCursor(Func<ReadOnlyMemory<byte>, FalloutScriptValue> evaluate,
        Action<FalloutCompiledInstruction> apply, FalloutScriptExecutionBudget budget,
        FalloutCompiledExecutionCursor cursor, Action<FalloutCompiledInstruction> reached)
    {
        ValidateSourceCursor(cursor.State);
        var budgetBefore = cursor.State.BudgetSpent;
        while (!cursor.State.Completed)
        {
            if (cursor.State.NextOffset == Boundary)
                throw new NotSupportedException("Reached compiled branch remains live at an event boundary; its cross-event runner owner is absent.");
            var index = Index(cursor.State.NextOffset);
            var instruction = Instructions[index];
            var frames = cursor.State.Branches.ToList();
            var history = cursor.State.ControlHistory!.ToList();
            budget.Spend(); reached(instruction);
            if (instruction.Opcode is 0x16 or 0x17 or 0x18 or 0x19)
            {
                bool? predicate = null;
                if (instruction.Opcode == 0x16 || instruction.Opcode == 0x18 && !Taken(frames))
                {
                    var operands = new FalloutCompiledOperandCursor(instruction.Payload);
                    _ = operands.UInt16();
                    var value = evaluate(operands.Bytes(operands.UInt16())); operands.RequireEnd();
                    if (value.Kind != FalloutScriptValueKind.Number)
                        throw new NotSupportedException("Reached compiled branch requires its actual numeric predicate owner.");
                    predicate = value.Number != 0;
                }
                index = ApplySourceBranch(index, frames, predicate);
                history.Add(new(instruction.Offset, predicate));
            }
            else if (instruction.Opcode == 0x1e)
            {
                RequireReturn(instruction); frames.Clear(); index = Instructions.Count;
            }
            else { apply(instruction); ++index; }
            var exhausted = index == Instructions.Count;
            var completed = exhausted && frames.Count == 0;
            // A returned last command commits before an unowned cross-event
            // boundary is attempted. The boundary refusal retains that prefix.
            cursor.Commit(exhausted ? Boundary : Instructions[index].Offset, completed, frames,
                checked(budgetBefore + budget.Spent), history);
            yield return true;
        }
    }
}
