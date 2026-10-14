using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal enum FalloutReferenceChangeStep
{ Entered, ThreadFlags, ManagerFlags, ReferenceFlags, DataHandler, Lookup, EntryConstructed, Inserted, MaskStore, Returned, ManagerFlagStore, DataStore, UnownedWriter }
internal sealed record FalloutReferenceChangeEntry(Guid Identity, uint SourceFormId, FalloutFormKey Reference,
    uint Flags, Guid? SerializedData);
internal sealed record FalloutReferenceChangeStore(FalloutReferenceChangeStep Step, long Changed,
    uint? Bits = null, Guid? Entry = null, Guid? Data = null);
internal sealed record FalloutReferenceChangeCall(FalloutReferencePositionInvocation Invocation, string Source,
    Guid Factory, long Entered, long Changed, FalloutReferenceChangeStep Step, uint Mask, bool Force,
    uint ManagerBefore, uint ManagerAfter, FalloutReferenceFlagRead? ReferenceFlags,
    FalloutSourceThreadReferenceFlagsRead? ThreadFlags, uint? SourceFormId,
    FalloutReferenceChangeEntry? Before, FalloutReferenceChangeEntry? After,
    IReadOnlyList<FalloutReferenceChangeStore> Stores, bool Returned, string? Failure,
    bool? DataHandlerSuppressed = null, bool? Suppressed = null, string? UnownedWriter = null,
    string Kind = "reference-change");
internal sealed record FalloutReferenceChangePublication(Guid Process, Guid Factory, Guid Constructor,
    Guid? PreviousProcess = null, Guid? PreviousFactory = null);
internal sealed record FalloutReferenceChangeSnapshot(string Schema, FalloutReferencePositionSource Source,
    string Stack, Guid Process, Guid Factory, Guid Constructor, long Sequence, uint ManagerFlags,
    IReadOnlyList<FalloutReferenceChangePublication> Publications, IReadOnlyList<FalloutReferenceChangeEntry> Entries,
    IReadOnlyList<IReadOnlyList<Guid>> Buckets, IReadOnlyList<FalloutReferenceChangeCall> Calls,
    string? WriterBoundary, bool Retired, IReadOnlyList<FalloutReferenceChangeEntry>? PendingEntries = null,
    IReadOnlyList<FalloutReferenceChangeDataSnapshot>? Data = null);
internal sealed record FalloutReferenceChangeDataSnapshot(Guid Identity, string Producer, byte[] Bytes);

// This is actual retained data storage. Pointer presence is meaningful even
// before its independent serializer/layout consumer has returned.
internal sealed class FalloutReferenceChangeData
{
    private readonly byte[] _bytes;
    internal Guid Identity { get; }
    internal string Producer { get; }
    internal FalloutReferenceChangeData(string producer, ReadOnlySpan<byte> bytes)
        : this(new(Guid.NewGuid(), producer, bytes.ToArray())) { }
    internal FalloutReferenceChangeData(FalloutReferenceChangeDataSnapshot saved)
    {
        if (saved.Identity == Guid.Empty || string.IsNullOrWhiteSpace(saved.Producer) || saved.Bytes is not { Length: > 0 })
            throw new InvalidDataException("Change data lacks its real retained source buffer.");
        Identity = saved.Identity; Producer = saved.Producer; _bytes = saved.Bytes.ToArray();
    }
    internal FalloutReferenceChangeDataSnapshot Capture() => new(Identity, Producer, _bytes.ToArray());
}

internal interface IFalloutReferenceChangeConsumers
{
    Guid Factory { get; }
    void RequireCurrent(FalloutReferencePositionInvocation invocation);
    FalloutSourceThreadReferenceFlagsRead ReadThreadFlags(FalloutReferencePositionInvocation invocation);
    FalloutReferenceFlagRead ReadReferenceFlags(FalloutReferencePositionInvocation invocation);
    uint ReadSourceFormId(FalloutReferencePositionInvocation invocation);
    bool ReadDataHandlerSuppression(FalloutReferencePositionInvocation invocation, uint sourceFormId);
}
