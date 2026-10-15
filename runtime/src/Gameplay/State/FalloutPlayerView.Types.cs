using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Gameplay.State;

internal enum FalloutPlayerViewWriter { LoadTemporary, LoadRestore, FirstTailToggle, SecondTailToggle }
internal sealed record FalloutPlayerViewRead(string Source, string Stack, Guid Process, Guid Constructor,
    long Sequence, byte Value, FalloutReferenceModelDataInvocation? Load = null,
    FalloutPlayerLateFrameInvocation? Frame = null);
internal sealed record FalloutPlayerViewStore(string Source, Guid Consumer, FalloutPlayerViewWriter Writer,
    FalloutPlayerViewRead Read, long Changed, byte After);
internal sealed record FalloutPlayerViewSpan(FalloutPlayerViewRead Initial,
    FalloutPlayerViewStore First, FalloutPlayerViewStore? Second = null);
internal sealed record FalloutPlayerViewPublication(Guid Process, Guid Constructor, long Published,
    byte Value, Guid? PreviousProcess = null, Guid? PreviousConstructor = null);
internal sealed record FalloutPlayerViewSnapshot(string Source, string Stack, Guid Process, Guid Constructor,
    long Sequence, byte Value, IReadOnlyList<FalloutPlayerViewPublication> Publications,
    FalloutPlayerViewSpan? Load, FalloutPlayerViewSpan? Tail, FalloutPlayerViewStore? LastWrite);
