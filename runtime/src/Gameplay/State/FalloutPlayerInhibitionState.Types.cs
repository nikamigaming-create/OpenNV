namespace OpenNV.Runtime.Gameplay.State;

// Lane names retain the source ordering already used by movement. They are
// independent source bytes, not control permissions or action Boolean aliases.
internal enum FalloutPlayerInhibitionLane { First, Second, Third, Fourth }
internal enum FalloutPlayerInhibitionOperation { ConstructorLiteral, ConstructorCompletedLoop, SelectedVoidCallback, ZeroSetter, UnownedWriter }
internal enum FalloutPlayerInhibitionConsumer { Movement, Following }
internal sealed record FalloutPlayerSourceCaller(Guid Main, long Ordinal, Guid Invocation);
internal sealed record FalloutPlayerInhibitionByte(byte Value, bool Available);
internal sealed record FalloutPlayerInhibitionEntry(Guid Observation, string Producer, Guid Process, Guid Factory,
    long Changed, FalloutPlayerInhibitionOperation Operation, FalloutPlayerInhibitionLane Lane,
    byte? Value, bool? CallbackArgument, bool CallbackReturned);
internal sealed record FalloutPlayerInhibitionRead(string Source, string Stack, Guid Process, Guid Factory,
    FalloutPlayerSourceCaller Caller, FalloutPlayerInhibitionConsumer Consumer, long Revision,
    FalloutPlayerInhibitionLane Lane, byte Value);
internal sealed record FalloutPlayerInhibitionPredicate(IReadOnlyList<FalloutPlayerInhibitionRead> Reads, bool Result);
internal sealed record FalloutPlayerInhibitionHandoff(Guid PreviousProcess, Guid Process, Guid PreviousFactory, Guid Factory);
internal sealed record FalloutPlayerFieldPublication(Guid Process, Guid Factory, Guid Constructor);
internal sealed record FalloutPlayerInhibitionSnapshot(string Schema, string Source, string Stack, Guid Process,
    Guid Factory, Guid Constructor, long Sequence, IReadOnlyList<FalloutPlayerInhibitionByte> Bytes,
    IReadOnlyList<FalloutPlayerInhibitionEntry> History, FalloutPlayerInhibitionHandoff? Cold, bool Retired,
    IReadOnlyList<FalloutPlayerFieldPublication>? Publications = null);
