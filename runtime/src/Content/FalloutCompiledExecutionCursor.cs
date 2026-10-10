namespace OpenNV.Runtime.Content;

internal sealed record FalloutCompiledBranchCursor(int IfOffset, bool Taken, bool HasIf = true);
internal sealed record FalloutCompiledCursorSnapshot(int NextOffset, int CommittedInstructions,
    int BudgetSpent, bool Completed, IReadOnlyList<FalloutCompiledBranchCursor> Branches,
    IReadOnlyList<FalloutCompiledBranchDecision>? ControlHistory = null)
{
    internal FalloutCompiledCursorSnapshot Copy() => this with
    { Branches = Branches.ToArray(), ControlHistory = ControlHistory?.ToArray() };
}

// This cursor changes only after the reached instruction returns. The source
// flag slot and each evaluated decision survive cold resume without evaluation.
internal sealed class FalloutCompiledExecutionCursor(FalloutCompiledCursorSnapshot state)
{
    internal FalloutCompiledCursorSnapshot State { get; private set; } = state.Copy();
    internal void Commit(int next, bool completed, IReadOnlyList<FalloutCompiledBranchCursor> branches, int spent,
        IReadOnlyList<FalloutCompiledBranchDecision>? controlHistory = null) =>
        State = new(next, checked(State.CommittedInstructions + 1), spent, completed, branches.ToArray(),
            (controlHistory ?? State.ControlHistory)?.ToArray());
}

internal sealed partial class FalloutCompiledControlFlow
{
    private int Boundary => Instructions.Count == 0 ? _emptyBoundary : Instructions[^1].End;
    internal FalloutCompiledCursorSnapshot InitialCursor => new(
        Instructions.Count == 0 ? Boundary : Instructions[0].Offset, 0, 0, Instructions.Count == 0, [], []);

    internal void ValidateCursor(FalloutCompiledCursorSnapshot state) => ValidateSourceCursor(state);

    private int Index(int offset)
    {
        for (var index = 0; index < Instructions.Count; ++index)
            if (Instructions[index].Offset == offset) return index;
        throw new InvalidDataException("Compiled cursor is not an instruction boundary in its event range.");
    }

    internal IEnumerable<bool> ExecuteResumable(Func<ReadOnlyMemory<byte>, FalloutScriptValue> evaluate,
        Action<FalloutCompiledInstruction> apply, FalloutScriptExecutionBudget budget,
        FalloutCompiledExecutionCursor cursor, Action<FalloutCompiledInstruction> reached) =>
        ExecuteSourceCursor(evaluate, apply, budget, cursor, reached);
}
