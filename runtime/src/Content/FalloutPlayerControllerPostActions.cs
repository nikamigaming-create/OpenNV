using OpenNV.Runtime.World.Physics;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutPlayerControllerPostActionsTrace(int QueryOrdinal, FalloutControllerPostProcessRead? Body,
    FalloutControllerPostProcessFlagsRead? Flags, bool NullBodyReferenceCalled, FalloutFormKey? Reference,
    bool RemoveEight, bool ClearContextFlag, bool Returned, string? Failure);

internal static partial class FalloutPlayerControllerContextConsumers
{
    internal static FalloutPlayerControllerPostActionsTrace ExecutePostActions(FalloutControllerPostProcessInvocation invocation,
        Func<FalloutControllerPostProcessInvocation, FalloutControllerPostProcessField, Action<FalloutControllerPostProcessRead>, FalloutControllerPostProcessRead> read,
        Func<FalloutControllerPostProcessInvocation, Action<FalloutControllerPostProcessFlagsRead>, FalloutControllerPostProcessFlagsRead> readFlags,
        Func<FalloutFormKey?> readNullBodyReference, Action<FalloutPlayerControllerPostActionsTrace> observe)
    {
        ArgumentNullException.ThrowIfNull(read); ArgumentNullException.ThrowIfNull(readFlags);
        ArgumentNullException.ThrowIfNull(readNullBodyReference); ArgumentNullException.ThrowIfNull(observe);
        if (invocation.SourceScope != FalloutControllerPlayerSourceScope.PostActions ||
            invocation.QueryOrdinal is not (0 or 1) || invocation.Context.Context is null)
            throw new InvalidDataException("Post-action controller child lacks its independent entered source scope.");
        var trace = new FalloutPlayerControllerPostActionsTrace(invocation.QueryOrdinal, null, null, false, null, false, false, false, null);
        try
        {
            if (invocation.QueryOrdinal == 0)
            {
                var body = read(invocation, FalloutControllerPostProcessField.FirstBody, actual =>
                { trace = trace with { Body = actual }; observe(trace); });
                trace = trace with { Body = body }; observe(trace);
                if (!body.Returned || body.Failure is not null || body.Invocation != invocation || body.Controller != invocation.Context.Context ||
                    body.Field != FalloutControllerPostProcessField.FirstBody || !MatchesControllerLineage(body.Lineage, invocation))
                    throw new InvalidDataException("Post-action body getter omitted its actual current controller/source return.");
                if (body.Value is not null)
                    throw new NotSupportedException("source-Player-post-action-nonnull-body-node-reference-predicate-unbound");
                trace = trace with { NullBodyReferenceCalled = true }; observe(trace);
                var reference = readNullBodyReference(); trace = trace with { Reference = reference }; observe(trace);
                if (reference is not null) throw new InvalidDataException("Post-action null-body conversion returned an invented reference.");
                // Source removes action eight only after this returned null
                // conversion. The real list writer remains in the Scene owner.
                trace = trace with { RemoveEight = true, Returned = true }; observe(trace); return trace;
            }
            var flags = readFlags(invocation, actual => { trace = trace with { Flags = actual }; observe(trace); });
            trace = trace with { Flags = flags }; observe(trace);
            if (!flags.Returned || flags.Failure is not null || flags.Flags is not { } bits || flags.Revision < 0 || flags.Invocation != invocation ||
                flags.Controller != invocation.Context.Context || !MatchesControllerLineage(flags.Lineage, invocation))
                throw new InvalidDataException("Post-action flags getter omitted its actual current controller/source return.");
            if ((bits & FalloutEarlyControllerSource.PostActionContextFlag) != 0)
                throw new NotSupportedException("source-Player-post-action-controller-notification-current-byte-and-effect-unbound");
            trace = trace with { ClearContextFlag = true, Returned = true }; observe(trace); return trace;
        }
        catch (Exception error) { trace = trace with { Returned = false, Failure = error.Message }; observe(trace); throw; }
    }
}
