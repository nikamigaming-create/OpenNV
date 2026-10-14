using OpenNV.Runtime.World.Physics;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutPlayerControllerContextTrace(int QueryOrdinal,
    IReadOnlyList<FalloutControllerPostProcessRead> Fields, bool ComparedEqual,
    bool NullObjectReferenceCalled, bool Returned, string? Failure);

internal static partial class FalloutPlayerControllerContextConsumers
{
    // Source field reads remain separate calls. First uses A then B, repeats
    // B only after unequal values; second reads its independent object slot
    // and calls the existing reference converter even when that slot is null.
    internal static FalloutPlayerControllerContextTrace Execute(FalloutControllerPostProcessInvocation invocation,
        Func<FalloutControllerPostProcessInvocation, FalloutControllerPostProcessField, Action<FalloutControllerPostProcessRead>, FalloutControllerPostProcessRead> read,
        Func<FalloutFormKey?> readNullObjectReference,
        Action<FalloutPlayerControllerContextTrace> observe)
    {
        ArgumentNullException.ThrowIfNull(read); ArgumentNullException.ThrowIfNull(readNullObjectReference); ArgumentNullException.ThrowIfNull(observe);
        if (invocation.SourceScope != FalloutControllerPlayerSourceScope.PostProcessContext ||
            invocation.QueryOrdinal is not (0 or 1) || invocation.Context.Context is null)
            throw new InvalidDataException("Nonnull controller continuation lacks its original entered getter.");
        var fields = new List<FalloutControllerPostProcessRead>(3);
        var trace = new FalloutPlayerControllerContextTrace(invocation.QueryOrdinal, [], false, false, false, null);
        try
        {
            if (invocation.QueryOrdinal == 0)
            {
                var first = Field(FalloutControllerPostProcessField.FirstBody);
                var second = Field(FalloutControllerPostProcessField.SecondBody);
                if (first.Value == second.Value)
                {
                    trace = trace with { ComparedEqual = true, Returned = true }; observe(trace); return trace;
                }
                var repeated = Field(FalloutControllerPostProcessField.SecondBody);
                if (repeated.Value is null) { trace = trace with { Returned = true }; observe(trace); return trace; }
                throw new NotSupportedException("Current controller's unequal nonnull body requires its actual body/node/reference consumer.");
            }
            var other = Field(FalloutControllerPostProcessField.Object);
            if (other.Value is not null)
                throw new NotSupportedException("Current controller object requires its actual retained object/phantom/node consumer.");
            trace = trace with { NullObjectReferenceCalled = true }; observe(trace);
            var reference = readNullObjectReference();
            if (reference is not null) throw new InvalidDataException("Source null object-reference conversion returned an invented reference.");
            trace = trace with { Returned = true }; observe(trace); return trace;
        }
        catch (Exception error) { trace = trace with { Returned = false, Failure = error.Message }; observe(trace); throw; }

        FalloutControllerPostProcessRead Field(FalloutControllerPostProcessField selected)
        {
            FalloutControllerPostProcessRead? partial = null;
            var before = fields.Count;
            try
            {
                var result = read(invocation, selected, actual => { partial = actual; });
                if (!result.Returned || result.Failure is not null || result.Invocation != invocation || result.Controller != invocation.Context.Context ||
                    result.Field != selected || !MatchesControllerLineage(result.Lineage, invocation))
                    throw new InvalidDataException("Controller field child omitted its real source scope/return.");
                fields.Add(result); trace = trace with { Fields = fields.ToArray() }; observe(trace); return result;
            }
            catch
            {
                if (partial is not null && fields.Count == before)
                { fields.Add(partial); trace = trace with { Fields = fields.ToArray() }; observe(trace); }
                throw;
            }
        }
    }

    private static bool MatchesControllerLineage(FalloutEarlyControllerLineage lineage, FalloutControllerPostProcessInvocation invocation) =>
        lineage.Actor == invocation.Context.Process.Actor && lineage.CommonProcess == invocation.Context.Process.Process &&
        lineage.Epoch == invocation.Context.Process.Epoch && lineage.RuntimeProcess == invocation.RuntimeProcess &&
        lineage.ThreadProcess == invocation.PlayerProcess && lineage.Constructor == invocation.Constructor &&
        lineage.Thread == invocation.ThreadId && lineage.Stack == invocation.Context.Process.Stack && lineage.Level == invocation.Context.Process.Level;
}
