namespace OpenNV.Runtime.Content;

// Control flow consumes source statement counts and current branch-slot flags.
// Lexical SCTX nesting never rewrites or admits the original instruction stream.
internal sealed partial class FalloutCompiledControlFlow
{
    internal IReadOnlyList<FalloutCompiledInstruction> Instructions { get; }
    private readonly IReadOnlyDictionary<int, int> _skips;
    private readonly int _emptyBoundary;
    private FalloutCompiledControlFlow(IReadOnlyList<FalloutCompiledInstruction> instructions,
        IReadOnlyDictionary<int, int> skips, int emptyBoundary)
    { Instructions = instructions; _skips = skips; _emptyBoundary = emptyBoundary; }

    internal static FalloutCompiledControlFlow Read(IEnumerable<FalloutCompiledInstruction> source, int emptyBoundary = 0) =>
        ReadSourceStructure(source, emptyBoundary);

    internal IEnumerable<bool> Execute(Func<ReadOnlyMemory<byte>, FalloutScriptValue> evaluate,
        Action<FalloutCompiledInstruction> apply, FalloutScriptExecutionBudget budget,
        Action<FalloutCompiledInstruction>? reached = null) =>
        ExecuteSourceCursor(evaluate, apply, budget, new(InitialCursor), reached ?? (_ => { }));
}
