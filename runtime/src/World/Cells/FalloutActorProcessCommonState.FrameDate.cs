using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutActorProcessCommonState
{
    // PackedDateWord is a new field in the existing Common scalar record.
    // It must be constructed/copied/persisted with that record, not read from
    // FirstWord or from the floating process clock by a convenient alias.
    internal FalloutPlayerLatePackedDayRead ReadPlayerFramePackedDay(FalloutPlayerLateFrameInvocation invocation,
        Action<FalloutPlayerLateFrameInvocation> requireActual)
    {
        ArgumentNullException.ThrowIfNull(requireActual); requireActual(invocation);
        var query = ReadSceneQueries(invocation.Common.Actor.Reference, invocation.Common.Epoch);
        FalloutPlayerLateActorTimeState.RequireSameProcess(invocation, query);
        if (!FalloutSourceMainFamily.IsFallout3(_source.ExecutableSha256))
            throw new NotSupportedException("source-packed-date-selected-family-unqualified");
        var word = Require(query.Actor.Reference).Scalars.PackedDateWord ??
            throw new NotSupportedException("source-packed-date-current-constructor-or-writer-unbound");
        requireActual(invocation);
        return new(query, word, checked((int)(word & FalloutPlayerLateActorTimeSource.PackedDayMask)));
    }
}
