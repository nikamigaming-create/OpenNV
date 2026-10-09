using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal static class FalloutSourceQueuedReferenceDispatch
{
    internal static FalloutSourceQueuedRoute Read(FalloutMainFrameDeclaration source, bool permitsInline,
        Func<FalloutActorProcessFact<uint>> callingThread, Func<FalloutActorProcessFact<uint>> mainThread,
        Func<FalloutActorProcessFact<bool>> independentInlineGate, string owner)
    {
        source.Validate(); ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        // The ordinary reference Load3D caller passes false. Neither its
        // thread nor the additional original gate is consumed on that arm.
        if (!permitsInline) return new(FalloutSourceQueuedDispatch.Queued, owner, null, null);
        ArgumentNullException.ThrowIfNull(callingThread); ArgumentNullException.ThrowIfNull(mainThread);
        var current = callingThread().Require(); var main = mainThread().Require();
        if (current == 0 || main == 0) throw new InvalidDataException("Source loader thread identity cannot be a null OS thread.");
        if (current != main) return new(FalloutSourceQueuedDispatch.Queued, owner, current, main);
        ArgumentNullException.ThrowIfNull(independentInlineGate);
        return new(independentInlineGate().Require() ? FalloutSourceQueuedDispatch.Queued : FalloutSourceQueuedDispatch.Inline,
            owner, current, main);
    }
}
